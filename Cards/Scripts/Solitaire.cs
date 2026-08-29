using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    // Owns the slot registry that turns a synced int back into a CardSlot.
    //
    // Slot ids are handed out deterministically at startup - base slots first, in
    // inspector order, then one per pool card in pool order - so every client
    // agrees on them without any of it going over the network. That is the trick
    // that makes a linked list of object references syncable in Udon: only the id
    // travels, and the structure is rebuilt locally from it.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class Solitaire : UdonSharpBehaviour
    {
        [Header("References")]
        [Tooltip("The stock deck (DeckManager with its VRCObjectPool).")]
        public DeckManager DeckOfCards;

        [Tooltip("7 tableau columns, left to right. Cards stack face-down except the top.")]
        public CardSlot[] TableauSlots = new CardSlot[7];

        [Tooltip("4 foundation piles. Build ace to king, one suit per pile (top card only).")]
        public CardSlot[] FoundationSlots = new CardSlot[4];

        [Tooltip("Waste pile next to the stock; drawn cards land here.")]
        public CardSlot WasteSlot;

        [Header("Placement")]
        [Tooltip("How close a dropped card has to be to a slot to snap into it.")]
        public float SnapDistance = 0.12f;

        [Header("Win")]
        [Tooltip("Optional object activated when all 4 foundations are complete.")]
        public GameObject WinMessage;

        private CardLogic[] cards;
        private CardSlot[] slotsById;
        private int baseSlotCount;
        private Transform cardHome;
        private bool dealing;
        private bool gameStarted;
        private bool initialized;
        private bool won;

        // True once a deal has happened; input that depends on a running game
        // (like drawing from the stock) is gated on this.
        public bool _IsGameStarted()
        {
            if (!initialized) Init();
            return gameStarted;
        }

        // True when the local player is the one who dealt the current game. Drives
        // who is allowed to grab cards and poke the deck.
        public bool _IsLocalGameOwner()
        {
            if (!initialized) Init();
            if (DeckOfCards == null) return false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;
            return DeckOfCards.GameOwnerId == local.playerId;
        }

        private void Start()
        {
            if (!initialized) Init();
        }

        public void _EnsureInit()
        {
            if (!initialized) Init();
        }

        private void Init()
        {
            initialized = true;
            if (DeckOfCards == null)
            {
                Debug.Log("Solitaire: Deck not assigned, cannot initialize.");
                return;
            }
            VRCObjectPool pool = DeckOfCards.Pool;
            if (pool == null)
            {
                pool = DeckOfCards.GetComponent<VRCObjectPool>();
                DeckOfCards.Pool = pool;
            }
            if (pool == null)
            {
                Debug.Log("Solitaire: Deck pool not assigned, cannot initialize.");
                return;
            }
            int n = pool.Pool.Length;
            if (n <= 0)
            {
                Debug.Log("Solitaire: No cards in deck, cannot initialize.");
                return;
            }

            cardHome = pool.transform;
            DeckOfCards.Solitaire = this;

            int tableauLength = TableauSlots != null ? TableauSlots.Length : 0;
            int foundationLength = FoundationSlots != null ? FoundationSlots.Length : 0;
            // The waste always occupies the last base id, even when unassigned, so
            // ids stay stable regardless of which references happen to be filled in.
            baseSlotCount = tableauLength + foundationLength + 1;

            slotsById = new CardSlot[baseSlotCount + n];
            for (int i = 0; i < tableauLength; i++)
            {
                RegisterBaseSlot(TableauSlots[i], i);
            }
            for (int i = 0; i < foundationLength; i++)
            {
                RegisterBaseSlot(FoundationSlots[i], tableauLength + i);
            }
            RegisterBaseSlot(WasteSlot, baseSlotCount - 1);

            cards = new CardLogic[n];
            for (int i = 0; i < n; i++)
            {
                GameObject go = pool.Pool[i];
                if (go == null) continue;
                // Pool objects are inactive at startup, so this has to include them.
                CardLogic logic = go.GetComponentInChildren<CardLogic>(true);
                cards[i] = logic;
                if (logic == null) continue;

                logic.DeckManager = DeckOfCards;
                logic.Solitaire = this;

                CardSlot slot = logic.GetComponent<CardSlot>();
                if (slot == null) continue;
                int id = baseSlotCount + i;
                logic.Slot = slot;
                slot.Owner = logic;
                slot.SlotId = id;
                slot.Solitaire = this;
                slotsById[id] = slot;
            }

            // Anything that deserialized before we were ready gets caught up here.
            for (int i = 0; i < n; i++)
            {
                if (cards[i] == null) continue;
                cards[i]._ApplyPlacement();
                cards[i]._RefreshPickupable();
            }

            Debug.Log($"Solitaire: Initialized with {n} cards and {baseSlotCount} base slots.");
        }

        private void RegisterBaseSlot(CardSlot slot, int id)
        {
            slotsById[id] = slot;
            if (slot == null) return;
            slot.SlotId = id;
            slot.Solitaire = this;
            slot.Owner = null;
        }

        public CardSlot _ResolveSlot(int id)
        {
            if (!initialized) Init();
            if (slotsById == null) return null;
            if (id < 0 || id >= slotsById.Length) return null;
            return slotsById[id];
        }

        // Reverse lookup for the linked list: who points at this slot? At 54 cards a
        // linear scan is cheaper than maintaining a second synced structure.
        public CardLogic _GetCardOn(CardSlot slot)
        {
            if (slot == null || cards == null) return null;
            int id = slot.SlotId;
            if (id < 0) return null;
            for (int i = 0; i < cards.Length; i++)
            {
                CardLogic card = cards[i];
                if (card == null) continue;
                if (!card.gameObject.activeInHierarchy) continue;
                if (card.PrevSlotId == id) return card;
            }
            return null;
        }

        // Re-snap everything stacked above a card, after something changed the
        // offset it hands out (a flip, say).
        public void _RepositionAbove(CardLogic card)
        {
            if (card == null || card.Slot == null) return;
            CardSlot current = card.Slot;
            int guard = 0;
            while (guard < 64)
            {
                CardLogic above = _GetCardOn(current);
                if (above == null) return;
                above._ApplyPlacement();
                if (above.Slot == null) return;
                current = above.Slot;
                guard++;
            }
        }

        public override void Interact()
        {
            Deal();
        }

        public void Deal()
        {
            if (dealing)
            {
                Debug.Log("Solitaire: Already dealing cards.");
                return;
            }

            if (!initialized || cards == null)
            {
                Init();
                Debug.Log("Solitaire: Initialized deck on demand.");
            }

            if (DeckOfCards == null || cards == null)
            {
                Debug.Log("Solitaire: Deck not assigned, cannot deal.");
                return;
            }
            if (TableauSlots == null || FoundationSlots == null)
            {
                Debug.Log("Solitaire: Tableau or Foundation slots not assigned, cannot deal.");
                return;
            }

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local))
            {
                Debug.Log("Solitaire: No local player, cannot deal cards.");
                return;
            }

            // Only the current game owner may deal. Anyone can deal a fresh game,
            // but once one is running it's locked to the player who started it.
            if (gameStarted && DeckOfCards.GameOwnerId != local.playerId)
            {
                Debug.Log("Solitaire: Game already started by someone else; only they may deal.");
                return;
            }

            Networking.SetOwner(local, DeckOfCards.gameObject);
            DeckOfCards._SetGameOwner(local.playerId);
            dealing = true;
            gameStarted = true;
            Debug.Log($"Solitaire: Dealing cards for {local.displayName} ({local.playerId})");
            ResetCards();

            for (int col = 0; col < TableauSlots.Length && col < 7; col++)
            {
                CardSlot slot = TableauSlots[col];
                if (slot == null) continue;
                for (int depth = 0; depth <= col; depth++)
                {
                    GameObject cardGO = DeckOfCards.DrawNext();
                    if (cardGO == null)
                    {
                        FinalizeDeal();
                        return;
                    }
                    CardLogic card = cardGO.GetComponentInChildren<CardLogic>(true);
                    if (card == null) continue;
                    Networking.SetOwner(local, cardGO);
                    card._ForcePlace(slot._GetTopSlot(), depth == col);
                }
            }
            FinalizeDeal();
        }

        // One click on the stock: deal the next card, or turn the waste back over
        // once the stock has run dry.
        public void _OnStockClicked()
        {
            if (!initialized) Init();
            if (DeckOfCards == null) return;
            if (!_IsLocalGameOwner()) return;

            if (DeckOfCards._IsStockEmpty()) _RecycleWaste();
            else _DrawFromStock();
        }

        // Send the whole waste pile back to the stock, face down.
        public void _RecycleWaste()
        {
            if (!initialized) Init();
            if (DeckOfCards == null || WasteSlot == null) return;
            if (!_IsLocalGameOwner()) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            int count = WasteSlot._GetCardCount();
            if (count <= 0)
            {
                Debug.Log("Solitaire: Stock and waste are both empty, nothing to recycle.");
                return;
            }

            // Snapshot the pile first - detaching rewrites the chain we'd be walking.
            CardLogic[] pile = new CardLogic[count];
            for (int i = 0; i < count; i++) pile[i] = WasteSlot._GetCardAt(i);

            Networking.SetOwner(local, DeckOfCards.gameObject);

            // Top down, so no card is deactivated while others are still parented
            // underneath it.
            for (int i = count - 1; i >= 0; i--)
            {
                CardLogic card = pile[i];
                if (card == null) continue;
                Networking.SetOwner(local, card.gameObject);
                card._Detach(cardHome);
                DeckOfCards._ReturnCard(card.gameObject);
            }

            Debug.Log($"Solitaire: Recycled {count} cards from the waste back into the stock.");
        }

        // Draw the next stock card onto the waste pile.
        public void _DrawFromStock()
        {
            if (!initialized) Init();
            if (DeckOfCards == null || WasteSlot == null) return;
            if (!_IsLocalGameOwner()) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            GameObject cardGO = DeckOfCards.DrawNext();
            if (cardGO == null) return;
            CardLogic card = cardGO.GetComponentInChildren<CardLogic>(true);
            if (card == null) return;
            Networking.SetOwner(local, cardGO);
            card._ForcePlace(WasteSlot._GetTopSlot(), true);
        }

        private void ResetCards()
        {
            // Unlink and unparent before returning to the pool - dealt cards are
            // parented under each other, and the pool won't undo that.
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i] != null) cards[i]._Detach(cardHome);
            }
            DeckOfCards._ResetDeck();
        }

        private void FinalizeDeal()
        {
            dealing = false;
            won = false;
            if (WinMessage != null) WinMessage.SetActive(false);
        }

        public void _OnCardPickup(CardLogic card)
        {
            if (card == null) return;
            if (!initialized) Init();

            // Backstop for the pickupable gate: even if a grab slips through, only
            // the player who started the game may handle cards.
            if (!_IsLocalGameOwner())
            {
                card._Reject();
                return;
            }

            CardSlot below = card.PrevSlot;
            if (below == null) return; // loose card, no pile rules apply

            // _RefreshPickupable should already have blocked this; it's a backstop
            // for the window between a card being uncovered and being turned over.
            if (!card.FaceUp)
            {
                card._Reject();
                return;
            }

            // Foundations only ever give up their top card.
            if (IsFoundationChain(below) && card.Slot != null && card.Slot._IsOccupied())
            {
                card._Reject();
            }
        }

        public void _OnCardDrop(CardLogic card)
        {
            if (card == null) return;
            if (!initialized) Init();

            CardSlot target = FindDropTarget(card);
            if (target != null) card._SetPrevSlot(target);
            else card._ApplyPlacement(); // no valid home, snap back where it was

            // The cards it was carrying stay parented to it, but their offset comes
            // from the pile they're now in, so re-derive it.
            _RepositionAbove(card);
            RevealTops();
            CheckWon();
        }

        // Nearest slot that will take this card. Cards stacked on top of the dragged
        // one come along for free - they're parented to it and their own PrevSlot
        // still points at it.
        private CardSlot FindDropTarget(CardLogic card)
        {
            if (slotsById == null) return null;
            Transform mover = card.CardRoot;
            if (mover == null) mover = card.transform;
            Vector3 position = mover.position;

            CardSlot best = null;
            float bestSqr = SnapDistance * SnapDistance;
            for (int i = 0; i < slotsById.Length; i++)
            {
                CardSlot slot = slotsById[i];
                if (slot == null) continue;
                if (slot.Owner != null && !slot.Owner.gameObject.activeInHierarchy) continue;

                float sqr = (slot.transform.position - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                if (!slot._CanAccept(card)) continue;

                best = slot;
                bestSqr = sqr;
            }
            if (best == null) LogDropFailure(card, position);
            return best;
        }

        // Only runs when a drop found nowhere to go. Reports the nearest slots with
        // their distance and the exact reason each one turned the card away, which
        // separates "aim was out of range" from "a rule said no".
        private void LogDropFailure(CardLogic card, Vector3 position)
        {
            const int Show = 4;
            CardSlot[] nearest = new CardSlot[Show];
            float[] nearestSqr = new float[Show];
            for (int i = 0; i < Show; i++) nearestSqr[i] = float.MaxValue;

            for (int i = 0; i < slotsById.Length; i++)
            {
                CardSlot slot = slotsById[i];
                if (slot == null) continue;
                float sqr = (slot.transform.position - position).sqrMagnitude;
                // Insertion sort into the running top-N, shuffling worse ones down.
                for (int rank = 0; rank < Show; rank++)
                {
                    if (sqr >= nearestSqr[rank]) continue;
                    for (int back = Show - 1; back > rank; back--)
                    {
                        nearestSqr[back] = nearestSqr[back - 1];
                        nearest[back] = nearest[back - 1];
                    }
                    nearestSqr[rank] = sqr;
                    nearest[rank] = slot;
                    break;
                }
            }

            string identity = card.IsJoker ? $"Joker {card.JokerIndex}" : $"{card.CardRank} of {card.CardSuit}";
            string report = $"Solitaire: {card.name} [{identity}] found no slot within {SnapDistance}m of {position}. Nearest:";
            for (int i = 0; i < Show; i++)
            {
                if (nearest[i] == null) continue;
                int code = nearest[i]._CheckAccept(card);
                report += $"\n  {nearest[i].name} (id {nearest[i].SlotId}) at {Mathf.Sqrt(nearestSqr[i]):F3}m - {nearest[i]._DescribeReject(code)}";
            }
            Debug.Log(report);
        }

        public bool _IsTableauChain(CardSlot slot)
        {
            if (TableauSlots == null || slot == null) return false;
            CardSlot root = slot._GetRootSlot();
            if (root == null) return false;
            for (int s = 0; s < TableauSlots.Length; s++)
            {
                if (TableauSlots[s] == root) return true;
            }
            return false;
        }

        private bool IsFoundationChain(CardSlot slot)
        {
            if (FoundationSlots == null || slot == null) return false;
            CardSlot root = slot._GetRootSlot();
            if (root == null) return false;
            for (int s = 0; s < FoundationSlots.Length; s++)
            {
                if (FoundationSlots[s] == root) return true;
            }
            return false;
        }

        private void RevealTops()
        {
            if (TableauSlots == null) return;
            for (int s = 0; s < TableauSlots.Length; s++)
            {
                CardSlot slot = TableauSlots[s];
                if (slot == null) continue;
                CardLogic top = slot._GetTopCard();
                if (top != null && !top.FaceUp) top.SetFaceUp(true);
            }
        }

        private void CheckWon()
        {
            if (won || FoundationSlots == null) return;
            for (int i = 0; i < FoundationSlots.Length; i++)
            {
                if (FoundationSlots[i] == null) return;
                if (FoundationSlots[i]._GetCardCount() < CardLogic.RankDefinitionsCount) return;
            }
            won = true;
            if (WinMessage != null) WinMessage.SetActive(true);
        }
    }
}
