using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
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

        [Header("Win")]
        [Tooltip("Optional object activated when all 4 foundations are complete.")]
        public GameObject WinMessage;

        private CardLogic[] cards;
        private bool dealing;
        private bool initialized;
        private bool won;

        private CardLogic[] dragGroup;
        private int dragCount;
        private CardSlot dragSource;
        private Transform[] dragParents;
        private bool[] dragPickupable;

        private void Start()
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
                Debug.Log("Solitaire: Deck pool not assigned, cannot initialize.");
                return;
            }
            int n = pool.Pool.Length;
            if (n <= 0)
            {
                Debug.Log("Solitaire: No cards in deck, cannot initialize.");
                return;
            }

            cards = new CardLogic[n];
            for (int i = 0; i < n; i++)
            {
                CardLogic logic = pool.Pool[i].GetComponentInChildren<CardLogic>();
                cards[i] = logic;
                if (logic != null)
                {
                    logic.DeckManager = DeckOfCards;
                    logic.Solitaire = this;
                }
            }

            int maxStack = CardLogic.RankDefinitionsCount * 4;
            dragParents = new Transform[maxStack];
            dragPickupable = new bool[maxStack];

            Debug.Log($"Solitaire: Initialized with {n} cards in deck.");
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

            dealing = true;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local))
            {
                Debug.Log("Solitaire: No local player, cannot deal cards.");
                dealing = false;
                return;
            }

            Debug.Log($"Solitaire: Dealing cards for {local.displayName} ({local.playerId})");
            VRCObjectPool pool = DeckOfCards.Pool;
            Networking.SetOwner(local, DeckOfCards.gameObject);
            ResetCardsAndSlots();

            int dealt = 0;
            for (int col = 0; col < TableauSlots.Length && col < 7; col++)
            {
                CardSlot slot = TableauSlots[col];
                if (slot == null) continue;
                for (int depth = 0; depth <= col; depth++)
                {
                    GameObject cardGO = DeckOfCards.DrawNext();
                    if (cardGO == null)
                    {
                        FinalizeDeal(dealt);
                        return;
                    }
                    CardLogic card = cardGO.GetComponentInChildren<CardLogic>();
                    Networking.SetOwner(local, cardGO);
                    card.Grabbed = false;
                    card.SetFaceUp(depth == col);

                    slot._ForceAdd(card);
                    dealt++;
                }
            }
            FinalizeDeal(dealt);
        }

        private void ResetCardsAndSlots()
        {
            DeckOfCards._ResetDeck();

            for (int s = 0; s < TableauSlots.Length; s++)
            {
                if (TableauSlots[s] != null) TableauSlots[s]._Clear();
            }
            for (int s = 0; s < FoundationSlots.Length; s++)
            {
                if (FoundationSlots[s] != null) FoundationSlots[s]._Clear();
            }
            if (WasteSlot != null) WasteSlot._Clear();

        }

        private void FinalizeDeal(int dealt)
        {
            dealing = false;
            won = false;
            if (WinMessage != null) WinMessage.SetActive(false);
        }

        public void _OnCardPickup(CardLogic top)
        {
            if (top == null) return;
            if (dragGroup != null) return;

            CardSlot owner = FindSlot(top);
            if (owner == null) return;
            int index = owner._GetCardIndex(top);
            if (index < 0) return;

            if (IsFoundationSlot(owner) && index < owner._GetCardCount() - 1)
            {
                RejectPickup(owner, top);
                return;
            }

            if (index < owner._GetCardCount() - 1)
            {
                CardLogic[] run = owner._GetStackFrom(top);
                if (!IsValidRun(run))
                {
                    RejectPickup(owner, top);
                    return;
                }
                BeginRunDrag(owner, run);
            }
        }

        private CardSlot FindSlot(CardLogic top)
        {
            if (TableauSlots != null)
            {
                for (int s = 0; s < TableauSlots.Length; s++)
                {
                    if (TableauSlots[s] != null && TableauSlots[s]._GetCardIndex(top) >= 0) return TableauSlots[s];
                }
            }
            if (FoundationSlots != null)
            {
                for (int s = 0; s < FoundationSlots.Length; s++)
                {
                    if (FoundationSlots[s] != null && FoundationSlots[s]._GetCardIndex(top) >= 0) return FoundationSlots[s];
                }
            }
            if (WasteSlot != null && WasteSlot._GetCardIndex(top) >= 0) return WasteSlot;
            return null;
        }

        private bool IsFoundationSlot(CardSlot slot)
        {
            if (FoundationSlots == null) return false;
            for (int s = 0; s < FoundationSlots.Length; s++)
            {
                if (FoundationSlots[s] == slot) return true;
            }
            return false;
        }

        private bool IsValidRun(CardLogic[] run)
        {
            if (run == null || run.Length < 2) return false;
            for (int i = 1; i < run.Length; i++)
            {
                CardLogic below = run[i - 1];
                CardLogic above = run[i];
                if (!above.FaceUp) return false;
                if ((int)below.CardRank != (int)above.CardRank + 1) return false;
                if (SuitIsRed(below) == SuitIsRed(above)) return false;
            }
            return true;
        }

        private bool SuitIsRed(CardLogic card)
        {
            int suit = (int)card.CardSuit;
            return suit == 1 || suit == 2;
        }

        private void RejectPickup(CardSlot owner, CardLogic top)
        {
            top._Drop();
            if (owner == null) return;
            owner._ForceAdd(top);
            owner._Repack();
        }

        private void BeginRunDrag(CardSlot source, CardLogic[] stack)
        {
            if (stack == null || stack.Length < 2) return;
            dragGroup = stack;
            dragCount = stack.Length;
            dragSource = source;

            Transform leaderRoot = stack[0].CardRoot;
            for (int i = 1; i < stack.Length; i++)
            {
                CardLogic follower = stack[i];
                Transform root = follower.CardRoot;
                dragParents[i - 1] = root.parent;
                VRCPickup pk = root.GetComponent<VRCPickup>();
                if (pk != null)
                {
                    dragPickupable[i - 1] = pk.pickupable;
                    pk.pickupable = false;
                }
                VRCObjectSync sy = root.GetComponent<VRCObjectSync>();
                if (sy != null) sy.SetKinematic(true);
                root.SetParent(leaderRoot, true);
            }
            Debug.Log($"Solitaire: Dragging run of {stack.Length} cards from slot {source.name}.");
        }

        public bool _OnCardDrop(CardLogic top)
        {
            if (top == null) return false;
            if (dragGroup == null || dragCount < 2) return false;
            if (dragGroup[0] != top) return false;

            CardLogic[] group = dragGroup;
            int count = dragCount;
            CardSlot source = dragSource;
            dragGroup = null;
            dragCount = 0;
            dragSource = null;

            for (int i = 1; i < count; i++)
            {
                CardLogic follower = group[i];
                if (follower == null) continue;
                Transform root = follower.CardRoot;
                if (dragParents[i - 1] != null) root.SetParent(dragParents[i - 1], true);
                VRCPickup pk = root.GetComponent<VRCPickup>();
                if (pk != null) pk.pickupable = dragPickupable[i - 1];
            }

            bool placed = false;
            CardSlot destination = top._GetCurrentSlot();
            if (destination != null)
            {
                placed = destination._PlaceCardStack(group);
            }
            if (!placed && source != null)
            {
                source._PlaceCardStack(group);
            }
            Debug.Log($"Solitaire: Stack drag resolved, placed={placed}.");
            return true;
        }

        public void _OnCardPlacedTableau()
        {
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject)) return;
            for (int s = 0; s < TableauSlots.Length; s++)
            {
                RevealTop(TableauSlots[s]);
            }
            _OnCardPlaced();
        }

        public void _OnCardPlacedFoundation()
        {
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject)) return;
            CheckWon();
            _OnCardPlaced();
        }

        public void _OnCardPlaced()
        {
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject)) return;
            DeckOfCards.NextCard();
        }

        private void RevealTop(CardSlot slot)
        {
            if (slot == null) return;
            CardLogic top = slot._GetTopCard();
            if (top != null && !top.FaceUp) top.SetFaceUp(true);
            slot._Repack();
        }

        private void CheckWon()
        {
            if (won || FoundationSlots == null) return;
            for (int i = 0; i < FoundationSlots.Length; i++)
            {
                if (FoundationSlots[i] == null || FoundationSlots[i]._GetCardCount() < CardLogic.RankDefinitionsCount) return;
            }
            won = true;
            if (WinMessage != null) WinMessage.SetActive(true);
        }

    }
}
