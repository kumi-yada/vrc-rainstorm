using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    // Canfield's tableau rule: build down by rank in alternating colours, wrapping
    // past the ace so a king lands on an ace. Any card may be placed in an empty
    // column.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class CanfieldTableauSlotRule : SlotRule
    {
        public override bool AllowedToPlace(CardLogic cardBelow, CardLogic card)
        {
            if (card == null || card.IsJoker) return false;

            if (cardBelow == null) return true;
            if (cardBelow.IsJoker) return false;

            return SuitIsRed(card) != SuitIsRed(cardBelow)
                && (int)cardBelow.CardRank == (int)card.CardRank % CardLogic.RankDefinitionsCount + 1;
        }

        private bool SuitIsRed(CardLogic card)
        {
            int suit = (int)card.CardSuit;
            return suit == 1 || suit == 2;
        }
    }
}
