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
        // A pile can never be deeper than the deck, so anything past this is a
        // cycle. Sized for a 104-card deck, not just the 52-card one.
        private const int ChainGuard = 128;

        [Header("References")]
        [Tooltip("The stock deck (DeckManager with its VRCObjectPool). Resolved to the local player's PlayerObject copy at startup; the scene reference is the fallback.")]
        public DeckManager DeckOfCards;

        [Tooltip("Which deck this table claims. Must match the DeckKey on the DeckManager: a player carries one deck PlayerObject per game, and the key is what stops this table from picking up another game's deck. Leave at 0 unless there is more than one.")]
        public int DeckKey = 0;

        [Tooltip("Where the deck (DeckManager root) is moved to when a game is dealt.")]
        public Transform CardHome;

        [Tooltip("Tableau columns, left to right. Cards stack face-down except the top. 7 for Klondike, 10 for Spider.")]
        public CardSlot[] TableauSlots = new CardSlot[7];

        [Tooltip("Foundation piles, each completed by 13 cards. 4 for Klondike (ace to king, one suit each), 8 for Spider (one per finished run).")]
        public CardSlot[] FoundationSlots = new CardSlot[4];

        [Tooltip("Waste pile next to the stock; drawn cards land here.")]
        public CardSlot WasteSlot;

        [Header("Placement")]
        [Tooltip("How close a dropped card has to be to a slot to snap into it.")]
        public float SnapDistance = 0.12f;

        [Tooltip("Seconds to wait between dealing each card. Each card costs a pool spawn, an ownership transfer and two serializations, so this is really a throttle on outgoing network traffic - going much below 0.15 risks VRChat dropping writes faster than the retries can recover them.")]
        public float DealDelay = 0.2f;

        [Header("Deal")]
        [Tooltip("How many cards go into each tableau column on the opening deal, in column order. {1,2,3,4,5,6,7} is Klondike; Spider two-suit wants {6,6,6,6,5,5,5,5,5,5}. Columns past the end of this array, and entries of 0, are skipped. Only the card that ends up on top of a column is dealt face-up.")]
        public int[] DealCounts = new int[] { 1, 2, 3, 4, 5, 6, 7 };

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

        // Reverse of the linked list: cardOnSlot[id] is the card whose PrevSlotId is
        // id. Rebuilt lazily because a scan per link is what _GetCardOn used to cost,
        // and every chain walk calls it once per level - so a drop, which tests every
        // slot in the game, paid cards x slots. Cheap at 52 cards, a frame hitch at
        // 104.
        private CardLogic[] cardOnSlot;
        private bool indexDirty = true;

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

            // slotsById was just replaced, so any index built against the old one is
            // meaningless. Has to happen before the catch-up loop below reads it.
            indexDirty = true;

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
                Debug.Log($"Solitaire: Using local player's deck PlayerObject for key {DeckKey}.");
                return deck;
            }
            // The scene reference is assigned by hand, so it is this table's deck by
            // definition - but a mismatched key means the PlayerObject lookup will
            // never find it either, and that is worth saying out loud.
            if (DeckOfCards != null && DeckOfCards.DeckKey != DeckKey)
            {
                Debug.Log($"Solitaire: Fallback deck {DeckOfCards.name} has DeckKey {DeckOfCards.DeckKey} but this table wants {DeckKey}. Fix one of them or the per-player deck will never resolve.");
            }
            return DeckOfCards;
        }

        // Picks the player's deck for *this* table. A world with two card games gives
        // every player one deck PlayerObject per game, so matching on the key is what
        // keeps them apart - without it whichever deck enumerated first would win.
        // A mismatch has to keep scanning rather than give up: the deck we want is
        // very likely a later entry.
        private DeckManager FindDeck(VRCPlayerApi player)
        {
            var objects = Networking.GetPlayerObjects(player);
            for (int i = 0; i < objects.Length; i++)
            {
                if (!Utilities.IsValid(objects[i])) continue;
                DeckManager foundScript = objects[i].GetComponentInChildren<DeckManager>();
                if (!Utilities.IsValid(foundScript)) continue;
                if (foundScript.DeckKey != DeckKey) continue;
                return foundScript;
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

        // Reverse lookup for the linked list: who points at this slot? Served from
        // the index, so this is O(1) no matter how big the deck gets.
        public CardLogic _GetCardOn(CardSlot slot)
        {
            if (slot == null || cards == null) return null;
            int id = slot.SlotId;
            if (id < 0) return null;
            if (indexDirty) RebuildCardIndex();
            if (cardOnSlot == null || id >= cardOnSlot.Length) return null;

            CardLogic card = cardOnSlot[id];
            if (card == null) return null;
            // The pool deactivating a card and its link clearing are two independent
            // network messages. If the deactivation lands first the index still holds
            // the card, so activity is re-tested on read rather than trusted from
            // build time.
            if (!card.gameObject.activeInHierarchy) return null;
            return card;
        }

        // Every card's link changes on placement, and any of them can invalidate the
        // index, so CardLogic calls this instead of the index being rebuilt eagerly.
        // A deal touches every card, and only the first read after it pays.
        public void _InvalidateCardIndex()
        {
            indexDirty = true;
        }

        private void RebuildCardIndex()
        {
            // Cleared first: nothing below re-enters Solitaire, so this can't recurse,
            // and clearing up front means it can't loop if that ever changes.
            indexDirty = false;
            if (slotsById == null || cards == null) return;

            if (cardOnSlot == null || cardOnSlot.Length != slotsById.Length)
            {
                cardOnSlot = new CardLogic[slotsById.Length];
            }
            else
            {
                for (int i = 0; i < cardOnSlot.Length; i++) cardOnSlot[i] = null;
            }

            for (int i = 0; i < cards.Length; i++)
            {
                CardLogic card = cards[i];
                if (card == null) continue;
                if (!card.gameObject.activeInHierarchy) continue;
                int id = card.PrevSlotId;
                if (id < 0 || id >= cardOnSlot.Length) continue;
                // Two cards claiming one slot only happens mid-deserialization. First
                // in pool order wins, which is what the old linear scan returned.
                if (cardOnSlot[id] == null) cardOnSlot[id] = card;
            }
        }

        // Re-snap everything stacked above a card, after something changed the
        // offset it hands out (a flip, say).
        public void _RepositionAbove(CardLogic card)
        {
            if (card == null || card.Slot == null) return;
            CardSlot current = card.Slot;
            int guard = 0;
            while (guard < ChainGuard)
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
            CheckDealPlan();
            SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
        }

        // A bad deal plan fails quietly in both directions: too many cards and the
        // deal stops mid-way when DrawNext dries up, which reads as a dropped network
        // write; too few and the table just comes up empty. Neither is obvious from
        // the symptom, so both get named here, once, before any card moves.
        private void CheckDealPlan()
        {
            int columns = TableauSlots != null ? TableauSlots.Length : 0;
            int total = 0;
            for (int col = 0; col < columns; col++) total += DealCountFor(col);

            if (total <= 0)
            {
                Debug.Log($"Solitaire: DealCounts deals no cards ({(DealCounts == null ? "null" : DealCounts.Length + " entries")}, {columns} tableau slots). Klondike wants {{1,2,3,4,5,6,7}}.");
                return;
            }

            int available = resolvedDeck != null && resolvedDeck.Pool != null
                ? resolvedDeck.Pool.Pool.Length
                : 0;
            if (total > available)
            {
                Debug.Log($"Solitaire: DealCounts asks for {total} cards but the deck only holds {available}. The deal will stop short.");
            }
        }

        // How many cards column `col` wants on the opening deal. Columns with no slot
        // assigned, or past the end of DealCounts, want none - which is what lets the
        // same loop deal a 7-column Klondike table and a 10-column Spider one.
        private int DealCountFor(int col)
        {
            if (TableauSlots == null || col < 0 || col >= TableauSlots.Length) return 0;
            if (TableauSlots[col] == null) return 0;
            if (DealCounts == null || col >= DealCounts.Length) return 0;
            int want = DealCounts[col];
            return want > 0 ? want : 0;
        }

        // Walks dealCol forward past every column that wants nothing more, so the
        // dealer never has to special-case a null slot or a zero count. Terminates
        // because each pass either returns or advances dealCol.
        private bool AdvanceToNextDealColumn()
        {
            int columns = TableauSlots != null ? TableauSlots.Length : 0;
            while (dealCol < columns)
            {
                if (dealDepth < DealCountFor(dealCol)) return true;
                dealCol++;
                dealDepth = 0;
            }
            return false;
        }

        // Deals exactly one card per call, then schedules the next after DealDelay.
        // The tableau is dealt column by column, as deep as DealCounts asks for.
        public void _DealNextCard()
        {
            if (!dealing) return;
            if (dealOwner == null || resolvedDeck == null || TableauSlots == null)
            {
                FinalizeDeal();
                return;
            }

            if (!AdvanceToNextDealColumn())
            {
                FinalizeDeal();
                return;
            }

            CardSlot slot = TableauSlots[dealCol];
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
                // Only the card that ends up on top of the column is face-up, which is
                // the rule in Klondike and Spider alike.
                card._ForcePlace(slot._GetTopSlot(), dealDepth == DealCountFor(dealCol) - 1);
            }
            dealDepth++;

            if (AdvanceToNextDealColumn())
            {
                SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
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
