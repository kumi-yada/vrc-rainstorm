using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    public enum SolitaireMode
    {
        // Draw one card to the waste, recycle it when the stock dries up, build the
        // foundations ace-up in suit.
        Klondike = 0,
        // No waste: a stock click deals one card face-up to every column. Groups only
        // move as same-suit runs, and a finished king-to-ace run leaves the table.
        Spider = 1,
        // Four short columns fed by a 13-card reserve, drawing three at a time to the
        // waste with unlimited redeals. The deal turns one card onto a foundation and
        // that card's rank is the base every foundation builds up from, wrapping past
        // the king back to the ace.
        Canfield = 2
    }

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

        [Header("Game")]
        [Tooltip("Which game this table plays. Everything the modes share is driven by the slot layout, DealCounts and the SlotRule components; this only switches the handful of mechanics that genuinely differ - what a stock click does, whether groups have to be same-suit runs, whether finished runs leave the table, and whether there is a reserve feeding the columns.")]
        public SolitaireMode Mode = SolitaireMode.Klondike;

        [Header("References")]
        [Tooltip("The stock deck (DeckManager with its VRCObjectPool). Resolved to the local player's PlayerObject copy at startup; the scene reference is the fallback.")]
        public DeckManager DeckOfCards;

        [Tooltip("Which deck this table claims. Must match the DeckKey on the DeckManager: a player carries one deck PlayerObject per game, and the key is what stops this table from picking up another game's deck. Leave at 0 unless there is more than one.")]
        public int DeckKey = 0;

        [Tooltip("Where the deck (DeckManager root) is moved to when a game is dealt.")]
        public Transform CardHome;

        [Tooltip("Tableau columns, left to right. Cards stack face-down except the top. 7 for Klondike, 10 for Spider, 4 for Canfield.")]
        public CardSlot[] TableauSlots = new CardSlot[7];

        [Tooltip("Foundation piles, each completed by 13 cards. 4 for Klondike (ace to king, one suit each), 8 for Spider (one per finished run), 4 for Canfield (base rank up, wrapping, one suit each).")]
        public CardSlot[] FoundationSlots = new CardSlot[4];

        [Tooltip("Waste pile next to the stock; drawn cards land here.")]
        public CardSlot WasteSlot;

        [Tooltip("Canfield's reserve pile: 13 cards dealt face-down with only the top one turned over, and the automatic filler for any tableau column that empties. Leave unassigned for Klondike and Spider. Wants Pickup = TopOnly, since only its top card is ever in play, and Drop = None - nothing ever goes back onto the reserve, and without it the pile would happily accept any card, having no SlotRule to turn one away.")]
        public CardSlot ReserveSlot;

        [Header("Placement")]
        [Tooltip("How close a dropped card has to be to a slot to snap into it.")]
        public float SnapDistance = 0.12f;

        [Tooltip("Seconds to wait between dealing each card. Each card costs a pool spawn, an ownership transfer and two serializations, so this is really a throttle on outgoing network traffic - going much below 0.15 risks VRChat dropping writes faster than the retries can recover them.")]
        public float DealDelay = 0.2f;

        [Header("Deal")]
        [Tooltip("How many cards go into each tableau column on the opening deal, in column order. {1,2,3,4,5,6,7} is Klondike; Spider two-suit wants {6,6,6,6,5,5,5,5,5,5}; Canfield wants {1,1,1,1}. Columns past the end of this array, and entries of 0, are skipped. Only the card that ends up on top of a column is dealt face-up.")]
        public int[] DealCounts = new int[] { 1, 2, 3, 4, 5, 6, 7 };

        [Tooltip("Canfield only: how many cards go into the reserve before the tableau is dealt. 13 is the standard game. Ignored when ReserveSlot is unassigned.")]
        public int ReserveCount = 13;

        [Header("Stock")]
        [Tooltip("How many cards one stock click turns onto the waste. 0 uses the mode default - 1 for Klondike, 3 for Canfield - so leaving it alone gives each mode the right game. Spider ignores this entirely; its stock click deals a row to the tableau instead.")]
        public int DrawCount = 0;

        [Header("Win")]
        [Tooltip("Optional object activated once every foundation holds a complete 13-card pile.")]
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

        // Which job the delayed deal loop is currently doing. A Spider stock round is
        // ten more cards down the same throttled pipe as the opening deal, and so are
        // Canfield's reserve, its foundation seed and its three-card draw, so they all
        // reuse that machinery rather than adding timers that could overlap with it.
        private const int DealPhaseNone = 0;
        private const int DealPhaseOpening = 1;
        private const int DealPhaseStockRow = 2;
        private const int DealPhaseReserve = 3;
        private const int DealPhaseFoundation = 4;
        private const int DealPhaseDraw = 5;
        private int dealPhase;

        // Cards still owed to the waste by the draw currently in flight.
        private int drawRemaining;

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

            // The deck's pool is an inspector reference that may sit on another
            // GameObject, so it can legitimately be unassigned - and DeckManager.Start
            // leaves it null rather than throwing when it is.
            VRCObjectPool pool = resolvedDeck.Pool;
            if (pool == null || pool.Pool == null)
            {
                Debug.Log($"Solitaire: deck {resolvedDeck.name} (key {resolvedDeck.DeckKey}) has no VRCObjectPool assigned, cannot initialize.");
                return;
            }

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
            // The waste and the reserve always occupy the last two base ids, even when
            // unassigned, so ids stay stable regardless of which references happen to
            // be filled in - a Klondike table with no reserve numbers its cards the
            // same way a Canfield one does.
            baseSlotCount = tableauLength + foundationLength + 2;

            slotsById = new CardSlot[baseSlotCount + n];
            for (int i = 0; i < tableauLength; i++)
            {
                RegisterBaseSlot(TableauSlots[i], i);
            }
            for (int i = 0; i < foundationLength; i++)
            {
                RegisterBaseSlot(FoundationSlots[i], tableauLength + i);
            }
            RegisterBaseSlot(WasteSlot, baseSlotCount - 2);
            RegisterBaseSlot(ReserveSlot, baseSlotCount - 1);

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
            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            DeckManager deck = FindDeck(owner);
            if (Utilities.IsValid(deck))
            {
                Debug.Log($"Solitaire: Using local player's deck PlayerObject for key {DeckKey}.");
                return deck;
            }

            // Nothing matched. The bare "Deck not assigned" that the callers log next
            // is misleading on its own, because the usual cause is that a deck *is*
            // there carrying a different DeckKey - so name the keys that were on offer.
            ReportDeckKeyMiss(owner);
            return DeckOfCards;
        }

        private void ReportDeckKeyMiss(VRCPlayerApi owner)
        {
            string seen = "";
            if (Utilities.IsValid(owner))
            {
                var objects = Networking.GetPlayerObjects(owner);
                for (int i = 0; i < objects.Length; i++)
                {
                    if (!Utilities.IsValid(objects[i])) continue;
                    DeckManager[] found = objects[i].GetComponentsInChildren<DeckManager>(true);
                    if (found == null) continue;
                    for (int d = 0; d < found.Length; d++)
                    {
                        if (!Utilities.IsValid(found[d])) continue;
                        seen += $" {found[d].name}(key {found[d].DeckKey}, {found[d].SuitsInPlay} suit(s))";
                    }
                }
            }
            if (seen.Length == 0) seen = " none";

            string fallback = DeckOfCards == null
                ? "unassigned"
                : $"{DeckOfCards.name} (key {DeckOfCards.DeckKey})";
            Debug.Log($"Solitaire: no deck PlayerObject matched DeckKey {DeckKey}. Decks on this player:{seen}. Fallback DeckOfCards is {fallback}.");
        }

        private DeckManager FindDeck(VRCPlayerApi player)
        {
            var objects = Networking.GetPlayerObjects(player);
            for (int i = 0; i < objects.Length; i++)
            {
                if (!Utilities.IsValid(objects[i])) continue;
                DeckManager[] found = objects[i].GetComponentsInChildren<DeckManager>(true);
                if (found == null) continue;
                for (int d = 0; d < found.Length; d++)
                {
                    if (!Utilities.IsValid(found[d])) continue;
                    if (found[d].DeckKey != DeckKey) continue;
                    return found[d];
                }
            }
            return null;
        }

        private bool FindActiveDeckFor(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return false;

            var objects = Networking.GetPlayerObjects(player);
            // Null for a player whose objects have not spawned yet, which is normal for
            // someone still loading in.
            if (objects == null) return false;

            for (int i = 0; i < objects.Length; i++)
            {
                if (!Utilities.IsValid(objects[i])) continue;
                // Same reason as FindDeck: the decks may share one PlayerObject root,
                // so every DeckManager under it has to be tested.
                DeckManager[] found = objects[i].GetComponentsInChildren<DeckManager>(true);
                if (found == null) continue;
                for (int d = 0; d < found.Length; d++)
                {
                    if (!Utilities.IsValid(found[d])) continue;
                    if (found[d].InActiveGame) return true;
                    return false;
                }
            }
            return false;
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

            // The dealer may not already have a game of their own running. Checked
            // before Init, because Init repoints resolvedDeck and rebuilds the whole
            // slot registry - a refused deal should not have moved anything, not even
            // locally. An invalid owner falls through to the explicit check below.
            bool busy = FindActiveDeckFor(owner);
            if (busy)
            {
                string who = Utilities.IsValid(owner) ? owner.displayName : "this player";
                Debug.Log($"Solitaire: {who} already has a game running, it has to be quit before dealing another.");
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

            // Before anything is claimed, reset or flagged as started: a refused plan
            // has to leave the table exactly as it was.
            if (!CheckDealPlan()) return;

            Networking.SetOwner(owner, gameObject);
            Networking.SetOwner(owner, resolvedDeck.gameObject);
            resolvedDeck._SetGameOwner(owner.playerId);
            // DeckManager owns this move because it knows where its pool is - the
            // undealt cards live under the pool, which may not be the deck itself.
            resolvedDeck._MoveTo(CardHome);
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
            // Canfield deals its reserve and foundation seed before the columns; every
            // other mode starts straight on the tableau.
            dealPhase = ResolveReserveCount() > 0 ? DealPhaseReserve : DealPhaseOpening;
            SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
        }

        // A bad deal plan fails quietly in both directions: too many cards and the
        // deal stops mid-way when DrawNext dries up, which reads as a dropped network
        // write; too few and the table just comes up empty. Neither is obvious from
        // the symptom, so both get named here, once, before any card moves.
        //
        // Returns false to refuse the deal outright. A table dealt short is not a
        // lesser game, it is an unplayable one - the short column can never be
        // completed and the stock is already dry - so it is better to deal nothing and
        // say why than to leave someone poking at a broken layout.
        private bool CheckDealPlan()
        {
            int columns = TableauSlots != null ? TableauSlots.Length : 0;
            int tableauTotal = 0;
            for (int col = 0; col < columns; col++) tableauTotal += DealCountFor(col);

            if (tableauTotal <= 0)
            {
                Debug.Log($"Solitaire: DealCounts deals no cards ({(DealCounts == null ? "null" : DealCounts.Length + " entries")}, {columns} tableau slots). Klondike wants {{1,2,3,4,5,6,7}}, Canfield {{1,1,1,1}}.");
                return false;
            }

            // The reserve and the foundation seed come out of the same stock, so the
            // pool has to cover them too or the tableau deal is the part that starves.
            // Tested against the slot array rather than against occupancy: this runs
            // before ResetCards, so a table still holding an abandoned game would
            // otherwise read its foundations as full and undercount by one.
            int reserve = ResolveReserveCount();
            int total = tableauTotal + reserve + (reserve > 0 && HasFoundationSlot() ? 1 : 0);

            int available = resolvedDeck != null && resolvedDeck.Pool != null && resolvedDeck.Pool.Pool != null
                ? resolvedDeck.Pool.Pool.Length
                : 0;
            if (total > available)
            {
                Debug.Log($"Solitaire: the deal asks for {total} cards ({tableauTotal} tableau, {total - tableauTotal} reserve/foundation) but the deck's pool only holds {available}. Refusing to deal - add the missing cards to the VRCObjectPool's Pool array.");
                return false;
            }
            return true;
        }

        private bool HasFoundationSlot()
        {
            if (FoundationSlots == null) return false;
            for (int i = 0; i < FoundationSlots.Length; i++)
            {
                if (FoundationSlots[i] != null) return true;
            }
            return false;
        }

        // How many cards the reserve wants on the opening deal. Zero for every mode
        // but Canfield, and zero for a Canfield table with no reserve slot wired up -
        // which is what keeps the reserve phases out of the other modes' deals.
        private int ResolveReserveCount()
        {
            if (Mode != SolitaireMode.Canfield) return 0;
            if (ReserveSlot == null) return 0;
            return ReserveCount > 0 ? ReserveCount : 0;
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

            // Every job comes back through this one event, so a stock row or a draw
            // can never end up interleaved with an opening deal on a second timer.
            int phase = dealPhase;

            if (dealOwner == null || resolvedDeck == null || TableauSlots == null)
            {
                // Bailing out of a stock row or a draw must not go through
                // FinalizeDeal, which would clear a win the player already earned.
                if (phase == DealPhaseStockRow) AbandonStockRow();
                else if (phase == DealPhaseDraw) FinalizeDraw();
                else FinalizeDeal();
                return;
            }

            if (phase == DealPhaseStockRow)
            {
                DealNextStockCard();
                return;
            }

            if (phase == DealPhaseDraw)
            {
                DrawNextWasteCard();
                return;
            }

            if (phase == DealPhaseReserve)
            {
                DealNextReserveCard();
                return;
            }

            if (phase == DealPhaseFoundation)
            {
                DealFoundationSeed();
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
                // the rule in every mode - Canfield deals one card per column, so
                // there it means the whole tableau comes up face-up.
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

        // Canfield's reserve: thirteen cards face-down with only the top one turned
        // over. Dealt before anything else, so the columns are the last thing to come
        // off the stock and the stock itself keeps whatever is left.
        private void DealNextReserveCard()
        {
            int want = ResolveReserveCount();
            if (ReserveSlot == null || dealDepth >= want)
            {
                BeginFoundationSeed();
                return;
            }

            GameObject cardGO = resolvedDeck.DrawNext();
            if (cardGO == null)
            {
                Debug.Log("Solitaire: Stock ran dry while dealing the reserve; the deal is short.");
                FinalizeDeal();
                return;
            }
            CardLogic card = cardGO.GetComponentInChildren<CardLogic>(true);
            if (card != null)
            {
                Networking.SetOwner(dealOwner, cardGO);
                card._ForcePlace(ReserveSlot._GetTopSlot(), dealDepth == want - 1);
            }
            dealDepth++;

            if (dealDepth < want) SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
            else BeginFoundationSeed();
        }

        private void BeginFoundationSeed()
        {
            dealPhase = DealPhaseFoundation;
            dealDepth = 0;
            SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
        }

        // One card onto the first foundation. This is the whole of Canfield's opening
        // difficulty: its rank becomes the base every foundation builds up from, so a
        // deal that turns over a nine leaves the aces buried eleven ranks away.
        private void DealFoundationSeed()
        {
            CardSlot foundation = FindEmptyFoundation();
            if (foundation != null)
            {
                GameObject cardGO = resolvedDeck.DrawNext();
                if (cardGO == null)
                {
                    Debug.Log("Solitaire: Stock ran dry before the foundation could be seeded; the deal is short.");
                    FinalizeDeal();
                    return;
                }
                CardLogic card = cardGO.GetComponentInChildren<CardLogic>(true);
                if (card != null)
                {
                    Networking.SetOwner(dealOwner, cardGO);
                    card._ForcePlace(foundation._GetTopSlot(), true);
                    Debug.Log($"Solitaire: Canfield base rank is {card.CardRank}.");
                }
            }

            dealPhase = DealPhaseOpening;
            dealCol = 0;
            dealDepth = 0;
            SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
        }

        // The rank every Canfield foundation starts from, taken from the bottom card
        // of the first foundation that holds one - the card the deal turned over.
        // -1 before the seed lands, and for every other mode.
        //
        // Derived rather than stored, so it costs nothing to sync: the foundations are
        // already on the wire as card links, and a late joiner rebuilds the same
        // answer from them.
        public int _GetFoundationBaseRank()
        {
            if (Mode != SolitaireMode.Canfield || FoundationSlots == null) return -1;
            for (int i = 0; i < FoundationSlots.Length; i++)
            {
                CardSlot slot = FoundationSlots[i];
                if (slot == null) continue;
                CardLogic bottom = slot._GetCardAt(0);
                if (bottom == null || bottom.IsJoker) continue;
                return (int)bottom.CardRank;
            }
            return -1;
        }

        // Whether a player may drop into an empty tableau column. In Canfield the
        // reserve fills those automatically, so a column is only genuinely free once
        // the reserve is spent. Every other mode leaves the decision to its SlotRule.
        public bool _CanFillEmptyTableau()
        {
            if (Mode != SolitaireMode.Canfield) return true;
            if (ReserveSlot == null) return true;
            return !ReserveSlot._IsOccupied();
        }

        // One click on the stock. Klondike draws the next card to the waste and turns
        // the waste back over once the stock runs dry; Canfield does the same three
        // cards at a time; Spider has no waste at all and deals a card to every column
        // instead.
        public void _OnStockClicked()
        {
            if (resolvedDeck == null || dealing) return;
            if (!_IsLocalGameOwner()) return;

            if (Mode == SolitaireMode.Spider)
            {
                _DealStockRow();
                return;
            }

            if (resolvedDeck._IsStockEmpty()) _RecycleWaste();
            else _DrawFromStock();
        }

        // Spider's stock click: one card face-up onto every column, throttled through
        // the same delayed loop as the opening deal because ten cards is ten pool
        // spawns and ten serializations.
        //
        // The standard rule refuses the row while any column is empty. That is worth
        // enforcing rather than allowing: an empty column is the scarce resource in
        // Spider, and burying it is usually what loses the game.
        public void _DealStockRow()
        {
            if (resolvedDeck == null || TableauSlots == null || dealing) return;
            if (!_IsLocalGameOwner()) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            if (resolvedDeck._IsStockEmpty())
            {
                Debug.Log("Solitaire: Stock is empty; there are no more rows to deal.");
                return;
            }

            int empty = FindEmptyTableauColumn();
            if (empty >= 0)
            {
                Debug.Log($"Solitaire: Column {empty} is empty - every column has to be filled before dealing another row from the stock.");
                return;
            }

            dealing = true;
            dealPhase = DealPhaseStockRow;
            dealCol = 0;
            dealDepth = 0;
            dealOwner = local;
            SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
        }

        // Index of the first assigned tableau column holding no cards, or -1 when all
        // of them are occupied.
        private int FindEmptyTableauColumn()
        {
            if (TableauSlots == null) return -1;
            for (int s = 0; s < TableauSlots.Length; s++)
            {
                CardSlot slot = TableauSlots[s];
                if (slot == null) continue;
                if (!slot._IsOccupied()) return s;
            }
            return -1;
        }

        // Walks dealCol to the next column that actually exists. Mirrors
        // AdvanceToNextDealColumn, but a stock row wants exactly one card per column
        // rather than a per-column count.
        private bool AdvanceToNextStockColumn()
        {
            int columns = TableauSlots != null ? TableauSlots.Length : 0;
            while (dealCol < columns)
            {
                if (TableauSlots[dealCol] != null) return true;
                dealCol++;
            }
            return false;
        }

        private void DealNextStockCard()
        {
            if (!AdvanceToNextStockColumn())
            {
                FinalizeStockRow();
                return;
            }

            CardSlot slot = TableauSlots[dealCol];
            GameObject cardGO = resolvedDeck.DrawNext();
            if (cardGO == null)
            {
                // Stock ran dry mid-row. The cards already placed are legal where they
                // are, so there is nothing to unwind.
                FinalizeStockRow();
                return;
            }
            CardLogic card = cardGO.GetComponentInChildren<CardLogic>(true);
            if (card != null)
            {
                Networking.SetOwner(dealOwner, cardGO);
                // Stock cards always land face-up in Spider - that is the whole cost
                // of the row, and why burying an empty column matters.
                card._ForcePlace(slot._GetTopSlot(), true);
            }
            dealCol++;

            if (AdvanceToNextStockColumn())
            {
                SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
            }
            else
            {
                FinalizeStockRow();
            }
        }

        // Give up on a half-dealt row without touching the win state or reading back
        // through a deck that has gone away.
        private void AbandonStockRow()
        {
            dealing = false;
            dealPhase = DealPhaseNone;
            Debug.Log("Solitaire: Stock row abandoned; the deck or game owner went away mid-row.");
        }

        // Unlike FinalizeDeal this must not touch `won` or the win message: a row can
        // be dealt long after the game was won, and clearing it would retract a win
        // the player already earned.
        private void FinalizeStockRow()
        {
            dealing = false;
            dealPhase = DealPhaseNone;
            Debug.Log($"Solitaire: Dealt a stock row; {resolvedDeck.CardCount} cards left in the stock.");
            // A stock card lands on top of its column, which is the low end of a run -
            // so it is exactly the card that can complete one.
            CollectCompletedRuns();
            // A dealt row buries the card under it in every column, so most of the
            // table's grabbability just changed.
            RefreshAllPickupable();
            CheckWon();
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

        // Draw from the stock onto the waste pile - one card in Klondike, three in
        // Canfield.
        //
        // Only the first card goes out now; the rest ride the same throttled loop as
        // the deal, because three cards in one frame is three pool spawns, three
        // ownership transfers and three serializations - the exact burst DealDelay
        // exists to spread out. It also means the draw is over in under half a second,
        // so blocking pickups for its duration costs the player nothing.
        public void _DrawFromStock()
        {
            if (resolvedDeck == null || WasteSlot == null || dealing) return;
            if (!_IsLocalGameOwner()) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            if (!DrawOneToWaste(local)) return;

            int remaining = ResolveDrawCount() - 1;
            if (remaining <= 0 || resolvedDeck._IsStockEmpty())
            {
                // The waste's old top card just stopped being the top one, and a
                // TopOnly waste hands out only that card, so the whole table's
                // grabbability moved with it.
                RefreshAllPickupable();
                return;
            }

            dealing = true;
            dealPhase = DealPhaseDraw;
            drawRemaining = remaining;
            dealOwner = local;
            SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
        }

        private void DrawNextWasteCard()
        {
            if (WasteSlot == null || drawRemaining <= 0)
            {
                FinalizeDraw();
                return;
            }

            // A draw that runs into the bottom of the stock just comes up short, which
            // is the normal way a Canfield redeal ends.
            if (!DrawOneToWaste(dealOwner))
            {
                FinalizeDraw();
                return;
            }
            drawRemaining--;

            if (drawRemaining > 0) SendCustomEventDelayedSeconds(nameof(_DealNextCard), DealDelay);
            else FinalizeDraw();
        }

        // Turns exactly one card face-up onto the waste. False when the stock had
        // nothing left to give.
        private bool DrawOneToWaste(VRCPlayerApi owner)
        {
            if (!Utilities.IsValid(owner)) return false;
            GameObject cardGO = resolvedDeck.DrawNext();
            if (cardGO == null) return false;
            CardLogic card = cardGO.GetComponentInChildren<CardLogic>(true);
            if (card == null) return false;
            Networking.SetOwner(owner, cardGO);
            card._ForcePlace(WasteSlot._GetTopSlot(), true);
            return true;
        }

        private void FinalizeDraw()
        {
            dealing = false;
            dealPhase = DealPhaseNone;
            drawRemaining = 0;
            RefreshAllPickupable();
        }

        // Cards per stock click. 0 means "whatever the mode wants", so a table only
        // has to set this when it wants something other than the standard game.
        private int ResolveDrawCount()
        {
            if (DrawCount > 0) return DrawCount;
            return Mode == SolitaireMode.Canfield ? 3 : 1;
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
            dealPhase = DealPhaseNone;
            won = false;
            if (WinMessage != null) WinMessage.SetActive(false);
            // A column that DealCounts left empty is one the reserve owes a card to.
            RefillTableauFromReserve();
            // The deal placed every card without refreshing anything below them, so
            // the opening layout needs one sweep before the player can touch it.
            RefreshAllPickupable();
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
            resolvedDeck._ResetPosition();
            resolvedDeck._SetGameOwner(-1);

            gameStarted = false;
            dealing = false;
            dealPhase = DealPhaseNone;
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
                return;
            }

            // Spider only lets a group move when it is a same-suit descending run.
            // This cannot live in a SlotRule, which never sees the carried cards.
            // _RefreshPickupable should already have blocked the grab; this stays as
            // the backstop for the window before the sweep has run.
            if (!_IsGroupMovable(card))
            {
                card._Reject();
            }
        }

        // True when this card may be lifted along with whatever is stacked on it.
        // Single source of truth for the rule: the pickupable flag and the grab-time
        // backstop both come through here, so they cannot drift apart.
        public bool _IsGroupMovable(CardLogic card)
        {
            if (Mode != SolitaireMode.Spider) return true;
            if (card == null) return false;
            // Only tableau columns restrict groups; foundations and the waste have
            // their own pickup modes and never hold a partial run.
            if (!_IsTableauChain(card.PrevSlot)) return true;
            return IsMovableRun(card);
        }

        // True when the cards riding on `card` continue it as a same-suit run
        // descending by one. A lone card is trivially movable.
        //
        // Klondike gets this for free: its tableau rule is invariant, so any legal
        // pile above the dragged card is automatically a legal continuation. Spider
        // breaks that - a column can be a perfectly legal *arrangement* (descending,
        // mixed suits) and still not be a legal *group*.
        private bool IsMovableRun(CardLogic card)
        {
            if (card == null) return false;
            CardLogic below = card;
            int guard = 0;
            while (guard < ChainGuard)
            {
                if (below.Slot == null) return true;
                CardLogic above = below.Slot._GetCardAbove();
                if (above == null) return true; // nothing riding on it
                if (!above.FaceUp || above.IsJoker) return false;
                if ((int)above.CardSuit != (int)below.CardSuit) return false;
                if ((int)above.CardRank != (int)below.CardRank - 1) return false;
                below = above;
                guard++;
            }
            return false;
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
            CollectCompletedRuns();
            RefillTableauFromReserve();
            // After the pile has settled, not before: the flags are derived from the
            // final layout.
            RefreshAllPickupable();
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

        // Re-evaluates every card's pickupable flag.
        //
        // A card's grabbability depends on what is stacked *on* it - the pile's
        // top-only rule, and in Spider whether the cards above continue its run - but
        // _RefreshPickupable only ever runs for the card that moved, never for the
        // cards below it. So one card landing can silently change the answer for a
        // whole column, and without a sweep those cards keep a stale flag until
        // something touches them directly. Dropping a card onto a TopOnly pile has
        // always had this bug; Spider just makes it constant.
        //
        // Only the game owner's flags matter (everyone else is blocked outright), and
        // this runs once per move rather than per frame, so the cost is fine.
        private void RefreshAllPickupable()
        {
            if (cards == null) return;
            for (int i = 0; i < cards.Length; i++)
            {
                CardLogic card = cards[i];
                if (card == null) continue;
                if (!card.gameObject.activeInHierarchy) continue;
                // Never touch the held card: its own flag is moot while it is in hand,
                // and changing pickupable mid-hold is not worth the risk.
                if (card.Grabbed) continue;
                card._RefreshPickupable();
            }
        }

        private void RevealTops()
        {
            if (TableauSlots != null)
            {
                for (int s = 0; s < TableauSlots.Length; s++)
                {
                    CardSlot slot = TableauSlots[s];
                    if (slot == null) continue;
                    CardLogic top = slot._GetTopCard();
                    if (top != null && !top.FaceUp) top.SetFaceUp(true);
                }
            }

            // The reserve is dealt face-down under a single turned-over card, so
            // spending that card has to expose the next one. Null outside Canfield.
            if (ReserveSlot != null)
            {
                CardLogic reserveTop = ReserveSlot._GetTopCard();
                if (reserveTop != null && !reserveTop.FaceUp) reserveTop.SetFaceUp(true);
            }
        }

        // Canfield refills an empty column from the reserve rather than leaving it
        // open: the reserve is the pile you are actually racing to clear, and handing
        // it a free outlet is the whole shape of the game. Once it is spent the
        // columns are genuinely free, which is what _CanFillEmptyTableau tells the
        // tableau rule.
        //
        // Force-placed, so it bypasses that rule - this move is the game making itself,
        // not the player asking for something.
        private void RefillTableauFromReserve()
        {
            if (Mode != SolitaireMode.Canfield) return;
            if (TableauSlots == null || ReserveSlot == null) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            bool moved = false;
            for (int s = 0; s < TableauSlots.Length; s++)
            {
                CardSlot column = TableauSlots[s];
                if (column == null || column._IsOccupied()) continue;

                CardLogic top = ReserveSlot._GetTopCard();
                if (top == null) break; // reserve spent; the rest stay open
                Networking.SetOwner(local, top.gameObject);
                top._ForcePlace(column._GetTopSlot(), true);
                moved = true;
            }

            // Taking the reserve's top card uncovers the one beneath it.
            if (moved) RevealTops();
        }

        // True when this slot's pile is the reserve. Face-down reserve cards are no
        // more grabbable than face-down tableau ones, and CardLogic needs to be able
        // to tell the two piles apart to say so.
        public bool _IsReserveChain(CardSlot slot)
        {
            if (ReserveSlot == null || slot == null) return false;
            return slot._GetRootSlot() == ReserveSlot;
        }

        // Spider clears a finished king-to-ace run off the tableau to a foundation.
        //
        // Moving the king is the entire move. The queen down to the ace are parented
        // under him and their PrevSlot links still point at his own slot, so they come
        // along for free and only re-derive their offset from the foundation's layout.
        // That is one card's worth of network traffic instead of thirteen - and
        // thirteen ownership transfers plus serializations in a single frame is
        // precisely what DealDelay exists to spread out.
        private void CollectCompletedRuns()
        {
            if (Mode != SolitaireMode.Spider) return;
            if (TableauSlots == null || FoundationSlots == null) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            bool collected = false;
            bool foundationsFull = false;
            for (int s = 0; s < TableauSlots.Length && !foundationsFull; s++)
            {
                CardSlot column = TableauSlots[s];
                if (column == null) continue;

                // Taking one run can uncover another right beneath it, so keep pulling
                // from this column until it stops yielding. Bounded by the foundation
                // count, which is the most runs that can ever be collected.
                int taken = 0;
                while (taken < FoundationSlots.Length)
                {
                    CardLogic king = FindCompletedRun(column);
                    if (king == null) break;

                    CardSlot foundation = FindEmptyFoundation();
                    if (foundation == null)
                    {
                        Debug.Log("Solitaire: A run finished but every foundation is already full.");
                        foundationsFull = true;
                        break;
                    }

                    Networking.SetOwner(local, king.gameObject);
                    king._ForcePlace(foundation._GetTopSlot(), true);
                    _RepositionAbove(king);
                    collected = true;
                    taken++;
                    Debug.Log($"Solitaire: Collected a completed run of {king.CardSuit} from column {s}.");
                }
            }

            // Pulling a run off a column uncovers whatever was under it.
            if (collected) RevealTops();
        }

        // Walks down from a column's top card looking for a complete same-suit
        // ace-to-king run, and returns its king (the card that has to move) or null.
        // Bounded at 13 steps, so this is cheap enough to poll after every move.
        //
        // The run does not have to sit at the bottom of the column - it just has to be
        // the last 13 cards of it - so this reads downward from the top rather than
        // assuming anything about where the king is.
        private CardLogic FindCompletedRun(CardSlot column)
        {
            CardLogic current = column._GetTopCard();
            if (current == null || current.IsJoker || !current.FaceUp) return null;
            // A finished run always ends on the ace, which is the cheapest possible
            // rejection for the columns that have not finished one.
            if ((int)current.CardRank != 1) return null;

            for (int step = 1; step < CardLogic.RankDefinitionsCount; step++)
            {
                CardSlot below = current.PrevSlot;
                if (below == null) return null;
                CardLogic next = below.Owner; // null once the walk reaches a base slot
                if (next == null || next.IsJoker || !next.FaceUp) return null;
                if ((int)next.CardSuit != (int)current.CardSuit) return null;
                if ((int)next.CardRank != (int)current.CardRank + 1) return null;
                current = next;
            }
            return current;
        }

        private CardSlot FindEmptyFoundation()
        {
            if (FoundationSlots == null) return null;
            for (int i = 0; i < FoundationSlots.Length; i++)
            {
                CardSlot slot = FoundationSlots[i];
                if (slot == null) continue;
                if (!slot._IsOccupied()) return slot;
            }
            return null;
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
