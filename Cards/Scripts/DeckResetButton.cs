
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DeckResetButton : UdonSharpBehaviour
    {

        public DeckManager DeckOfCards;
        
        private Animator anim;
        private int hashTrigger;


        private void Start()
        {
            anim = GetComponent<Animator>();
            hashTrigger = Animator.StringToHash("Trigger");
        }

        public override void Interact()
        {
            if (DeckOfCards == null || DeckOfCards.Solitaire == null) return;
            if (!DeckOfCards.Solitaire._IsGameStarted() || !DeckOfCards.Solitaire._IsLocalGameOwner()) return;
            DeckOfCards._ResetDeck();
            anim.SetTrigger(hashTrigger);
        }
    }
}