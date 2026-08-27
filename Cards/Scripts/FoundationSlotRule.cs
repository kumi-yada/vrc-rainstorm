using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class FoundationSlotRule : SlotRule
    {
        private const int RankAce = 1;

        public override bool AllowedToPlace(CardLogic[] cards, CardLogic card)
        {
            if (cards == null || card == null || card.IsJoker) return false;
            if (cards.Length == 0) return (int)card.CardRank == RankAce;
            CardLogic top = cards[cards.Length - 1];
            return (int)top.CardSuit == (int)card.CardSuit
                && (int)top.CardRank == (int)card.CardRank + 1;
        }
    }
}