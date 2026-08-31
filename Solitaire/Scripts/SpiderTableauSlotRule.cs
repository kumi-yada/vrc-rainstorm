using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    // Spider's tableau rule: build down by rank, ignoring suit, and an empty column
    // takes anything.
    //
    // Note what is deliberately *not* here. Spider only lets you move a group of
    // cards when it is a same-suit descending run, but that is a property of the
    // cards riding on the dragged one, and a SlotRule only ever sees the card being
    // placed and the one it lands on. So the group check lives in
    // Solitaire._OnCardPickup instead, which can walk the chain upward. Placement
    // and movability are genuinely two different rules in this game - unlike
    // Klondike, where the tableau rule is invariant and so a legal pile above the
    // dragged card is automatically a legal continuation.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class SpiderTableauSlotRule : SlotRule
    {
        public override bool AllowedToPlace(CardLogic cardBelow, CardLogic card)
        {
            if (card == null || card.IsJoker) return false;
            // Any rank opens an empty column, which is what makes an empty column the
            // scarce resource in Spider rather than a place to park a king.
            if (cardBelow == null) return true;
            if (cardBelow.IsJoker) return false;
            return (int)cardBelow.CardRank == (int)card.CardRank + 1;
        }
    }
}
