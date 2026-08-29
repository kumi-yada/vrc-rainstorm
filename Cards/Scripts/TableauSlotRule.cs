using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class TableauSlotRule : SlotRule
    {
        private const int RankKing = 13;

        public override bool AllowedToPlace(CardLogic cardBelow, CardLogic card)
        {
            if (card == null || card.IsJoker) return false;
            if (cardBelow == null) return (int)card.CardRank == RankKing;
            return SuitIsRed(card) != SuitIsRed(cardBelow)
                && (int)cardBelow.CardRank == (int)card.CardRank + 1;
        }

        private bool SuitIsRed(CardLogic card)
        {
            int suit = (int)card.CardSuit;
            return suit == 1 || suit == 2;
        }
    }
}