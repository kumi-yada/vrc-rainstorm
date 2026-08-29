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

        public override bool AllowedToPlace(CardLogic cardBelow, CardLogic card)
        {
            if (card == null || card.IsJoker) return false;
            if (cardBelow == null) return (int)card.CardRank == RankAce;
            // Foundations build up in suit: ace, two, three... so the incoming card
            // is one rank above the one it lands on.
            return (int)cardBelow.CardSuit == (int)card.CardSuit
                && (int)card.CardRank == (int)cardBelow.CardRank + 1;
        }
    }
}