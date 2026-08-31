using UdonSharp;
using UnityEngine;
using TMPro;
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
        [Tooltip("The stock deck (DeckManager with its VRCObjectPool). Resolved to the local player's PlayerObject copy at startup; the scene reference is the fallback.")]
        public DeckManager DeckOfCards;

        [Tooltip("Where the deck (DeckManager root) is moved to when a game is dealt.")]
        public Transform CardHome;

        [Tooltip("7 tableau columns, left to right. Cards stack face-down except the top.")]
        public CardSlot[] TableauSlots = new CardSlot[7];

        [Tooltip("4 foundation piles. Build ace to king, one suit per pile (top card only).")]
        public CardSlot[] FoundationSlots = new CardSlot[4];

        [Tooltip("Waste pile next to the stock; drawn cards land here.")]
        public CardSlot WasteSlot;

        [Header("Placement")]
        [Tooltip("How close a dropped card has to be to a slot to snap into it.")]
        public float SnapDistance = 0.12f;

        [Tooltip("Seconds to wait between dealing each card. Each card costs a pool spawn, an ownership transfer and two serializations, so this is really a throttle on outgoing network traffic - going much below 0.15 risks VRChat dropping writes faster than the retries can recover them.")]
        public float DealDelay = 0.2f;

        [Header("Win")]
        [Tooltip("Optional object activated when all 4 foundations are complete.")]
        public GameObject WinMessage;

        [Header("UI")]
        [Tooltip("Label on the start/quit trigger. Shows \"Start\" before a game, \"Quit\" while one is running.")]
        public TextMeshProUGUI StartButtonLabel;

        [Tooltip("Interact collider on the start/quit button. While a game is running by another player it is disabled so they cannot start a competing game.")]
        public Collider StartButtonInteract;

        [Tooltip("Confirmation dialog shown when pressing Quit would abandon the game in progress.")]
        public GameObject ConfirmDialog;

        // The per-player deck actually used for the running game. Unlike the
        // serialized DeckOfCards (the scene template/fallback reference), this one
        // is repointed at the local player's PlayerObject copy on every deal, so the
        // original DeckOfCards keeps pointing at the same deck it was assigned.
        private DeckManager resolvedDeck;
        private CardLogic[] cards;
        private CardSlot[] slotsById;
        private int baseSlotCount;
        private Transform cardHome;
        private bool dealing;
        private bool gameStarted;
        private bool won;
        private int dealCol;
        private int dealDepth;
        private VRCPlayerApi dealOwner;

        // True once a deal has happened; input that depends on a running game
        // (like drawing from the stock) is gated on this.
        public bool _IsGameStarted()
        {
            return gameStarted;
        }

        // True when the local player is the one who dealt the current game. Drives
        // who is allowed to grab cards and poke the deck.
        public bool _IsLocalGameOwner()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;
            return Networking.IsOwner(local, gameObject);
        }

        private void Start()
        {
            if (WinMessage != null) WinMessage.SetActive(false);
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
        }

        private void Init()
        {
            resolvedDeck = ResolveDeck();
            if (resolvedDeck == null)
            {
                Debug.Log("Solitaire: Deck not assigned, cannot initialize.");
                return;
            }

            VRCObjectPool pool = resolvedDeck.Pool;
            int n = pool.Pool.Length;
            if (n <= 0)
            {
                Debug.Log("Solitaire: No cards in deck, cannot initialize.");
                return;
            }

            cardHome = pool.transform;
            resolvedDeck.Solitaire = this;

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

                logic.DeckManager = resolvedDeck;
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

            RefreshStartLabel();
            _RefreshStartInteractable();
            if (WinMessage != null) WinMessage.SetActive(false);
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
            Debug.Log($"Solitaire: Initialized with {n} cards and {baseSlotCount} base slots.");
        }

        private DeckManager ResolveDeck()
        {
            DeckManager deck = FindDeck(Networking.GetOwner(gameObject));
            if (Utilities.IsValid(deck))
            {
                Debug.Log("Solitaire: Using local player's deck PlayerObject.");
                return deck;
            }
            return DeckOfCards;
        }

        private DeckManager FindDeck(VRCPlayerApi player)
        {
            var objects = Networking.GetPlayerObjects(player);
            for (int i = 0; i < objects.Length; i++)
            {
                if (!Utilities.IsValid(objects[i])) continue;
                DeckManager foundScript = objects[i].GetComponentInChildren<DeckManager>();
                if (Utilities.IsValid(foundScript)) return foundScript;
            }
            return null;
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

        // The interactable lives on the start button now (driven by GameInteract,
        // which forwards presses here), so only that button lights up on hover.
        public void _OnStartPressed()
        {
            if (gameStarted)
            {
                if (!_IsLocalGameOwner())
                {
                    Debug.Log("Solitaire: Only the player who started the game may quit it.");
                    return;
                }

                if (won)
                {
                    _ResetGame();
                    return;
                }

                if (ConfirmDialog != null)
                {
                    if (ConfirmDialog != null && ConfirmDialog.activeSelf)
                    {
                        ConfirmDialog.SetActive(false);
                        return;
                    }
                    ConfirmDialog.SetActive(true);
                }
                return;
            }
            Deal(Networking.LocalPlayer);
        }

        // User confirmed quitting; tear the game down to the pre-deal state.
        public void _ConfirmNewGame()
        {
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
            _ResetGame();
        }

        // User backed out; just close the confirmation dialog.
        public void _CancelNewGame()
        {
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
        }

        public void Deal(VRCPlayerApi owner = null)
        {
            if (dealing)
            {
                Debug.Log("Solitaire: Already dealing cards.");
                return;
            }

            // Re-resolve the deck and rebuild the card/slot registry on every deal:
            // the deck lives as a per-player PlayerObject copy, so whichever player
            // is dealing owns a different deck with different card objects. Re-init
            // picks that up instead of reusing the previous player's snapshot.
            Init();

            if (resolvedDeck == null || cards == null)
            {
                Debug.Log("Solitaire: Deck not assigned, cannot deal.");
                return;
            }
            if (TableauSlots == null || FoundationSlots == null)
            {
                Debug.Log("Solitaire: Tableau or Foundation slots not assigned, cannot deal.");
                return;
            }

            if (!Utilities.IsValid(owner))
            {
                Debug.Log("Solitaire: No local player, cannot deal cards.");
                return;
            }

            Networking.SetOwner(owner, gameObject);
            Networking.SetOwner(owner, resolvedDeck.gameObject);
            resolvedDeck._SetGameOwner(owner.playerId);
            if (CardHome != null)
            {
                resolvedDeck.transform.position = CardHome.position;
                resolvedDeck.transform.rotation = CardHome.rotation;
            }
            dealing = true;
            gameStarted = true;
            RefreshStartLabel();
            _RefreshStartInteractable();
            resolvedDeck._RefreshInteractable();
            Debug.Log($"Solitaire: Dealing cards for {owner.displayName} ({owner.playerId})");
            ResetCards();
            dealCol = 0;
            dealDepth = 0;
            dealOwner = owner;
            SendCustomEventDelayedSeconds("_DealNextCard", DealDelay);
        }

        // Deals exactly one card per call, then schedules the next after DealDelay.
        // The tableau is dealt column by column, each column one deeper than the last.
        public void _DealNextCard()
        {
            if (!dealing) return;
            if (dealOwner == null || resolvedDeck == null || TableauSlots == null)
            {
                FinalizeDeal();
                return;
            }

            if (dealCol < TableauSlots.Length && dealCol < 7)
            {
                CardSlot slot = TableauSlots[dealCol];
                if (slot == null)
                {
                    dealCol++;
                    dealDepth = 0;
                }
                else
                {
                    GameObject cardGO = resolvedDeck.DrawNext();
                    if (cardGO == null)
                    {
                        FinalizeDeal();
                        return;
                    }
                    CardLogic card = cardGO.GetComponentInChildren<CardLogic>(true);
                    if (card != null)
                    {
                        Networking.SetOwner(dealOwner, cardGO);
                        card._ForcePlace(slot._GetTopSlot(), dealDepth == dealCol);
                    }
                    dealDepth++;
                    if (dealDepth > dealCol)
                    {
                        dealCol++;
                        dealDepth = 0;
                    }
                }
            }
            if (dealCol < TableauSlots.Length && dealCol < 7)
            {
                SendCustomEventDelayedSeconds("_DealNextCard", DealDelay);
            }
            else
            {
                FinalizeDeal();
            }
        }

        // One click on the stock: deal the next card, or turn the waste back over
        // once the stock has run dry.
        public void _OnStockClicked()
        {
            if (resolvedDeck == null || dealing) return;
            if (!_IsLocalGameOwner()) return;

            if (resolvedDeck._IsStockEmpty()) _RecycleWaste();
            else _DrawFromStock();
        }

        // Send the whole waste pile back to the stock, face down.
        public void _RecycleWaste()
        {
            if (resolvedDeck == null || WasteSlot == null || dealing) return;
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

            // Top down, so no card is deactivated while others are still parented
            // underneath it.
            for (int i = count - 1; i >= 0; i--)
            {
                CardLogic card = pile[i];
                if (card == null) continue;
                Networking.SetOwner(local, card.gameObject);
                card._Detach(cardHome);
                resolvedDeck._ReturnCard(card.gameObject);
            }

            Debug.Log($"Solitaire: Recycled {count} cards from the waste back into the stock.");
        }

        // Draw the next stock card onto the waste pile.
        public void _DrawFromStock()
        {
            if (resolvedDeck == null || WasteSlot == null || dealing) return;
            if (!_IsLocalGameOwner()) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            GameObject cardGO = resolvedDeck.DrawNext();
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
            resolvedDeck._ResetDeck();
        }

        private void FinalizeDeal()
        {
            dealing = false;
            won = false;
            if (WinMessage != null) WinMessage.SetActive(false);
        }

        // The label mirrors the interactable: hidden while another player is
        // mid-game, so a spectator sees neither the "Start" prompt nor a button
        // that they can't press anyway.
        private void RefreshStartLabel()
        {
            if (StartButtonLabel == null) return;
            int ownerId = resolvedDeck != null ? resolvedDeck.GameOwnerId : -1;
            bool running = gameStarted || ownerId != -1;
            bool localOwner = false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local)) localOwner = ownerId == local.playerId;
            StartButtonLabel.text = (running && !localOwner) ? "" : (gameStarted ? "Quit" : "Start");
        }

        // The start/quit button only invites an interact when no game is running
        // (anyone may deal) or the local player owns the current one (Quit). While
        // another player is mid-game the collider drops so they can't start a
        // competing deal. Runs locally on each client; the synced ownerId drives it.
        public void _RefreshStartInteractable()
        {
            if (StartButtonInteract == null) return;
            int ownerId = resolvedDeck != null ? resolvedDeck.GameOwnerId : -1;
            bool running = gameStarted || ownerId != -1;
            bool localOwner = false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local)) localOwner = ownerId == local.playerId;
            StartButtonInteract.enabled = !running || localOwner;
            RefreshStartLabel();
        }

        // Tear the running game back down to the pre-deal state: every card back in
        // the pool, no game owner, nobody may interact until someone deals anew.
        public void _ResetGame()
        {
            if (resolvedDeck == null || cards == null) return;

            ResetCards();
            resolvedDeck._SetGameOwner(-1);

            gameStarted = false;
            dealing = false;
            won = false;
            RefreshStartLabel();
            _RefreshStartInteractable();
            resolvedDeck._RefreshInteractable();
            if (WinMessage != null) WinMessage.SetActive(false);
            Debug.Log("Solitaire: Game reset to initial state.");
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (resolvedDeck == null) return;
            if (player == null || resolvedDeck.GameOwnerId != player.playerId) return;
            Debug.Log($"Solitaire: Game owner {player.displayName} left; resetting game.");
            _ResetGame();
        }

        public void _OnCardPickup(CardLogic card)
        {
            if (card == null) return;

            // Backstop for the pickupable gate: even if a grab slips through, only
            // the player who started the game may handle cards.
            if (!_IsLocalGameOwner() || dealing)
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

            // Same backstop for the base slot's pick-up policy (top-only / none).
            CardPickupMode pickupMode = below._GetPickupMode();
            if (pickupMode == CardPickupMode.None
                || (pickupMode == CardPickupMode.TopOnly && below._GetTopCard() != card))
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
