
using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace org.kumagee
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DeckManager : UdonSharpBehaviour
    {
        
        [UdonSynced, SerializeField, FieldChangeCallback(nameof(CardCount))] private int cardCount;
        public int CardCount
        {
            get => cardCount;
            set
            {
                cardCount = value;
                
                if (cardCount <= 0)
                {
                    Deck.localScale = Vector3.zero;
                }
                else
                {
                    Deck.localScale = new Vector3(1, cardCount + 0.002f, 1);
                }
            }
        }
        [UdonSynced] public int CardCurrent;
        [UdonSynced] public bool UseGravity;
        
        [Header("References")]
        public Transform Deck;
        [HideInInspector] public VRCObjectPool Pool;
        
        private VRCPlayerApi playerLocal;
        private CardLogic[] cards;
        private GameObject currentCard;
        
        
        private void Start()
        {
            playerLocal = Networking.LocalPlayer;
            Pool = GetComponent<VRCObjectPool>();
            
            cards = new CardLogic[Pool.Pool.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = Pool.Pool[i].GetComponentInChildren<CardLogic>();
                cards[i].UseGravity = UseGravity;
                
                if (i < CardLogic.RankDefinitionsCount * 4)
                {
                    int col = i % CardLogic.RankDefinitionsCount;
                    int suitIndex = i / CardLogic.RankDefinitionsCount;
                    Debug.Log($"DeckManager: Assigning card {cards[i].name} to rank {col + 1}, suit {suitIndex}");
                    Rank rank = (Rank)(col + 1);
                    cards[i].SetCardIdentity(rank, (Suit)suitIndex);
                }
                else
                {
                    cards[i].SetJoker(i - CardLogic.RankDefinitionsCount * 4);
                }
                cards[i].ApplyFaceTexture();
            }
        }
        
        public void NextCard()
        {
            if (CardCurrent >= Pool.Pool.Length - 1)
            {
                Deck.localScale = Vector3.zero;
            }
            else
            {
                CardCount -= 1;
                CardCurrent += 1;
                RequestSerialization();
                
                Networking.SetOwner(playerLocal, Pool.gameObject);
                currentCard = Pool.TryToSpawn();
                Debug.Log($"DeckManager: Spawned card {currentCard.name} from pool, CardCurrent={CardCurrent}, CardCount={CardCount}");
                Networking.SetOwner(playerLocal, currentCard);
                
                SetCurrentCardToTop();
            }
        }

        public GameObject DrawNext()
        {
            NextCard();
            return currentCard;
        }
        
        public void _ResetDeck()
        {
            Networking.SetOwner(playerLocal, gameObject);
            
            foreach (CardLogic card in cards)
            {
                Networking.SetOwner(playerLocal, card.gameObject);
                card.Grabbed = false;
                card.RequestSerialization();
                Pool.Return(card.transform.parent.gameObject);
                if (!card.gameObject.activeSelf) continue;
                card._Drop();
            }
            
            CardCurrent = -1;
            CardCount = Pool.Pool.Length;
            
            Pool.Shuffle();
        }

        public void _ReturnCard(GameObject card)
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            CardCount += 1;
            CardCurrent -= 1;
            Pool.Return(card);
            SetCurrentCardToTop();
        }

        private void SetCurrentCardToTop()
        {
            if (!currentCard) return;
            currentCard.transform.localPosition = new Vector3(0,  cardCount * 0.002f, 0);
            VRCObjectSync sync = currentCard.GetComponent<VRCObjectSync>();
            if (sync)
            {
                sync.SetKinematic(true);
                sync.FlagDiscontinuity();
            }
        }
    }
}