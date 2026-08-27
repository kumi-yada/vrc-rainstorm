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

        public override bool AllowedToPlace(CardLogic[] cards, CardLogic card)
        {
            if (cards == null || card == null || card.IsJoker) return false;
            if (cards.Length == 0) return (int)card.CardRank == RankKing;
            CardLogic top = cards[cards.Length - 1];
            return SuitIsRed(card) != SuitIsRed(top)
                && (int)top.CardRank == (int)card.CardRank + 1;
        }

        private bool SuitIsRed(CardLogic card)
        {
            int suit = (int)card.CardSuit;
            return suit == 1 || suit == 2;
        }
    }
}