using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    // Canfield's foundation rule: build up in suit from the base rank the deal turned
    // over, wrapping past the king back to the ace.
    //
    // The base rank is not a constant like Klondike's ace, and it is not something a
    // SlotRule can see from (cardBelow, card) either, so it is read back off the
    // table through Solitaire - the bottom card of the first seeded foundation. That
    // keeps it out of the network entirely: the foundations are already synced as
    // card links, so every client derives the same base from them.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class CanfieldFoundationSlotRule : SlotRule
    {
        private Solitaire solitaire;

        // Solitaire assigns itself to the slots in Init, which does not run until the
        // first deal, so this cannot be resolved in Start.
        private Solitaire ResolveSolitaire()
        {
            if (solitaire == null)
            {
                CardSlot slot = GetComponent<CardSlot>();
                if (slot != null) solitaire = slot.Solitaire;
            }
            return solitaire;
        }

        public override bool AllowedToPlace(CardLogic cardBelow, CardLogic card)
        {
            if (card == null || card.IsJoker) return false;

            if (cardBelow == null)
            {
                Solitaire game = ResolveSolitaire();
                int baseRank = game != null ? game._GetFoundationBaseRank() : -1;
                // Nothing has seeded a base yet, which outside a half-finished deal
                // should not happen - let the card through and it becomes the base.
                if (baseRank < 1) return true;
                return (int)card.CardRank == baseRank;
            }

            if (cardBelow.IsJoker) return false;
            return (int)cardBelow.CardSuit == (int)card.CardSuit
                && (int)card.CardRank == (int)cardBelow.CardRank % CardLogic.RankDefinitionsCount + 1;
        }
    }
}
