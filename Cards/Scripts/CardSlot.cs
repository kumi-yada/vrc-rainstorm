using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace org.kumagee
{
    // A single landing spot for one card. Two flavours share this component:
    //
    //   * Base slots sit in the scene (tableau columns, foundations, waste). They
    //     anchor a pile, carry its layout and its SlotRule, and have a null Owner.
    //   * Card slots ride along on every card and represent the spot directly on
    //     top of that card. They inherit layout and rule from the base slot at the
    //     bottom of their chain.
    //
    // A pile is a linked list built upward: every card points its PrevSlot at the
    // slot it landed in, so the chain from any card walks down to a base slot.
    // Nothing here is synced - CardLogic syncs the link and everyone rebuilds the
    // same structure from it.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class CardSlot : UdonSharpBehaviour
    {
        // A pile can never exceed the deck size, so anything past this is a cycle.
        private const int ChainGuard = 64;

        [Header("Layout")]
        [Tooltip("Where the card above sits, relative to this slot. Only read off the base slot of a pile; card slots inherit it.")]
        public Vector3 Offset = new Vector3(0f, 0.002f, 0f);

        [Tooltip("Whether the first card placed directly on this slot is offset. Turn off to have it sit flush on the anchor, so the fan starts from the second card. Only meaningful on a base slot.")]
        public bool InitialOffset = true;

        [Tooltip("Snap the card's rotation to this slot's when it lands.")]
        public bool AlignRotation = true;

        [Header("Runtime")]
        [Tooltip("Network identity for this slot, assigned by Solitaire at startup. This is what cards actually sync.")]
        [HideInInspector] public int SlotId = -1;

        [Tooltip("Assigned by Solitaire at startup.")]
        [HideInInspector] public Solitaire Solitaire;

        [Tooltip("The card this slot sits on top of. Null on the base slots placed in the scene.")]
        [HideInInspector] public CardLogic Owner;

        private SlotRule rule;
        private bool ruleResolved;

        public bool IsBaseSlot => Owner == null;

        // The base slot at the bottom of this slot's chain, or null when the chain
        // doesn't reach one - which means the card carrying this slot is loose.
        public CardSlot _GetRootSlot()
        {
            CardSlot current = this;
            int guard = 0;
            while (current != null && guard < ChainGuard)
            {
                if (current.Owner == null) return current;
                current = current.Owner.PrevSlot;
                guard++;
            }
            return null;
        }

        private SlotRule ResolveRule()
        {
            if (!ruleResolved)
            {
                ruleResolved = true;
                rule = GetComponent<SlotRule>();
            }
            return rule;
        }

        public SlotRule _GetRule()
        {
            CardSlot root = _GetRootSlot();
            if (root == null) return null;
            return root.ResolveRule();
        }

        // Layout comes from the pile's base slot, so a card fans the same way no
        // matter which pile it was dealt into.
        public Vector3 _GetOffsetForNext()
        {
            CardSlot root = _GetRootSlot();
            if (root == null) root = this;
            // A base slot marks where its own first card sits, so that card can land
            // flush on the anchor and let the fan start from the one above it.
            if (IsBaseSlot && !InitialOffset) return Vector3.zero;
            return root.Offset;
        }

        public bool _GetAlignRotation()
        {
            CardSlot root = _GetRootSlot();
            if (root == null) return AlignRotation;
            return root.AlignRotation;
        }

        public CardLogic _GetCardAbove()
        {
            if (Solitaire == null) return null;
            return Solitaire._GetCardOn(this);
        }

        public bool _IsOccupied()
        {
            return _GetCardAbove() != null;
        }

        // Highest free slot in this pile - where the next card would land.
        public CardSlot _GetTopSlot()
        {
            CardSlot current = this;
            int guard = 0;
            while (guard < ChainGuard)
            {
                CardLogic above = current._GetCardAbove();
                if (above == null) return current;
                if (above.Slot == null) return current;
                current = above.Slot;
                guard++;
            }
            return current;
        }

        // Topmost card of this pile, or null when the pile is empty.
        public CardLogic _GetTopCard()
        {
            return _GetTopSlot().Owner;
        }

        public int _GetCardCount()
        {
            int count = 0;
            CardSlot current = this;
            int guard = 0;
            while (guard < ChainGuard)
            {
                CardLogic above = current._GetCardAbove();
                if (above == null) break;
                count++;
                if (above.Slot == null) break;
                current = above.Slot;
                guard++;
            }
            return count;
        }

        public CardLogic _GetCardAt(int index)
        {
            if (index < 0) return null;
            CardSlot current = this;
            int guard = 0;
            while (guard < ChainGuard)
            {
                CardLogic above = current._GetCardAbove();
                if (above == null) return null;
                if (index == 0) return above;
                index--;
                if (above.Slot == null) return null;
                current = above.Slot;
                guard++;
            }
            return null;
        }

        // True when this slot sits somewhere above the given card. Placing that card
        // here would make it its own ancestor, so it has to be rejected.
        private bool DescendsFrom(CardLogic card)
        {
            CardSlot current = this;
            int guard = 0;
            while (current != null && guard < ChainGuard)
            {
                if (current.Owner == null) return false;
                if (current.Owner == card) return true;
                current = current.Owner.PrevSlot;
                guard++;
            }
            return false;
        }

        public const int AcceptOk = 0;
        public const int RejectNoCard = 1;
        public const int RejectOwnSlot = 2;
        public const int RejectAboveDragged = 3;
        public const int RejectOccupied = 4;
        public const int RejectNoPile = 5;
        public const int RejectRule = 6;

        // Single source of truth for placement; _CanAccept is just the boolean view
        // and the code is what the drop diagnostics report.
        public int _CheckAccept(CardLogic card)
        {
            if (card == null) return RejectNoCard;
            // Putting a card back exactly where it already was is always legal. It
            // also has to short-circuit the occupancy test below, which would
            // otherwise see the card itself sitting here and turn it away.
            if (card.PrevSlot == this) return AcceptOk;
            if (card.Slot == this) return RejectOwnSlot;
            if (DescendsFrom(card)) return RejectAboveDragged;
            if (_IsOccupied()) return RejectOccupied;

            // A card slot only takes cards while its own card is part of a pile.
            CardSlot root = _GetRootSlot();
            if (root == null) return RejectNoPile;

            SlotRule slotRule = root.ResolveRule();
            if (slotRule == null) return AcceptOk;
            if (!slotRule.AllowedToPlace(Owner, card)) return RejectRule;
            return AcceptOk;
        }

        public bool _CanAccept(CardLogic card)
        {
            return _CheckAccept(card) == AcceptOk;
        }

        public string _DescribeReject(int code)
        {
            if (code == AcceptOk) return "ok";
            if (code == RejectNoCard) return "no card";
            if (code == RejectOwnSlot) return "own slot";
            if (code == RejectAboveDragged) return "above dragged card";
            if (code == RejectOccupied) return "occupied";
            if (code == RejectNoPile) return "not in a pile";
            if (code == RejectRule) return "rule rejected";
            return "unknown";
        }
    }
}
