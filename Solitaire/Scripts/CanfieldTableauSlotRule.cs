using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    // Canfield's tableau rule: build down by rank in alternating colours, wrapping
    // past the ace so a king lands on an ace.
    //
    // The one thing here that has to look past the pair of cards a SlotRule is handed
    // goes through Solitaire: an empty column is not a free space while the reserve
    // still holds cards, because the reserve fills it automatically. Only once the
    // reserve is spent does the column become somewhere a player may put a card of
    // their choosing.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class CanfieldTableauSlotRule : SlotRule
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

            Solitaire game = ResolveSolitaire();
            if (cardBelow == null) return game == null || game._CanFillEmptyTableau();
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
