using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using UCS;


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

    // Owns the board: where every card is, which way it is facing, and which of
    // them are in the dealer's hands. This behaviour is the only thing at the table
    // that syncs.
    //
    // Slot ids are handed out deterministically at startup - base slots first, in
    // inspector order, then one per pool card in pool order - so every client
    // agrees on them without any of it going over the network. That is the trick
    // that makes a linked list of object references syncable in Udon: only the id
    // travels, and the structure is rebuilt locally from it.
    //
    // The ids for a whole deck then travel together, as one array in one packet.
    // Cards used to carry their own links, which meant a single logical move landed
    // as one of 52 independent serializations: any of them could be dropped by the
    // rate limiter or arrive out of order, so clients saw the table half-applied,
    // and a lost write was permanent because a link is a delta with nothing later
    // to correct it. An array is absolute - the next packet restates the whole
    // truth - so a dropped one costs a moment of staleness instead of a stuck card.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class Solitaire : UdonSharpBehaviour
    {
        // A pile can never be deeper than the deck, so anything past this is a
        // cycle. Sized for a 104-card deck, not just the 52-card one.
        private const int ChainGuard = 128;

        [Header("Game")]
        [Tooltip("Which game this table plays. Everything the modes share is driven by the slot layout, DealCounts and the SlotRule components; this only switches the handful of mechanics that genuinely differ - what a stock click does, whether groups have to be same-suit runs, whether finished runs leave the table, and whether there is a reserve feeding the columns.")]
        public SolitaireMode Mode = SolitaireMode.Klondike;

        [Header("Entry Fee")]
        [Tooltip("UdonChips component used to deduct the entry fee when a game is dealt. Leave unassigned to disable fees.")]
        public UdonChips UdonChips;

        [Tooltip("Chips deducted to start a game.")]
        public float EntryFee = 0f;

        [Tooltip("Chips awarded per card placed on a foundation.")]
        public float FoundationPayout = 0f;

        [Tooltip("Base multiplier on the total accrued payout when the game is completed (won) rather than quit early. 1 means no bonus; 2 doubles the winnings only on a win, never on an early quit. An in-progress win streak adds a further bonus on top of this (see _StreakWinMultiplier).")]
        public float CompletePayoutMultiplier = 1f;

        [Header("Audio")]
        [Tooltip("Shared pool that plays the card dealing, movement and entry-fee sounds, locally or on every client.")]
        public AudioPool CardAudio;

        [Tooltip("Index into CardAudio's Clips list of the sound played once per card dealt onto the table.")]
        public int DealClipIndex = 0;

        [Tooltip("Index into CardAudio's Clips list of the sound played once per card moved to a new pile.")]
        public int MoveClipIndex = 1;

        [Tooltip("Index into CardAudio's Clips list of the sound played when the entry fee is deducted to start a game.")]
        public int PayClipIndex = 2;

        [Tooltip("Index into CardAudio's Clips list of the sound played when the game is won.")]
        public int WinClipIndex = 4;

        [Tooltip("Index into CardAudio's Clips list of the sound played when a Spider run completes and leaves the table.")]
        public int RunCompleteClipIndex = 3;

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

        [Tooltip("Seconds a completed Spider run takes to glide from its column to a foundation. Purely visual: the board is committed instantly, only the card's world position eases over this window.")]
        public float RunCollectDuration = 0.25f;

        [Tooltip("Seconds a dealt card takes to glide from the deck to its slot. Purely visual, like RunCollectDuration: the card is committed instantly and eases over this window.")]
        public float DealDuration = 0.2f;

        [Tooltip("Seconds to wait between dealing each card. Purely the pace of the animation - nothing per-card goes on the wire any more, so this is free to lower. Values below one frame just deal a card per frame. 0.05 gives a brisk Klondike deal in about 1.5s and a 54-card Spider one in under 3.")]
        public float DealDelay = 0.1f;

        [Header("Deal")]
        [Tooltip("How many cards go into each tableau column on the opening deal, in column order. {1,2,3,4,5,6,7} is Klondike; Spider two-suit wants {6,6,6,6,5,5,5,5,5,5}; Canfield wants {1,1,1,1}. Columns past the end of this array, and entries of 0, are skipped. Only the card that ends up on top of a column is dealt face-up.")]
        public int[] DealCounts = new int[] { 1, 2, 3, 4, 5, 6, 7 };

        [Tooltip("Canfield only: how many cards go into the reserve before the tableau is dealt. 13 is the standard game. Ignored when ReserveSlot is unassigned.")]
        public int ReserveCount = 13;

        [Header("Win")]
        [Tooltip("Banner over the table. Once every foundation holds a complete 13-card pile it shows its win text; before any game starts it shows the entry fee instead (i.e. \"200c to start\") and stays hidden for the duration of a running game so nobody mistakes a spoiler for the current state.")]
        public GameObject WinMessage;

        [Header("UI")]
        [Tooltip("Label on the start/quit trigger. Shows \"Start\" before a game, \"Quit\" while one is running.")]
        public LocalizedText StartButtonLabel;

        [Tooltip("Interact collider on the start/quit button. While a game is running by another player it is disabled so they cannot start a competing game.")]
        public Collider StartButtonInteract;

        [Tooltip("Generic interact on the start/quit button. Solitaire builds its hover prompt and tells it whether a press has to go through the confirmation canvas.")]
        public GameInteract StartInteract;

        [Tooltip("Generic interact on the reshuffle trigger. Shown only while the local player's running game can still be restarted for free - no card has moved since the deal - and hidden the rest of the time. Wire its GameInteract callback to _OnReshuffle.")]
        public GameInteract ReshuffleInteract;

        [Tooltip("Confirmation dialog shown when pressing Quit would abandon the game in progress.")]
        public GameObject ConfirmDialog;

        [Tooltip("Label inside the confirmation dialog. Solitaire appends the localized REFUND marker to it while the game can still be refunded (no card moved yet). Leave unassigned to resolve it from ConfirmDialog's children.")]
        public LocalizedText ConfirmDialogLabel;

        [Tooltip("Notification popup for player-facing messages (illegal moves, insufficient chips, etc.). Leave unassigned to silence them.")]
        public Notification Notification;

        [Header("Double or Nothing")]
        [Tooltip("Canvas shown after a win, offering to collect the winnings or stake them on a double-or-nothing game. Leave unassigned to disable the feature and pay out immediately as before. Wire its two buttons to _OnDoubleOrNothingCollect and _OnDoubleOrNothingDouble.")]
        public GameObject DoubleOrNothingDialog;

        [Tooltip("Label on the dialog showing the amount on offer. Solitaire writes it with the DOUBLE_OR_NOTHING key plus a chip suffix; add that key to the localization table.")]
        public LocalizedText DoubleOrNothingAmountLabel;

        [Tooltip("Localization manager used to translate notification text. Falls back to a scene search if empty.")]
        [SerializeField] private LocalizationManager localization;

        // The per-player deck actually used for the running game. Unlike the
        // serialized DeckOfCards (the scene template/fallback reference), this one
        // is repointed at the dealing player's PlayerObject copy on every deal, so
        // the original DeckOfCards keeps pointing at the same deck it was assigned.
        private DeckManager resolvedDeck;

        // Player id of whoever dealt the game currently running at this table, or
        // -1 when the table is idle. The slot registry is rebuilt locally on every
        // client (slot ids are deterministic), but a client can only build it
        // against the right deck PlayerObject if it knows whose deck that is - so
        // this has to travel alongside the board it indexes into. Written by the dealer
        // right after taking ownership of the table; cleared by _ResetGame and by
        // whoever inherits the table when the dealer leaves mid-game.
        [UdonSynced] private int syncedDealerId = -1;

        // The last syncedDealerId this client already reacted to. OnDeserialization
        // fires for every sync touching this behaviour, so without this memo each
        // spectator would rebuild the whole registry on every card move.
        private int lastDealerId = -1;

        // The board. One row per pool card: boardSlots[i] is that card's PrevSlotId
        // (-1 when it is loose), boardFlags[i] its face state. Indexed by pool
        // index, which is the one card identity every client already agrees on -
        // VRCObjectPool.Shuffle permutes a private draw order, never the Pool array.
        [UdonSynced] private int[] boardSlots;
        [UdonSynced] private byte[] boardFlags;

        private const int FlagFaceUp = 1;
        private const int FlagFaceVisible = 2;

        // The up-to-two cards currently in the dealer's hands, as pool indices,
        // with which hand each is in and the pose it sits at relative to that hand
        // bone. Only the dealer may grab, so the holder is always syncedDealerId
        // and does not need syncing alongside them.
        [UdonSynced] private int grabCardA = -1;
        [UdonSynced] private int grabCardB = -1;
        [UdonSynced] private bool grabLeftA;
        [UdonSynced] private bool grabLeftB;
        // Never read unless the matching slot holds a card, and a slot is only ever
        // filled together with a pose, so these have no meaningful default.
        [UdonSynced] private Vector3 grabPoseA;
        [UdonSynced] private Vector3 grabPoseB;
        [UdonSynced] private Quaternion grabRotationA;
        [UdonSynced] private Quaternion grabRotationB;

        private bool boardDirty;
        private bool flushScheduled;
        private bool reapplyScheduled;
        private float lastFlushTime = -999f;
        private float lastGrabSyncTime = -999f;

        // A deal writes a card every DealDelay, and one drop can rewrite half a
        // dozen. Serializing each of those separately would put the whole board on
        // the wire several times a second and run into the same rate limiter that
        // used to eat the per-card writes, so writes are coalesced and, while the
        // dealer is mid-deal, throttled to this. A spectator still watches the deal
        // fill in, just in coarser steps.
        private const float DealFlushInterval = 0.25f;

        // The held pose is against the holder's hand bone, so it only has to be
        // re-sent when the card shifts within the hand - which on desktop means
        // when the holder re-aims it. Carrying it around the room sends nothing.
        private const float GrabResyncInterval = 0.2f;
        private const float GrabDriftEpsilonSqr = 4e-4f; // (2cm)^2
        private const float GrabDriftDotSqr = 0.9981f;   // cos(2.5deg)^2, i.e. 5deg apart

        // Guards the spectator-side registry retry: the dealer id can arrive before
        // the dealer's PlayerObjects have spawned on this client, and the registry
        // is useless until they do.
        private int spectatorInitRetries;
        private CardLogic[] cards;
        private CardSlot[] slotsById;

        // Reverse of the linked list: cardOnSlot[id] is the card whose PrevSlotId is
        // id. Rebuilt lazily because a scan per link is what _GetCardOn used to cost,
        // and every chain walk calls it once per level - so a drop, which tests every
        // slot in the game, paid cards x slots. Cheap at 52 cards, a frame hitch at
        // 104.
        private CardLogic[] cardOnSlot;
        private bool indexDirty = true;

        // The banner's localized text component. Before any game it shows the entry
        // fee, during a running game it shows the accrued payout, and once won it
        // shows the win message.
        private LocalizedText winMessageText;

        // The interactable behind the start/quit button is the GameInteract
        // behaviour. Solitaire builds its hover prompt from the same state as the
        // button label and decides when a press needs confirming.
        private int baseSlotCount;
        private Transform cardHome;

        // The table's angle pivot, found under this object at startup. It is told
        // whether a game is running and whether the local player owns it, so it can
        // hide its interact and fall back to its default angle while the table is
        // idle. Left null on a table with no pivot.
        private ChangeAngle changeAngle;
        private bool dealing;
        private bool gameStarted;
        private bool won;

        // True once the dealer has moved (or drawn) a single card this game. While
        // false, an early quit refunds the entry fee, because the player paid for a
        // game they never actually played.
        private bool cardsMoved;

        // Total payout accrued by this game. Credited into the wallet as it is earned
        // (see AccumulateFoundationReward) rather than held to the finish: UdonChips
        // only persists money on its own auto-save cadence, so a payout still sitting
        // in this field when a player quits the world never reaches their saved wallet.
        private float payoutEarned;

        // How much of payoutEarned has already been pushed into UdonChips.money. The
        // difference is what a win bonus still owes (payoutEarned is multiplied by the
        // bonus before CreditPayout runs, so the gap is exactly the bonus).
        private float payoutCredited;

        // Double-or-nothing state. Only the dealer's client ever runs the win/payout
        // path, so none of this needs to sync - spectators see the same board either
        // way and never touch the money.
        //
        // doubleStake is the amount carried over from a previous win and put at risk,
        // including whatever the dealer clawed back out of their wallet to do so. A
        // double-or-nothing win pays exactly twice this - no streak multiplier, no
        // foundation accrual folded in - and a loss or quit forfeits it.
        private bool doubleOrNothingGame;
        private float doubleStake;

        // PlayerData prefixes for the lifetime tally, one game/won counter per mode.
        // The mode name is appended so Klondike, Spider and Canfield keep separate
        // records for the local player, persisted across sessions by the SDK.
        private const string GamesKeyPrefix = "Solitaire.Games.";
        private const string WinsKeyPrefix = "Solitaire.Wins.";
        // Consecutive wins: bumped on every win, dropped back to zero on every
        // game the player abandons without finishing. Kept per mode alongside the
        // games/wins counters, for the same reason.
        private const string WinStreakKeyPrefix = "Solitaire.Streak.";

        // PlayerData may only be touched after OnPlayerRestored. The flag gates the
        // counting so a record can never be written (or read) before the SDK has
        // loaded this player's saved data.
        private bool playerDataReady;

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
        // (like drawing from the stock) is gated on this. gameStarted is only
        // ever set on the dealer's client - the synced dealer id is what makes
        // the answer true for spectators and late joiners too.
        public bool _IsGameStarted()
        {
            return gameStarted || syncedDealerId != -1;
        }

        // True while the throttled deal/draw loop is still placing cards. Cards
        // stay ungrabbable for its duration - the deal places them without
        // consulting rules, so one lifted mid-deal would leave the layout and
        // the links disagreeing. Only the dealer ever sees this true: the loop
        // runs on their client alone, and nobody else may grab cards anyway.
        public bool _IsDealing()
        {
            return dealing;
        }

        // True when the local player is the one who dealt the current game. Drives
        // who is allowed to grab cards and poke the deck.
        //
        // Identity, not object ownership. The two normally agree - Deal takes the
        // table and writes the dealer id in the same breath - but ownership is a
        // separate message on its own schedule, and a scene object starts out owned
        // by the instance master. Asking IsOwner therefore had a window where the
        // master's client said yes to a game that was not theirs, and the dealer's
        // own client said no to one that was. The dealer id flips atomically with
        // the board it describes, so it cannot disagree with what is on the table.
        public bool _IsLocalGameOwner()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;
            return syncedDealerId != -1 && syncedDealerId == local.playerId;
        }

        // Whether this client may author the table's synced state. Object ownership
        // rather than identity, because this is the question RequestSerialization
        // actually asks - and because it is how a departed dealer gets cleaned up:
        // whoever VRChat hands the table to is the one who can clear the ghost
        // dealer id, and by then that is nobody's game by identity.
        private bool _IsLocalTableOwner()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;
            return Networking.IsOwner(local, gameObject);
        }

        private void Start()
        {
            if (localization == null)
            {
                GameObject locGo = GameObject.Find("Localization");
                if (locGo != null) localization = locGo.GetComponent<LocalizationManager>();
            }
            // Registered so a language switch rebuilds the start button's hover
            // prompt, which Solitaire now owns.
            if (localization != null) localization.Register(this);
            if (WinMessage != null)
            {
                winMessageText = WinMessage.GetComponentInChildren<LocalizedText>(true);
                RefreshWinMessage();
            }
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
            if (ConfirmDialogLabel == null && ConfirmDialog != null) ConfirmDialogLabel = ConfirmDialog.GetComponentInChildren<LocalizedText>(true);
            if (DoubleOrNothingDialog != null) DoubleOrNothingDialog.SetActive(false);
            if (StartInteract != null)
            {
                StartInteract.CallbackEvent = nameof(_OnStartPressed);
                // Fall back to the dialog on this table when the prefab leaves the
                // generic slot unwired.
                if (StartInteract.ConfirmCanvas == null) StartInteract.ConfirmCanvas = ConfirmDialog;
            }

            // A pivot under this object (ChangeAngle) moves the table's card places
            // with it, but the deck is a per-player PlayerObject parked at CardHome
            // by world transform, so it has to be told to follow. Register here so
            // nothing in the inspector has to be wired; the event is only meaningful
            // once a game has parked a deck, which _OnPivotMoved checks for itself.
            changeAngle = GetComponentInChildren<ChangeAngle>(true);
            if (changeAngle != null)
            {
                changeAngle._SetAngleChangedTarget(
                    (UdonBehaviour)GetComponent(typeof(UdonBehaviour)), nameof(_OnPivotMoved));
            }

            // Registered before the refresh below so the pivot is told the table's
            // state on the same pass that lights up the start button.
            _RefreshStartInteractable();
        }

        private void Init(VRCPlayerApi dealer)
        {
            resolvedDeck = ResolveDeck(dealer);
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
                logic.PoolIndex = i;

                CardSlot slot = logic.GetComponent<CardSlot>();
                if (slot == null) continue;
                int id = baseSlotCount + i;
                logic.Slot = slot;
                slot.Owner = logic;
                slot.SlotId = id;
                slot.Solitaire = this;
                slotsById[id] = slot;
            }

            // A spectator may already be holding a board that arrived before the
            // registry was ready - it deserialized into these fields whether or not
            // anything could read it yet, and it is the authoritative copy. Only
            // allocate when there isn't one that fits.
            if (boardSlots == null || boardSlots.Length != n)
            {
                boardSlots = new int[n];
                for (int i = 0; i < n; i++) boardSlots[i] = -1;
            }
            if (boardFlags == null || boardFlags.Length != n)
            {
                boardFlags = new byte[n];
            }

            // slotsById was just replaced, so any index built against the old one is
            // meaningless. Has to happen before ApplyBoard reads it.
            indexDirty = true;

            // Whatever the board already says, apply it. On the dealer that is the
            // previous game, which ResetCards is about to clear; on a spectator it
            // is the running game they just found out about.
            ApplyBoard();

            RefreshStartLabel();
            _RefreshStartInteractable();
            RefreshWinMessage();
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
            if (DoubleOrNothingDialog != null) DoubleOrNothingDialog.SetActive(false);
            Debug.Log($"Solitaire: Initialized with {n} cards and {baseSlotCount} base slots.");
        }

        private DeckManager ResolveDeck(VRCPlayerApi dealer)
        {
            // The deck has to be the DEALING player's own PlayerObject copy, not the
            // table owner's: PlayerObject ownership can never be transferred, so the
            // dealer is only guaranteed to own every card and pool they write if the
            // game runs on their own deck. Resolving by table ownership instead would
            // hand the first deal to the instance master's deck and silently drop
            // everyone else's writes.
            DeckManager deck = Utilities.IsValid(dealer) ? FindDeck(dealer) : null;
            if (Utilities.IsValid(deck))
            {
                Debug.Log($"Solitaire: Using {dealer.displayName}'s deck PlayerObject for key {DeckKey}.");
                return deck;
            }

            // Nothing matched. The bare "Deck not assigned" that the callers log next
            // is misleading on its own, because the usual cause is that a deck *is*
            // there carrying a different DeckKey - so name the keys that were on offer.
            ReportDeckKeyMiss(dealer);
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
            // Null for a player whose objects have not spawned yet on this client -
            // normal for someone still loading in, and for the window where the
            // dealer id syncs before the dealer's PlayerObjects do.
            if (objects == null) return null;
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
                }
            }
            return false;
        }

        // A won game at another table is finished but not cleared: the cards are
        // still laid out and the table still belongs to the dealer. When the same
        // player deals a fresh game elsewhere, that old table has to be torn down
        // first or it stays locked to them with a dead board on it. Walk the
        // player's decks - a won deck is that deck's own record of "won, not yet
        // reset" - and reset whichever table each one points at.
        private void ClearFinishedGames(VRCPlayerApi owner)
        {
            if (!Utilities.IsValid(owner)) return;

            var objects = Networking.GetPlayerObjects(owner);
            // Null while the player's objects are still spawning; the new game does
            // not depend on this cleanup, so a miss just leaves the old table up.
            if (objects == null) return;

            for (int i = 0; i < objects.Length; i++)
            {
                if (!Utilities.IsValid(objects[i])) continue;
                DeckManager[] found = objects[i].GetComponentsInChildren<DeckManager>(true);
                if (found == null) continue;
                for (int d = 0; d < found.Length; d++)
                {
                    DeckManager deck = found[d];
                    if (!Utilities.IsValid(deck)) continue;
                    if (!deck.GameWon) continue;

                    Solitaire table = deck.Solitaire;
                    if (table != null && table != this) table._ResetGame();
                }
            }
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

        // Rebuilt lazily: a deal touches every card, and only the first read after
        // it pays. Every write path sets indexDirty on the way through _WriteCard
        // or ApplyBoard, which is the only way a link changes now.
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
                // Two cards claiming one slot is no longer reachable through the
                // network - the board arrives whole - but a half-written frame on
                // the dealer's own client still passes through here. First in pool
                // order wins, which is what the old linear scan returned.
                if (cardOnSlot[id] == null) cardOnSlot[id] = card;
            }
        }

        // ---- Board
        //
        // Every change to where a card sits or which way it faces goes through
        // _WriteCard, and every client rebuilds the table from ApplyBoard. Those
        // two are the entire network contract for the game.

        // The one place the board changes. Updates the row, mirrors it straight
        // into the card so local readers see it this frame, and queues the packet.
        public void _WriteCard(CardLogic card, int slotId, bool faceUp, bool faceVisible)
        {
            if (card == null) return;

            int i = card.PoolIndex;
            if (boardSlots != null && boardFlags != null
                && i >= 0 && i < boardSlots.Length && i < boardFlags.Length)
            {
                int bits = 0;
                if (faceUp) bits |= FlagFaceUp;
                if (faceVisible) bits |= FlagFaceVisible;
                byte flags = (byte)bits;

                if (boardSlots[i] != slotId || boardFlags[i] != flags)
                {
                    boardSlots[i] = slotId;
                    boardFlags[i] = flags;
                    MarkBoardDirty();
                }
            }

            ApplyCard(card, slotId, faceUp, faceVisible);
        }

        // Push one row into its card and re-derive everything local from it.
        private void ApplyCard(CardLogic card, int slotId, bool faceUp, bool faceVisible)
        {
            card._ApplyState(slotId, faceUp, faceVisible);
            // The link just changed under us and everything below walks the chain,
            // so the reverse index has to be dropped before any of it reads back.
            indexDirty = true;

            if (slotId < 0) card._ReturnHome(cardHome);
            else card._ApplyPlacement();
            card.ApplyFaceTexture();
            // Free during a deal: _RefreshPickupable answers "nobody may grab
            // anything right now" without walking the pile.
            card._RefreshPickupable();
        }

        // Re-derive the whole table from the board. Cheap to call as often as we
        // like: it is a pure function of the arrays, and _RefreshPlacement skips
        // every card already sitting where it belongs, which is nearly all of them.
        private void ApplyBoard()
        {
            if (cards == null || boardSlots == null || boardFlags == null) return;

            int n = cards.Length;
            if (boardSlots.Length < n) n = boardSlots.Length;
            if (boardFlags.Length < n) n = boardFlags.Length;

            // Mirror every row before placing any of them. Placement reads back
            // through the chain, and a fan window reads how many cards are above -
            // neither answer is right until all of the links are in.
            for (int i = 0; i < n; i++)
            {
                CardLogic card = cards[i];
                if (card == null) continue;
                int flags = boardFlags[i];
                card._ApplyState(boardSlots[i], (flags & FlagFaceUp) != 0, (flags & FlagFaceVisible) != 0);
            }
            indexDirty = true;

            for (int i = 0; i < n; i++)
            {
                CardLogic card = cards[i];
                if (card == null) continue;
                if (card.PrevSlotId < 0) card._ReturnHome(cardHome);
                else card._ApplyPlacement();
                card.ApplyFaceTexture();
            }

            // Placement above ran in pool order; a fan window needs one pass in
            // pile order on top of it.
            _RelayoutFannedPiles();
            RefreshAllPickupable();
        }

        // A card just went active on this client. Its row may have arrived while the
        // object was still disabled, in which case nothing placed it.
        //
        // Coalesced rather than handled per card: a deal activates a card every
        // frame or two, and on a spectator the pool can bring a whole batch up at
        // once when a sync lands. Applying one row costs a relayout of every fanned
        // pile, so doing that per card turned a fast deal into a burst of O(n) work
        // on clients that have nothing else to do with it. One re-apply per frame
        // covers any number of arrivals, and the board is absolute so it does not
        // matter which of them triggered it.
        public void _OnCardSpawned(CardLogic card)
        {
            if (card == null) return;
            // A card going active joins the slot index, whoever we are.
            indexDirty = true;

            // The dealer placed this card in the frame it spawned - _ForcePlace ran
            // straight after TryToSpawn - so there is nothing to catch up on, and
            // re-applying would cost a whole board pass per dealt card. This exists
            // for the clients that *receive* the board.
            if (_IsLocalGameOwner()) return;

            if (reapplyScheduled) return;
            reapplyScheduled = true;
            SendCustomEventDelayedFrames(nameof(_ReapplyBoard), 0);
        }

        public void _ReapplyBoard()
        {
            reapplyScheduled = false;
            ApplyBoard();
        }

        private void MarkBoardDirty()
        {
            boardDirty = true;
            if (flushScheduled) return;
            flushScheduled = true;

            // Mid-deal the writes come in a steady stream, so hold them to the
            // throttle. Otherwise a frame is enough to collect everything a single
            // move touched - the card that landed, the one it uncovered, a run
            // collected off the back of it, a reserve refill.
            float wait = 0f;
            if (dealing)
            {
                float since = Time.time - lastFlushTime;
                if (since < DealFlushInterval) wait = DealFlushInterval - since;
            }
            if (wait > 0f) SendCustomEventDelayedSeconds(nameof(_FlushBoard), wait);
            else SendCustomEventDelayedFrames(nameof(_FlushBoard), 1);
        }

        public void _FlushBoard()
        {
            flushScheduled = false;
            if (!boardDirty) return;
            boardDirty = false;
            // Ownership, not identity: this is exactly the precondition for
            // RequestSerialization landing. It also stays true for the moment after
            // _ResetGame clears the dealer id, which is when the emptied board has
            // to go out.
            if (!_IsLocalTableOwner()) return;
            lastFlushTime = Time.time;
            RequestSerialization();
        }

        // A throttled serialization is dropped, not queued. The board is absolute,
        // so the recovery is simply to ask again - and there is no retry counter,
        // because giving up would strand every other client on a stale table for
        // the rest of the game. Backed off so a refusal doesn't retry into itself.
        public override void OnPostSerialization(SerializationResult result)
        {
            if (result.success) return;
            boardDirty = true;
            if (flushScheduled) return;
            flushScheduled = true;
            SendCustomEventDelayedSeconds(nameof(_FlushBoard), DealFlushInterval);
        }

        // ---- Grab
        //
        // A held card is the one thing the board can't describe on its own: it is
        // not in a pile, and it moves every frame. Two slots cover the whole deck,
        // because only the dealer may grab and they have two hands.
        //
        // The pose is stored relative to the holder's hand *bone* and captured once
        // per grab, so carrying a card across the table costs nothing on the wire -
        // remotes rebuild it from an avatar they are already receiving.
        //
        // The bone, specifically, and not GetTrackingData. Tracking data on the
        // holder's own client is the raw device input that feeds their IK; on
        // everyone else's it is what came off the network. For a desktop holder,
        // whose hands are synthesised rather than tracked, those are not the same
        // frame, so a pose captured against one and rebuilt against the other lands
        // in the wrong place. A hand bone is the far side of that solve: it is the
        // avatar everyone can already see, and it means the same thing everywhere.

        public bool _IsCardGrabbed(CardLogic card)
        {
            if (card == null) return false;
            int i = card.PoolIndex;
            if (i < 0) return false;
            return grabCardA == i || grabCardB == i;
        }

        // Take whichever slot is free and record where the card sits in its hand.
        public void _BeginGrab(CardLogic card)
        {
            if (card == null || card.PoolIndex < 0) return;

            bool useA = grabCardA < 0 || grabCardA == card.PoolIndex;
            int held = card._GetHeldHand();
            // currentHand is still None this early in OnPickup if VRChat has not
            // attached the pickup yet. Right is the better coin toss - it is what
            // desktop always uses - and the recapture below settles it either way.
            bool isLeft = held == 1;

            if (useA)
            {
                grabCardA = card.PoolIndex;
                grabLeftA = isLeft;
            }
            else
            {
                grabCardB = card.PoolIndex;
                grabLeftB = isLeft;
            }

            CaptureGrabPose(useA);
            MarkBoardDirty();

            // OnPickup runs before the attach, and on desktop AutoHold teleports the
            // card to a fixed hold pose straight afterwards, so what we just
            // captured is still the card lying in its pile. Take it again - hand and
            // all - once the attach has actually landed.
            SendCustomEventDelayedFrames(nameof(_RecaptureGrabPose), 1);
        }

        public void _RecaptureGrabPose()
        {
            if (grabCardA < 0 && grabCardB < 0) return;

            RebindGrabHand(true);
            RebindGrabHand(false);

            if (grabCardA >= 0) CaptureGrabPose(true);
            if (grabCardB >= 0) CaptureGrabPose(false);
            lastGrabSyncTime = Time.time;
            MarkBoardDirty();
        }

        // Ask VRChat which hand the card actually ended up in, now that it is
        // attached, and correct the guess _BeginGrab had to make.
        private void RebindGrabHand(bool slotA)
        {
            CardLogic card = ResolveGrabbedCard(slotA);
            if (card == null) return;

            int held = card._GetHeldHand();
            if (held < 0) return; // not held any more; OnDrop clears the slot

            if (slotA) grabLeftA = held == 1;
            else grabLeftB = held == 1;
        }

        private void CaptureGrabPose(bool slotA)
        {
            CardLogic card = ResolveGrabbedCard(slotA);
            if (card == null) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            HumanBodyBones bone = (slotA ? grabLeftA : grabLeftB)
                ? HumanBodyBones.LeftHand
                : HumanBodyBones.RightHand;

            // An avatar with no such bone reports it at exactly the origin. That
            // sentinel is the only way to ask: GetBoneTransform, which would say so
            // directly, is not exposed to Udon.
            Vector3 bonePosition = local.GetBonePosition(bone);
            if (bonePosition == Vector3.zero) return;

            Quaternion boneRotation = local.GetBoneRotation(bone);
            Quaternion invBone = Quaternion.Inverse(boneRotation);

            Transform root = card.CardRoot != null ? card.CardRoot : card.transform;
            if (slotA)
            {
                grabPoseA = invBone * (root.position - bonePosition);
                grabRotationA = invBone * root.rotation;
            }
            else
            {
                grabPoseB = invBone * (root.position - bonePosition);
                grabRotationB = invBone * root.rotation;
            }
        }

        public void _EndGrab(CardLogic card)
        {
            if (card == null) return;
            int i = card.PoolIndex;
            if (i < 0) return;

            bool changed = false;
            if (grabCardA == i) { grabCardA = -1; changed = true; }
            if (grabCardB == i) { grabCardB = -1; changed = true; }
            if (changed) MarkBoardDirty();
        }

        // True when `slotId` is where a held card's grab started and that held card
        // is not `except`. A slot a held card vacated is not really free: the card
        // hovering above it snaps back there if its own drop finds nowhere, so a
        // second card dropped into it would leave two cards claiming one slot.
        public bool _IsSlotReservedByHeld(int slotId, CardLogic except)
        {
            if (slotId < 0) return false;
            CardLogic a = ResolveGrabbedCard(true);
            if (a != null && a != except && a._GetSavedPrevSlotId() == slotId) return true;
            CardLogic b = ResolveGrabbedCard(false);
            if (b != null && b != except && b._GetSavedPrevSlotId() == slotId) return true;
            return false;
        }

        // Drop whatever is in hand without running the drop rules. The slots are
        // cleared before the pickups are released, so the OnDrop each release fires
        // finds nothing left to clear and can't re-enter this.
        public void _ClearAllGrabs()
        {
            int a = grabCardA;
            int b = grabCardB;
            if (a < 0 && b < 0) return;

            grabCardA = -1;
            grabCardB = -1;
            MarkBoardDirty();

            ReleaseGrabbedCard(a);
            ReleaseGrabbedCard(b);
        }

        private void ReleaseGrabbedCard(int index)
        {
            if (index < 0 || cards == null || index >= cards.Length) return;
            CardLogic card = cards[index];
            if (card != null) card._ForceRelease();
        }

        // Blank the board without queueing it. Used when a game ends on this client:
        // the cards are going back to the pool regardless, and leaving the last
        // layout in the array would have the next deal lay it back out - over a
        // different player's deck - in the window before their first packet lands.
        private void ClearBoard()
        {
            if (boardSlots != null)
            {
                for (int i = 0; i < boardSlots.Length; i++) boardSlots[i] = -1;
            }
            if (boardFlags != null)
            {
                for (int i = 0; i < boardFlags.Length; i++) boardFlags[i] = 0;
            }
            grabCardA = -1;
            grabCardB = -1;
        }

        private CardLogic ResolveGrabbedCard(bool slotA)
        {
            int index = slotA ? grabCardA : grabCardB;
            if (index < 0 || cards == null || index >= cards.Length) return null;
            return cards[index];
        }

        // The dealer re-sends the held pose only when the card has shifted in their
        // hand; everyone else rebuilds it from the holder's hand bone every frame.
        private void Update()
        {
            if (grabCardA < 0 && grabCardB < 0) return;

            if (_IsLocalGameOwner())
            {
                // Desktop lets the holder re-aim a card after grabbing it, so the
                // pose is not fixed for the life of the grab. Carrying it around is
                // free though: the offset is against the hand, so walking, turning
                // and waving all move the bone and the card together and send
                // nothing.
                if (Time.time - lastGrabSyncTime < GrabResyncInterval) return;
                if (!GrabPoseDrifted(true) && !GrabPoseDrifted(false)) return;
                _RecaptureGrabPose();
                return;
            }

            VRCPlayerApi holder = VRCPlayerApi.GetPlayerById(syncedDealerId);
            if (!Utilities.IsValid(holder)) return;
            FollowGrab(holder, true);
            FollowGrab(holder, false);
        }

        private bool GrabPoseDrifted(bool slotA)
        {
            CardLogic card = ResolveGrabbedCard(slotA);
            if (card == null) return false;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;

            HumanBodyBones bone = (slotA ? grabLeftA : grabLeftB)
                ? HumanBodyBones.LeftHand
                : HumanBodyBones.RightHand;

            Vector3 bonePosition = local.GetBonePosition(bone);
            if (bonePosition == Vector3.zero) return false;

            Quaternion invBone = Quaternion.Inverse(local.GetBoneRotation(bone));
            Transform root = card.CardRoot != null ? card.CardRoot : card.transform;
            Vector3 offset = invBone * (root.position - bonePosition);
            Quaternion rotation = invBone * root.rotation;

            Vector3 storedOffset = slotA ? grabPoseA : grabPoseB;
            if ((offset - storedOffset).sqrMagnitude > GrabDriftEpsilonSqr) return true;

            // Angle between two orientations is 2*acos(|dot|), so comparing the
            // squared dot against cos(half-angle) squared says the same thing
            // without the trig.
            Quaternion stored = slotA ? grabRotationA : grabRotationB;
            float dot = stored.x * rotation.x + stored.y * rotation.y
                + stored.z * rotation.z + stored.w * rotation.w;
            return dot * dot < GrabDriftDotSqr;
        }

        private void FollowGrab(VRCPlayerApi holder, bool slotA)
        {
            CardLogic card = ResolveGrabbedCard(slotA);
            if (card == null || !card.gameObject.activeInHierarchy) return;

            HumanBodyBones bone = (slotA ? grabLeftA : grabLeftB)
                ? HumanBodyBones.LeftHand
                : HumanBodyBones.RightHand;

            // No humanoid rig, no bone to hang the card off - and the sentinel for
            // that is the bone reporting itself at the origin, which is also
            // exactly where the card would end up if we used it anyway. Leaving the
            // card where it is beats flinging it across the world; the drop will
            // put it right.
            Vector3 bonePosition = holder.GetBonePosition(bone);
            if (bonePosition == Vector3.zero) return;

            Quaternion boneRotation = holder.GetBoneRotation(bone);

            Transform root = card.CardRoot != null ? card.CardRoot : card.transform;
            root.position = bonePosition + boneRotation * (slotA ? grabPoseA : grabPoseB);
            root.rotation = boneRotation * (slotA ? grabRotationA : grabRotationB);
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

        // Re-lay every pile that has a fan window, because in those the resting place
        // of a card depends on how many cards are above it - so a draw-three waste
        // restates the position of the card falling out of the window, and of the one
        // rising into it, without either of them having moved.
        //
        // Runs on every client rather than being sent: offsets are derived from the
        // synced links, never serialized, so each client reaches the same layout on
        // its own. That is also why this is safe to call liberally - _RefreshPlacement
        // is a no-op for a card already sitting where it belongs, which is nearly all
        // of them, and piles with no fan window are skipped outright.
        public void _RelayoutFannedPiles()
        {
            if (slotsById == null) return;
            int bases = baseSlotCount < slotsById.Length ? baseSlotCount : slotsById.Length;
            for (int i = 0; i < bases; i++)
            {
                CardSlot slot = slotsById[i];
                if (slot == null || slot.FanCount <= 0) continue;

                CardSlot current = slot;
                int guard = 0;
                while (guard < ChainGuard)
                {
                    CardLogic above = _GetCardOn(current);
                    if (above == null) break;
                    above._RefreshPlacement();
                    if (above.Slot == null) break;
                    current = above.Slot;
                    guard++;
                }
            }
        }

        // The interactable lives on the start button now (driven by GameInteract,
        // which forwards presses here), so only that button lights up on hover.
        //
        // Quitting a game in progress no longer lands here: GameInteract routes
        // those presses through the confirmation canvas, whose buttons call
        // _ConfirmNewGame/_CancelNewGame. Only the presses that fire straight
        // through arrive here.
        public void _OnStartPressed()
        {
            if (gameStarted)
            {
                if (!_IsLocalGameOwner())
                {
                    Debug.Log("Solitaire: Only the player who started the game may quit it.");
                    ShowNotification("NOTIFY_ONLY_OWNER_CAN_QUIT");
                    return;
                }

                if (won)
                {
                    // A pending double-or-nothing decision owns the flow; the canvas is
                    // the only way forward, so the start button does nothing until the
                    // player collects or doubles (which would otherwise drop the pot).
                    if (DoubleOrNothingDialog != null && DoubleOrNothingDialog.activeSelf) return;
                    _ResetGame();
                    return;
                }

                return;
            }
            Deal(Networking.LocalPlayer);
        }

        // User confirmed quitting; settle the accrued payout, tear the game down to
        // the pre-deal state, then flash the total earned on the banner.
        public void _ConfirmNewGame()
        {
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
            if (won)
            {
                _ResetGame();
                return;
            }
            RecordGameLost();
            float earned;
            if (doubleOrNothingGame)
            {
                // Quitting a double-or-nothing game forfeits the whole pot: the stake
                // carried over from the previous win and this game's deferred accrual.
                // Nothing was credited, so there is nothing to claw back - just drop it.
                doubleStake = 0f;
                doubleOrNothingGame = false;
                payoutEarned = 0f;
                payoutCredited = 0f;
                earned = 0f;
            }
            else
            {
                // Nothing moved since the deal, so the player never actually played
                // the game they paid to enter: give the entry fee straight back.
                if (!cardsMoved && EntryFee > 0f && UdonChips != null) UdonChips.money += EntryFee;
                earned = CreditPayout();
            }
            _ResetGame();
            if (WinMessage != null && UdonChips != null && earned > 0f)
            {
                if (winMessageText != null) winMessageText.SetPrefixKeyAndSuffix("", "YOU_EARNED", " " + BuildPayoutAmount() + "c");
                WinMessage.SetActive(true);
                SendCustomEventDelayedSeconds(nameof(_RevertIdleBanner), 4f);
            }
        }

        // Reverts the banner to the idle entry-fee prompt after an early-quit payout
        // has been shown for a moment. Does nothing if a win took over meanwhile.
        private void _RevertIdleBanner()
        {
            if (won) return;
            RefreshWinMessage();
        }

        // User backed out; just close the confirmation dialog.
        public void _CancelNewGame()
        {
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
        }

        // Restart the running game from scratch. Only offered while not a single
        // card has moved since the deal, so the game was never actually played: the
        // player gets a fresh deal for the fee they already paid, exactly as if they
        // had quit for the refund and pressed Start again, but without the table
        // ever leaving their game.
        public void _OnReshuffle()
        {
            if (!_IsLocalGameOwner()) return;
            if (!gameStarted || dealing || won) return;
            if (!_CanReshuffle())
            {
                Debug.Log("Solitaire: Reshuffle is only available before any card has moved.");
                return;
            }

            // Nothing was played, so the fee already taken for this game stands as
            // payment for the fresh one: no wallet movement, no second charge.
            ReshuffleDeal(Networking.LocalPlayer);
        }

        // Whether the running game can still be restarted for free. The same bar as
        // the entry-fee refund - nothing moved and no stake riding on it - but a
        // fee is not required, so a free table may reshuffle too. The local-owner
        // test lives in the caller, since cardsMoved is only ever set on the
        // dealer's own client.
        private bool _CanReshuffle()
        {
            if (cardsMoved) return false;
            if (doubleOrNothingGame) return false;
            return true;
        }

        // Whether the running game still qualifies for an entry-fee refund: no card
        // has moved since the deal and an entry fee was actually charged. A
        // double-or-nothing game never charges a fee, so it never refunds one.
        private bool _CanRefundEntryFee()
        {
            if (cardsMoved) return false;
            if (doubleOrNothingGame) return false;
            if (EntryFee <= 0f) return false;
            if (UdonChips == null) return false;
            return true;
        }

        // Fires once on the dealer's first card move (or stock draw), so the
        // start/quit label drops its refund marker the moment the game is actually
        // played. Only the dealer moves cards, so this is never reached elsewhere.
        private void _MarkCardsMoved()
        {
            if (cardsMoved) return;
            cardsMoved = true;
            RefreshStartLabel();
        }

        // Rewrites the confirmation dialog's message: while the game can still be
        // refunded it asks "Refund the game?", otherwise the usual quit confirmation.
        private void _RefreshConfirmDialogText()
        {
            if (ConfirmDialogLabel == null) return;
            ConfirmDialogLabel.SetPrefixKeyAndSuffix("", _CanRefundEntryFee() ? "REFUND_CONFIRM" : "QUIT_CONFIRM", "");
        }

        public float _GetEntryFee()
        {
            return EntryFee;
        }

        private void ShowNotification(string key)
        {
            if (Notification != null) Notification.Notify(_Text(key));
        }

        // Same, but appends an untranslated suffix (a chip amount) to the localized
        // message. Udon has no string formatting, so amounts ride as raw suffixes the
        // same way the win banner's " 200c" does.
        private void ShowNotificationSuffix(string key, string suffix)
        {
            if (Notification != null) Notification.Notify(_Text(key) + suffix);
        }

        private string _Text(string key)
        {
            if (localization == null) return key;
            return localization.GetText(key);
        }

        // Plays a short card sound on every client through the shared pool, at
        // the card's world position so it is spatialised where it landed. The
        // index addresses a clip in the pool's Clips list, which is the single
        // source of truth for which sound is which.
        private void PlayCardAudio(int clipIndex, CardLogic card)
        {
            if (CardAudio == null || card == null) return;
            Transform root = card.CardRoot != null ? card.CardRoot : card.transform;
            if (root == null) return;
            CardAudio.PlayNetworkedIndex(clipIndex, root.position);
        }

        private bool TryPayEntryFee()
        {
            if (UdonChips == null) return true;
            float fee = _GetEntryFee();
            if (fee <= 0f) return true;
            if (UdonChips.money < fee) return false;
            UdonChips.money -= fee;
            if (CardAudio != null) CardAudio.PlayLocalIndex(PayClipIndex, transform.position);
            return true;
        }

        // Accrues the per-card payout and credits it straight into the wallet. It is
        // credited as it is earned, not at the finish: UdonChips only saves money to
        // PlayerData on its own schedule, and a player who leaves mid-game never gets
        // that schedule - so a payout held until the end was lost the moment the
        // dealer's client disconnected. The win bonus still lands at the finish (see
        // CreditPayout), where payoutEarned has been multiplied past what is credited.
        private void AccumulateFoundationReward(int cardCount)
        {
            if (FoundationPayout <= 0f) return;
            // Double-or-nothing is a pure bet on the staked amount: cards reaching a
            // foundation accrue nothing, so the win is exactly twice the stake.
            if (doubleOrNothingGame) return;
            float reward = FoundationPayout * cardCount;
            payoutEarned += reward;
            if (UdonChips != null)
            {
                UdonChips.money += reward;
                payoutCredited += reward;
            }
        }

        // Pays out whatever payoutEarned has that the wallet has not yet received, and
        // returns the full accrued amount so the caller can report it. In practice that
        // gap is only ever the win bonus: the per-card payout was already credited as
        // it was earned, and CheckWon multiplies payoutEarned before calling here, so
        // the difference is exactly the bonus portion.
        private float CreditPayout()
        {
            float amount = payoutEarned;
            float delta = amount - payoutCredited;
            if (UdonChips != null && delta > 0f) UdonChips.money += delta;
            payoutCredited = amount;
            return amount;
        }

        private string BuildPayoutAmount()
        {
            if (Mathf.Approximately(payoutEarned, Mathf.Floor(payoutEarned)))
            {
                int amount = (int)payoutEarned;
                return $"{amount}";
            }
            return $"{payoutEarned}";
        }

        public void Deal(VRCPlayerApi owner = null)
        {
            StartDeal(owner, false);
        }

        // Re-deal the game a player is already in, in place. Used by reshuffle: the
        // table is already theirs and its fee is already paid, so the "someone else
        // is playing / you already have a game / can you pay" guards are skipped and
        // the synced dealer id is left untouched. That last part is what keeps the
        // pivot on the angle the player chose - the table never goes idle, so
        // neither it nor any spectator sees the game close and reopen.
        private void ReshuffleDeal(VRCPlayerApi owner)
        {
            StartDeal(owner, true);
        }

        private void StartDeal(VRCPlayerApi owner, bool inPlace)
        {
            if (dealing)
            {
                Debug.Log("Solitaire: Already dealing cards.");
                ShowNotification("NOTIFY_ALREADY_DEALING");
                return;
            }

            if (!inPlace)
            {
                // Backstop for the start collider: a synced dealer id that is not ours
                // means someone else's game is running at this table, so there is
                // nothing for a local press to do. (The local gameStarted flag is only
                // true on the dealer's client, so it cannot answer this on its own.)
                if (syncedDealerId != -1)
                {
                    Debug.Log("Solitaire: Another player's game is already running at this table.");
                    ShowNotification("NOTIFY_SOMEONE_ELSES_GAME");
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
                    ShowNotification("NOTIFY_ALREADY_HAVE_GAME");
                    return;
                }

                // A double-or-nothing game stakes the previous win instead of charging a
                // fresh entry fee - the winnings already in the pot are the buy-in.
                if (!doubleOrNothingGame && !TryPayEntryFee())
                {
                    Debug.Log($"Solitaire: Not enough chips to start a {Mode} game (fee {_GetEntryFee()}).");
                    ShowNotificationSuffix("NOTIFY_NOT_ENOUGH_CHIPS", BuildEntryFeeAmount() + "c.");
                    return;
                }
            }

            // Tear down any game this player already won at another table before
            // dealing here: a finished table left uncleared would otherwise stay
            // locked to them with a dead board on it.
            ClearFinishedGames(owner);

            // Re-resolve the deck and rebuild the card/slot registry on every deal:
            // the deck lives as a per-player PlayerObject copy, so whichever player
            // is dealing owns a different deck with different card objects. Re-init
            // picks that up instead of reusing the previous player's snapshot.
            Init(owner);

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
            syncedDealerId = owner.playerId;
            lastDealerId = syncedDealerId;
            // Announce the dealer to every other client: this is what tells
            // spectators and late joiners whose deck PlayerObject to build their
            // slot registry against. The table was just transferred to the dealer,
            // so the write is an owner write and will actually travel.
            RequestSerialization();
            resolvedDeck._SetGameWon(false);
            resolvedDeck._SetGameOwner(owner.playerId);
            // DeckManager owns this move because it knows where its pool is - the
            // undealt cards live under the pool, which may not be the deck itself.
            resolvedDeck._MoveTo(CardHome);
            dealing = true;
            gameStarted = true;
            won = false;
            cardsMoved = false;
            payoutEarned = 0f;
            payoutCredited = 0f;
            RecordGameStarted();
            RefreshStartLabel();
            _RefreshStartInteractable();
            RefreshWinMessage();
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

        // Walks the deal forward in row-major order: every column gets its first
        // card, then every column that wants a second, and so on. The dealer never
        // has to special-case a null slot or a zero count. Terminates because
        // dealDepth only ever climbs toward the deepest column's count.
        private bool AdvanceToNextDealCard()
        {
            int columns = TableauSlots != null ? TableauSlots.Length : 0;
            int maxRows = 0;
            for (int c = 0; c < columns; c++)
            {
                int want = DealCountFor(c);
                if (want > maxRows) maxRows = want;
            }
            while (dealDepth < maxRows)
            {
                while (dealCol < columns)
                {
                    if (DealCountFor(dealCol) > dealDepth) return true;
                    dealCol++;
                }
                dealCol = 0;
                dealDepth++;
            }
            return false;
        }

        // Deals exactly one card per call, then schedules the next after DealDelay.
        // The tableau is dealt row by row, so the columns fill up together rather
        // than one column completing before the next begins.
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

            if (!AdvanceToNextDealCard())
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
                // Only the card that ends up on top of the column is face-up, which is
                // the rule in every mode - Canfield deals one card per column, so
                // there it means the whole tableau comes up face-up.
                card._SlideFromWorld(cardGO.transform.position, slot._GetTopSlot(), dealDepth == DealCountFor(dealCol) - 1, DealDuration);
                PlayCardAudio(DealClipIndex, card);
            }
            dealCol++;

            if (AdvanceToNextDealCard())
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
                card._SlideFromWorld(cardGO.transform.position, ReserveSlot._GetTopSlot(), dealDepth == want - 1, DealDuration);
                PlayCardAudio(DealClipIndex, card);
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
                    card._SlideFromWorld(cardGO.transform.position, foundation._GetTopSlot(), true, DealDuration);
                    PlayCardAudio(DealClipIndex, card);
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

            _MarkCardsMoved();

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
                ShowNotification("NOTIFY_STOCK_EMPTY");
                return;
            }

            int empty = FindEmptyTableauColumn();
            if (empty >= 0)
            {
                Debug.Log($"Solitaire: Column {empty} is empty - every column has to be filled before dealing another row from the stock.");
                ShowNotification("NOTIFY_FILL_COLUMNS");
                return;
            }

            dealing = true;
            dealPhase = DealPhaseStockRow;
            dealCol = 0;
            dealDepth = 0;
            dealOwner = local;
            // Shut the stock before the first card of the row leaves it; a second
            // click during the row is refused anyway, so stop offering one.
            RefreshDeckInteractable();
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

        // Walks dealCol to the next column that actually exists. A stock row wants
        // exactly one card per column rather than a per-column count, so it keeps
        // its own walker instead of sharing the row-major opening-deal one.
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
                // Stock cards always land face-up in Spider - that is the whole cost
                // of the row, and why burying an empty column matters.
                card._SlideFromWorld(cardGO.transform.position, slot._GetTopSlot(), true, DealDuration);
                PlayCardAudio(DealClipIndex, card);
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
            RefreshDeckInteractable();
            Debug.Log("Solitaire: Stock row abandoned; the deck or game owner went away mid-row.");
            // Cards placed before the bail-out were left ungrabbable by the
            // dealing gate; they are legal where they sit, so hand the flags
            // back.
            RefreshAllPickupable();
            _FlushBoard();
        }

        // Unlike FinalizeDeal this must not touch `won` or the win message: a row can
        // be dealt long after the game was won, and clearing it would retract a win
        // the player already earned.
        private void FinalizeStockRow()
        {
            dealing = false;
            dealPhase = DealPhaseNone;
            RefreshDeckInteractable();
            Debug.Log($"Solitaire: Dealt a stock row; {resolvedDeck.CardCount} cards left in the stock.");
            // A stock card lands on top of its column, which is the low end of a run -
            // so it is exactly the card that can complete one.
            CollectCompletedRuns();
            _RelayoutFannedPiles();
            // A dealt row buries the card under it in every column, so most of the
            // table's grabbability just changed.
            RefreshAllPickupable();
            CheckWon();
            _FlushBoard();
        }

        // Send the whole waste pile back to the stock, face down.
        public void _RecycleWaste()
        {
            if (resolvedDeck == null || WasteSlot == null || dealing) return;
            if (!_IsLocalGameOwner()) return;

            int count = WasteSlot._GetCardCount();
            if (count <= 0)
            {
                Debug.Log("Solitaire: Stock and waste are both empty, nothing to recycle.");
                ShowNotification("NOTIFY_NO_CARDS_TO_RECYCLE");
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
                card._Detach(cardHome);
                resolvedDeck._ReturnCard(card.gameObject);
            }

            Debug.Log($"Solitaire: Recycled {count} cards from the waste back into the stock.");
        }

        // Draw from the stock onto the waste pile - one card in Klondike, three in
        // Canfield.
        //
        // Only the first card goes out now; the rest ride the same throttled loop as
        // the deal, because three cards in one frame is three pool spawns and three
        // serializations - the exact burst DealDelay exists to spread out. It also
        // means the draw is over in under half a second, so blocking pickups for its
        // duration costs the player nothing.
        public void _DrawFromStock()
        {
            if (resolvedDeck == null || WasteSlot == null || dealing) return;
            if (!_IsLocalGameOwner()) return;

            if (!DrawOneToWaste()) return;

            int remaining = ResolveDrawCount() - 1;
            if (remaining <= 0 || resolvedDeck._IsStockEmpty())
            {
                _RelayoutFannedPiles();
                // The waste's old top card just stopped being the top one, and a
                // TopOnly waste hands out only that card, so the whole table's
                // grabbability moved with it.
                RefreshAllPickupable();
                return;
            }

            dealing = true;
            dealPhase = DealPhaseDraw;
            drawRemaining = remaining;
            dealOwner = Networking.LocalPlayer;
            // A Canfield draw is three cards over two DealDelays - long enough for a
            // second click to land on a deck that would refuse it.
            RefreshDeckInteractable();
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
            if (!DrawOneToWaste())
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
        private bool DrawOneToWaste()
        {
            GameObject cardGO = resolvedDeck.DrawNext();
            if (cardGO == null) return false;
            CardLogic card = cardGO.GetComponentInChildren<CardLogic>(true);
            if (card == null) return false;
            card._ForcePlace(WasteSlot._GetTopSlot(), true);
            PlayCardAudio(DealClipIndex, card);
            return true;
        }

        private void FinalizeDraw()
        {
            dealing = false;
            dealPhase = DealPhaseNone;
            drawRemaining = 0;
            RefreshDeckInteractable();
            _RelayoutFannedPiles();
            RefreshAllPickupable();
            _FlushBoard();
        }

        // Cards per stock click: Klondike turns one, Canfield three. Spider never gets
        // here - its stock click deals a row to the tableau instead.
        private int ResolveDrawCount()
        {
            return Mode == SolitaireMode.Canfield ? 3 : 1;
        }

        private void ResetCards()
        {
            // Anyone mid-grab loses it; the table is going away under their hand.
            _ClearAllGrabs();

            // Unlink and unparent before returning to the pool - dealt cards are
            // parented under each other, and the pool won't undo that. All 52 of
            // these used to be a serialization each, in one frame, immediately
            // before the deal wanted the wire for itself; now they're 52 writes
            // into one array that goes out once, below.
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i] != null) cards[i]._Detach(cardHome);
            }
            resolvedDeck._ResetDeck();

            // Don't leave the empty board sitting in the queue behind a deal that
            // is about to start filling it in again.
            _FlushBoard();
        }

        private void FinalizeDeal()
        {
            dealing = false;
            dealPhase = DealPhaseNone;
            won = false;
            // The deal closed the stock on its way in; the finished table reopens it.
            RefreshDeckInteractable();
            RefreshWinMessage();
            // A column that DealCounts left empty is one the reserve owes a card to.
            RefillTableauFromReserve();
            _RelayoutFannedPiles();
            // The deal placed every card without refreshing anything below them, so
            // the opening layout needs one sweep before the player can touch it.
            RefreshAllPickupable();
            // A freshly dealt, untouched table is exactly the reshuffle window, so
            // offer it now that the deal has closed.
            RefreshReshuffleInteractable();
            // Out from under the mid-deal throttle, so the finished table lands on
            // everyone else now rather than up to DealFlushInterval later.
            _FlushBoard();
        }

        // The label mirrors the interactable: hidden while another player is
        // mid-game, so a spectator sees neither the "Start" prompt nor a button
        // that they can't press anyway. Shares its state with the interact text of
        // the Start/quit trigger.
        private void RefreshStartLabel()
        {
            // The synced dealer id, not the local gameStarted flag: a spectator's
            // gameStarted is always false, and the label has to go quiet for them
            // too while someone else is playing.
            int ownerId = syncedDealerId;
            bool running = gameStarted || ownerId != -1;
            bool localOwner = false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local)) localOwner = ownerId == local.playerId;
            if (StartButtonLabel != null)
            {
                if (running && !localOwner)
                {
                    StartButtonLabel.SetPrefixKeyAndSuffix("", "", "");
                }
                else if (gameStarted)
                {
                    // The button reads "Refund" until the dealer's first move, then
                    // "Quit" once the game has actually been played.
                    StartButtonLabel.SetPrefixKeyAndSuffix("", _CanRefundEntryFee() ? "REFUND" : "QUIT", "");
                }
                else
                {
                    StartButtonLabel.SetPrefixKeyAndSuffix("", "START", "");
                }
            }
            RefreshStartPrompt();
            RefreshReshuffleInteractable();
        }

        // Builds the GameInteract hover prompt and tells it whether the press has
        // to be confirmed first. The text mirrors the button label so the two
        // never advertise different things, and it is rebuilt here rather than in
        // GameInteract so the wording (and the entry-fee suffix) stays in one place.
        //
        // Quitting a game in progress is the only press that confirms; starting a
        // game, or clearing a finished board, fires straight through.
        private void RefreshStartPrompt()
        {
            if (StartInteract == null) return;

            bool confirm = false;
            string key;
            if (gameStarted)
            {
                key = _CanRefundEntryFee() ? "REFUND" : "QUIT";
                // A won game just resets, and a pending double-or-nothing choice owns
                // the flow - neither needs a confirmation in front of it.
                bool doNothing = DoubleOrNothingDialog != null && DoubleOrNothingDialog.activeSelf;
                confirm = !won && !doNothing;
            }
            else
            {
                key = "START";
            }

            string prompt = _Text(key);
            if (!gameStarted && EntryFee > 0f) prompt += " (" + (int)EntryFee + "c)";
            StartInteract._SetInteractionText(prompt);

            if (confirm) _RefreshConfirmDialogText();
            StartInteract._SetConfirm(confirm);
        }

        // Shows the reshuffle trigger only while a fresh deal is still free: the
        // local player owns the running game, no card has moved since the deal, and
        // nothing is mid-deal or already won. Driven from every state change that
        // rebuilds the start UI. The end of the deal calls it directly, since
        // FinalizeDeal does not go through RefreshStartLabel.
        private void RefreshReshuffleInteractable()
        {
            if (ReshuffleInteract == null) return;
            bool available = gameStarted && !dealing && !won && _IsLocalGameOwner() && _CanReshuffle();
            ReshuffleInteract._SetVisible(available);
        }

        // A language switch re-reads every localized string. The button label
        // refreshes itself through LocalizedText's own registration; the hover
        // prompt is plain text Solitaire built, so it has to be rebuilt.
        public void _OnLanguageChanged()
        {
            RefreshStartPrompt();
        }

        // The start/quit button only invites an interact when no game is running
        // (anyone may deal) or the local player owns the current one (Quit). While
        // another player is mid-game the collider drops so they can't start a
        // competing deal. Runs locally on each client; the synced ownerId drives it.
        public void _RefreshStartInteractable()
        {
            int ownerId = syncedDealerId;
            bool running = gameStarted || ownerId != -1;
            bool localOwner = false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local)) localOwner = ownerId == local.playerId;
            if (StartButtonInteract != null) StartButtonInteract.enabled = !running || localOwner;
            RefreshStartLabel();

            // The pivot follows the same rule as the start button: only the player
            // whose game is running may turn it, and it returns to its default angle
            // whenever the table goes idle.
            if (changeAngle != null) changeAngle._SetGameState(running, localOwner);
        }

        // The dealer's client drives its own state straight out of Deal/_ResetGame
        // and never deserializes its own writes, so everything here is the other
        // half: a deal (or a quit) by someone else just reached this client, and
        // the slot registry has to be rebuilt against the dealer's deck before any
        // of the already-synced card links can resolve.
        public override void OnDeserialization()
        {
            if (syncedDealerId != lastDealerId)
            {
                lastDealerId = syncedDealerId;

                if (syncedDealerId == -1)
                {
                    _OnGameClosedLocal();
                    return;
                }

                // Init applies the board itself, once it has a registry to apply it
                // against - which may take a retry or two if the dealer's deck
                // PlayerObject hasn't spawned here yet.
                spectatorInitRetries = 0;
                _InitSpectator();
                return;
            }

            // Same game, new board. It arrives whole, so the table is never seen
            // part-updated the way it was when every card carried its own link.
            ApplyBoard();
        }

        // Rebuilds this client's registry for a game dealt by someone else. Runs on
        // every spectator and late joiner; the dealer's own registry was built in
        // Deal, and the dealer id on their client never deserializes.
        public void _InitSpectator()
        {
            // The registry already matches the running game - a duplicate retry
            // callback from an earlier deal racing a newer one, nothing to do.
            if (resolvedDeck != null && resolvedDeck.GameOwnerId == syncedDealerId)
            {
                _FinishSpectatorInit();
                return;
            }

            if (spectatorInitRetries >= 10) return;
            spectatorInitRetries++;

            VRCPlayerApi dealer = VRCPlayerApi.GetPlayerById(syncedDealerId);
            if (!Utilities.IsValid(dealer)) return;
            Init(dealer);

            // The deck's own synced owner id may still be in flight (it travels on a
            // different behaviour than the dealer id), and the deck PlayerObject
            // itself may not have spawned on this client yet. Either way the
            // registry would point at the wrong objects, so wait and try again
            // rather than building slot ids nothing will ever match.
            if (resolvedDeck == null || resolvedDeck.GameOwnerId != syncedDealerId)
            {
                SendCustomEventDelayedSeconds(nameof(_InitSpectator), 1f);
                return;
            }

            _FinishSpectatorInit();
        }

        private void _FinishSpectatorInit()
        {
            spectatorInitRetries = 0;

            // The same move the dealer made on their own client. The deck root has
            // no ObjectSync by design (only its synced ints travel), so every
            // client parks its own copy of the deck at the table.
            resolvedDeck._MoveTo(CardHome);

            // This client now points at the dealer's deck. Say so explicitly rather
            // than leaving whatever that copy's collider happened to be: on a
            // spectator the answer is always "off", and nothing else would have
            // told it that.
            resolvedDeck._RefreshInteractable();

            // Init re-applied every placement in pool order, so fan windows (whose
            // offsets depend on how deep the pile is) need one pass in pile order.
            _RelayoutFannedPiles();
            RefreshStartLabel();
            _RefreshStartInteractable();
            RefreshWinMessage();
        }

        // Re-parks this client's deck at CardHome after the table's pivot is
        // re-angled (a ChangeAngle interact). Cards under the pivot follow it
        // because they are parented to it, but the deck is a per-player
        // PlayerObject parked here by world transform, so turning the pivot leaves
        // it behind. _MoveTo is how it was parked at deal time and running it
        // again is how it follows. Only while a game actually holds the deck at
        // the table; once idle the deck belongs back at its template origin.
        public void _OnPivotMoved()
        {
            if (resolvedDeck == null || !_IsGameStarted()) return;
            resolvedDeck._MoveTo(CardHome);
        }

        // Local half of a quit/reset for everyone but the dealer. The authoritative
        // card returns and stock refill already came from the dealer's client (they
        // own the deck and its pool), so this only parks this client's copy of the
        // deck back at its template home and restores the idle UI. No pool writes
        // from here: a non-owner Return/Shuffle would reorder this client's pool
        // and desync every slot id derived from it.
        private void _OnGameClosedLocal()
        {
            gameStarted = false;
            dealing = false;
            dealPhase = DealPhaseNone;
            won = false;
            ClearBoard();
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
            if (Utilities.IsValid(resolvedDeck))
            {
                resolvedDeck._ResetPosition();
                // Last chance to close this deck's collider while we still hold a
                // reference to it.
                resolvedDeck._RefreshInteractable();
            }
            resolvedDeck = null;
            RefreshStartLabel();
            _RefreshStartInteractable();
            RefreshWinMessage();
        }

        // Tear the running game back down to the pre-deal state: every card back in
        // the pool, no game owner, nobody may interact until someone deals anew.
        public void _ResetGame()
        {
            if (resolvedDeck == null || cards == null) return;

            // Tell the rest of the instance the table is free again before the
            // cards go back; the dealer owns the table at this point, so the write
            // travels. Spectators react in OnDeserialization -> _OnGameClosedLocal.
            syncedDealerId = -1;
            lastDealerId = -1;
            RequestSerialization();

            ResetCards();
            resolvedDeck._ResetPosition();
            resolvedDeck._SetGameWon(false);
            resolvedDeck._SetGameOwner(-1);

            gameStarted = false;
            dealing = false;
            dealPhase = DealPhaseNone;
            won = false;
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
            if (DoubleOrNothingDialog != null) DoubleOrNothingDialog.SetActive(false);
            RefreshStartLabel();
            _RefreshStartInteractable();
            resolvedDeck._RefreshInteractable();
            RefreshWinMessage();
            Debug.Log("Solitaire: Game reset to initial state.");
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player == null) return;
            // Only the dealer leaving matters; every other departure leaves the
            // table exactly as it was.
            if (syncedDealerId == -1 || syncedDealerId != player.playerId) return;

            // The payout was already credited into the wallet as it was earned
            // (AccumulateFoundationReward), so there is nothing left to settle here:
            // UdonChips keeps saving money to PlayerData on its own cadence, and by
            // the time this event fires for the leaver it is far too late to persist
            // anything else anyway.

            // The leaver's deck PlayerObjects die with them, so this client's
            // registry points at dead objects. Drop it and restore the idle UI; the
            // cards themselves need nothing - whatever the dealer last synced is
            // gone along with its owner.
            gameStarted = false;
            dealing = false;
            dealPhase = DealPhaseNone;
            won = false;
            resolvedDeck = null;
            // Their cards died with them, so the board describes nothing. Leaving it
            // would have the next deal briefly lay this game back out over whoever
            // deals next, using their deck.
            ClearBoard();
            if (ConfirmDialog != null) ConfirmDialog.SetActive(false);
            RefreshStartLabel();
            _RefreshStartInteractable();
            RefreshWinMessage();

            // The id still reads the departed dealer here, so refreshing the UI now
            // would just re-derive "locked". Re-memoize the id we already see (so the
            // eventual -1 still registers as a change on OnDeserialization) and defer
            // the rest: ownership of the table lands on a remaining player on its own
            // schedule, and that client is the one who has to clear the ghost id and
            // follow it up with a UI refresh. The transfer may not have settled by the
            // time this callback runs, so the decision is made a few frames later.
            lastDealerId = syncedDealerId;
            SendCustomEventDelayedFrames(nameof(_ReclaimGhostTable), 5);
        }

        // The table's ownership moves on a schedule this code cannot see, so both
        // OnPlayerLeft and OnOwnershipTransferred funnel into here rather than try to
        // resolve the owner inline. Runs on the new owner once the transfer has landed:
        // if the synced dealer id no longer resolves to anyone in the instance it is a
        // ghost, and clearing it frees the table. The UI refresh is part of the same
        // step - a client that clears the id locally never deserializes its own write,
        // so without it the start button would stay stuck on "locked".
        public void _ReclaimGhostTable()
        {
            if (!_IsLocalTableOwner()) return;
            if (syncedDealerId == -1) return;

            VRCPlayerApi dealer = VRCPlayerApi.GetPlayerById(syncedDealerId);
            if (Utilities.IsValid(dealer)) return; // a new game was dealt meanwhile

            syncedDealerId = -1;
            lastDealerId = -1;
            RequestSerialization();
            RefreshStartLabel();
            _RefreshStartInteractable();
            RefreshWinMessage();
        }

        // Fires on every client when the table changes hands. On the new owner this
        // nudges the ghost cleanup a frame out, so the serialization does not race the
        // transfer and get dropped; on everyone else it is a no-op.
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            if (syncedDealerId == -1) return;
            SendCustomEventDelayedFrames(nameof(_ReclaimGhostTable), 1);
        }

        public void _OnCardPickup(CardLogic card)
        {
            if (card == null) return;

            // Backstop for the pickupable gate: even if a grab slips through, only
            // the player who started the game may handle cards.
            if (!_IsLocalGameOwner() || dealing)
            {
                card._Reject();
                if (!_IsLocalGameOwner()) ShowNotification("NOTIFY_NOT_YOUR_GAME");
                return;
            }

            CardSlot below = card.PrevSlot;
            if (below == null) return; // loose card, no pile rules apply

            // _RefreshPickupable should already have blocked this; it's a backstop
            // for the window between a card being uncovered and being turned over.
            if (!card.FaceUp)
            {
                card._Reject();
                ShowNotification("NOTIFY_CARD_FACE_DOWN");
                return;
            }

            // Same backstop for the base slot's pick-up policy (top-only / none).
            CardPickupMode pickupMode = below._GetPickupMode();
            if (pickupMode == CardPickupMode.None
                || (pickupMode == CardPickupMode.TopOnly && below._GetTopCard() != card))
            {
                card._Reject();
                ShowNotification("NOTIFY_TOP_CARD_ONLY");
                return;
            }

            // Foundations only ever give up their top card.
            if (IsFoundationChain(below) && card.Slot != null && card.Slot._IsOccupied())
            {
                card._Reject();
                ShowNotification("NOTIFY_TOP_CARD_FROM_FOUNDATION");
                return;
            }

            // Spider only lets a group move when it is a same-suit descending run.
            // This cannot live in a SlotRule, which never sees the carried cards.
            // _RefreshPickupable should already have blocked the grab; this stays as
            // the backstop for the window before the sweep has run.
            if (!_IsGroupMovable(card))
            {
                card._Reject();
                ShowNotification("NOTIFY_GROUP_SAME_SUIT");
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
            if (target != null)
            {
                // Only a genuine relocation counts as a move: dropping back onto the
                // same slot (or a snap-back with no valid target) changes nothing.
                if (target.SlotId != card._GetSavedPrevSlotId()) _MarkCardsMoved();
                card._SetPrevSlot(target);
                PlayCardAudio(MoveClipIndex, card);
                if (IsFoundationChain(target))
                {
                    AccumulateFoundationReward(1);
                    RefreshWinMessage();
                }
            }
            else card._SnapBack(); // no valid home, snap back where it was

            // The cards it was carrying stay parented to it, but their offset comes
            // from the pile they're now in, so re-derive it.
            _RepositionAbove(card);
            RevealTops();
            CollectCompletedRuns();
            RefillTableauFromReserve();
            _RelayoutFannedPiles();
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

        // The deck's own gate on `dealing` lives in its collider, so it only answers
        // the question when something asks. Every flip of the flag is paired with
        // this, the same way a move is paired with RefreshAllPickupable.
        private void RefreshDeckInteractable()
        {
            if (Utilities.IsValid(resolvedDeck)) resolvedDeck._RefreshInteractable();
        }

        private void RevealTops()
        {
            if (TableauSlots != null)
            {
                for (int s = 0; s < TableauSlots.Length; s++)
                {
                    CardSlot slot = TableauSlots[s];
                    if (slot == null) continue;
                    RevealTop(slot._GetTopCard());
                }
            }

            // The reserve is dealt face-down under a single turned-over card, so
            // spending that card has to expose the next one. Null outside Canfield.
            if (ReserveSlot != null)
            {
                RevealTop(ReserveSlot._GetTopCard());
            }
        }

        // Flip a pile's top card face-up, unless it is only on top because the card
        // covering it is still being held. A held card's vacated slot still belongs
        // to it, so the face-down card under it must not turn over until the held
        // card actually lands somewhere else - not even when an unrelated card drops.
        private void RevealTop(CardLogic top)
        {
            if (top == null || top.FaceUp) return;
            if (top.Slot != null && _IsSlotReservedByHeld(top.Slot.SlotId, null)) return;
            top.SetFaceUp(true);
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

            bool moved = false;
            for (int s = 0; s < TableauSlots.Length; s++)
            {
                CardSlot column = TableauSlots[s];
                if (column == null || column._IsOccupied()) continue;

                // A column a held card just vacated is not genuinely empty - filling
                // it would strand the held card when it snaps back.
                if (_IsSlotReservedByHeld(column.SlotId, null)) continue;

                CardLogic top = ReserveSlot._GetTopCard();
                if (top == null) break; // reserve spent; the rest stay open
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
        // thirteen serializations in a single frame is precisely what DealDelay
        // exists to spread out.
        private void CollectCompletedRuns()
        {
            if (Mode != SolitaireMode.Spider) return;
            if (TableauSlots == null || FoundationSlots == null) return;

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

                    king._SlideToSlot(foundation._GetTopSlot(), true, RunCollectDuration);
                    _RepositionAbove(king);
                    PlayCardAudio(RunCompleteClipIndex, king);
                    AccumulateFoundationReward(CardLogic.RankDefinitionsCount);
                    RefreshWinMessage();
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

        // The banner under the start button (WinMessage) doubles as the idle
        // prompt: before any game it says "200c to start" instead of floating
        // dead over the table. Once won it shows the win message, and the whole
        // banner hides while a game is dealt - the running table is thanks enough,
        // and showing the fee mid-game would read as a double charge.
        private void RefreshWinMessage()
        {
            if (WinMessage == null) return;

            if (won)
            {
                if (winMessageText != null)
                {
                    winMessageText.SetPrefixKeyAndSuffix("", "YOU_WON", " " + BuildPayoutAmount() + "c");
                }
                WinMessage.SetActive(true);
                return;
            }

            int ownerId = syncedDealerId;
            bool localOwner = false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local)) localOwner = ownerId == local.playerId;
            if (ownerId != -1 && !localOwner)
            {
                WinMessage.SetActive(false);
                return;
            }

            if (gameStarted)
            {
                if (doubleOrNothingGame)
                {
                    if (winMessageText != null) winMessageText.SetPrefixKeyAndSuffix("", "DOUBLE_OR_NOTHING_SHORT", " " + BuildDoubleStakeAmount() + "c");
                    WinMessage.SetActive(true);
                }
                else if (payoutEarned > 0f)
                {
                    if (winMessageText != null) winMessageText.SetPrefixKeyAndSuffix("", "PAYOUT", " " + BuildPayoutAmount() + "c");
                    WinMessage.SetActive(true);
                }
                else
                {
                    WinMessage.SetActive(false);
                }
                return;
            }

            if (EntryFee > 0f)
            {
                if (winMessageText != null) winMessageText.SetPrefixKeyAndSuffix(BuildEntryFeeAmount() + "c", "COINS_TO_START", "");
                WinMessage.SetActive(true);
            }
            else
            {
                WinMessage.SetActive(false);
            }
        }

        private string BuildEntryFeeAmount()
        {
            if (Mathf.Approximately(EntryFee, Mathf.Floor(EntryFee)))
            {
                int fee = (int)EntryFee;
                return $"{fee}";
            }
            return $"{EntryFee}";
        }

        private string BuildDoubleStakeAmount()
        {
            if (Mathf.Approximately(doubleStake, Mathf.Floor(doubleStake)))
            {
                int stake = (int)doubleStake;
                return $"{stake}";
            }
            return $"{doubleStake}";
        }

        // ---- Records
        //
        // Lifetime games/wins for the local player, tallied per mode in PlayerData so
        // they survive across sessions. Only the dealer's client ever runs Deal and
        // CheckWon, so the local player here is always the one whose record to bump.

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.IsValid() || !player.isLocal) return;
            playerDataReady = true;
        }

        private void RecordGameStarted()
        {
            IncrementRecord(GamesKeyPrefix);
        }

        // Returns the streak after this win, so CheckWon can size the payout bonus
        // off it without having to read PlayerData back a second time.
        private int RecordGameWon()
        {
            IncrementRecord(WinsKeyPrefix);
            return IncrementWinStreak();
        }

        // A game that ended without a win - the player quit it part-way - breaks the
        // run, so the streak goes back to zero rather than being left to carry over.
        private void RecordGameLost()
        {
            SaveWinStreak(0);
        }

        private int IncrementWinStreak()
        {
            if (!playerDataReady) return 1;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return 1;

            string key = ModeRecordKey(WinStreakKeyPrefix);
            int streak = 0;
            if (PlayerData.TryGetInt(local, key, out int saved)) streak = saved;
            int next = streak + 1;
            SaveWinStreak(next);
            return next;
        }

        private void SaveWinStreak(int streak)
        {
            if (!playerDataReady) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            PlayerData.SetInt(ModeRecordKey(WinStreakKeyPrefix), streak);
        }

        // Bonus added to CompletePayoutMultiplier on a win, sized by how long the
        // current streak already is: the first win pays no streak bonus, then it
        // climbs by 0.25x, 0.5x and 1x for the second, third and fourth, and
        // plateaus at 3x from the fifth onward. `streak` is the win count including
        // the one just recorded, so a first-ever win (streak 1) gets nothing and a
        // second in a row (streak 2) gets the 0.25x step.
        private float _StreakWinMultiplier(int streak)
        {
            if (streak <= 1) return 0f;
            if (streak == 2) return 0.25f;
            if (streak == 3) return 0.5f;
            if (streak == 4) return 1f;
            return 3f;
        }

        private void IncrementRecord(string prefix)
        {
            if (!playerDataReady) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            string key = ModeRecordKey(prefix);
            int count = 0;
            if (PlayerData.TryGetInt(local, key, out int saved)) count = saved;
            PlayerData.SetInt(key, count + 1);
        }

        private string ModeRecordKey(string prefix)
        {
            if (Mode == SolitaireMode.Spider) return prefix + "Spider";
            if (Mode == SolitaireMode.Canfield) return prefix + "Canfield";
            return prefix + "Klondike";
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
            int streak = RecordGameWon();
            if (CardAudio != null) CardAudio.PlayNetworkedIndex(WinClipIndex, transform.position);
            // Double-or-nothing is a pure bet: the win pays exactly twice the staked
            // amount, with no streak multiplier and no foundation accrual folded in.
            if (doubleOrNothingGame)
            {
                payoutEarned = 2f * doubleStake;
            }
            else
            {
                // The streak bonus rides on top of the base win multiplier rather than
                // replacing it. A base of zero (or less) means "no bonus" and reads as
                // 1x, so it never cancels the payout; the streak step is then added.
                float multiplier = CompletePayoutMultiplier > 0f ? CompletePayoutMultiplier : 1f;
                multiplier += _StreakWinMultiplier(streak);
                payoutEarned *= multiplier;
            }
            if (resolvedDeck != null) resolvedDeck._SetGameWon(true);

            // No double-or-nothing UI means the old immediate payout: credit now and
            // let the win banner report it. Otherwise defer the credit and offer the
            // choice - collect, or stake the lot on another game.
            if (DoubleOrNothingDialog != null)
            {
                ShowDoubleOrNothing();
            }
            else
            {
                CreditPayout();
                RefreshWinMessage();
            }
            _RefreshStartInteractable();
        }

        // Puts the amount on offer on the double-or-nothing dialog and shows it in
        // place of the win banner, so the decision owns the screen until made.
        private void ShowDoubleOrNothing()
        {
            if (DoubleOrNothingAmountLabel != null)
            {
                DoubleOrNothingAmountLabel.SetPrefixKeyAndSuffix("", "DOUBLE_OR_NOTHING", " " + BuildPayoutAmount() + "c");
            }
            if (DoubleOrNothingDialog != null) DoubleOrNothingDialog.SetActive(true);
            if (WinMessage != null) WinMessage.SetActive(false);
        }

        // Collect the current winnings and finish the game, paying out whatever
        // payoutEarned still has that the wallet has not received. In a normal win
        // that is the win bonus; in a double-or-nothing win it is the whole pot.
        public void _OnDoubleOrNothingCollect()
        {
            if (!_IsLocalGameOwner()) return;
            if (DoubleOrNothingDialog != null) DoubleOrNothingDialog.SetActive(false);
            CreditPayout();
            doubleStake = 0f;
            doubleOrNothingGame = false;
            RefreshWinMessage();
        }

        // Stake the winnings on another game. Any part already credited into the
        // wallet is clawed back so the full amount is at risk, then a fresh game is
        // dealt in double-or-nothing mode.
        public void _OnDoubleOrNothingDouble()
        {
            if (!_IsLocalGameOwner()) return;

            // Re-staking claws the already-credited foundation payout back out of the
            // wallet. If the player spent that part on another game while the dialog
            // was up, the wallet no longer covers the claw-back, so refuse the double
            // and collect what is left instead.
            if (UdonChips != null && UdonChips.money < payoutCredited)
            {
                ShowNotificationSuffix("NOTIFY_NOT_ENOUGH_CHIPS", BuildPayoutAmount() + "c.");
                _OnDoubleOrNothingCollect();
                return;
            }

            if (DoubleOrNothingDialog != null) DoubleOrNothingDialog.SetActive(false);

            doubleStake = payoutEarned;
            if (UdonChips != null && payoutCredited > 0f) UdonChips.money -= payoutCredited;
            payoutCredited = 0f;
            payoutEarned = 0f;

            _ResetGame();
            doubleOrNothingGame = true;
            Deal(Networking.LocalPlayer);
        }
    }
}
