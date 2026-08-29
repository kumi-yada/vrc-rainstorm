
using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using MMMaellon;

namespace org.kumagee
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DeckManager : UdonSharpBehaviour
    {
        
        // Index of the last card handed out; -1 means nothing drawn yet. This is the
        // only stock state that syncs - everything else is derived from it, so the
        // deck visual can't disagree with what NextCard will actually give you.
        [UdonSynced, SerializeField, FieldChangeCallback(nameof(CardCurrent))] private int cardCurrent = -1;
        public int CardCurrent
        {
            get => cardCurrent;
            set
            {
                cardCurrent = value;
                RefreshDeckVisual();
            }
        }

        // Cards still face down in the stock.
        public int CardCount
        {
            get
            {
                if (Pool == null) return 0;
                int remaining = Pool.Pool.Length - 1 - cardCurrent;
                return remaining > 0 ? remaining : 0;
            }
        }

        [UdonSynced] public bool UseGravity;

        // Player id of whoever started (dealt) the current game. Only they are
        // allowed to interact with the deck and cards. -1 means no game yet.
        [UdonSynced] private int gameOwnerId = -1;
        public int GameOwnerId => gameOwnerId;

        public void _SetGameOwner(int playerId)
        {
            if (gameOwnerId != playerId)
            {
                gameOwnerId = playerId;
                RequestSerialization();
            }
        }
        
        [Header("References")]
        public Transform Deck;
        [HideInInspector] public VRCObjectPool Pool;

        [Tooltip("Assigned by Solitaire at startup; drives what a draw does.")]
        [HideInInspector] public Solitaire Solitaire;
        
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
                // Pool objects are already inactive by this point.
                cards[i] = Pool.Pool[i].GetComponentInChildren<CardLogic>(true);
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

            // Pool is only known now, so the derived count is only meaningful now.
            RefreshDeckVisual();
        }
        
        // Every pool object has been handed out, so there is nothing left to draw.
        public bool _IsStockEmpty()
        {
            return Pool == null || CardCount <= 0;
        }

        private void RefreshDeckVisual()
        {
            if (Deck == null) return;
            // The deck mesh is authored one card thick, so the scale is the count.
            int remaining = CardCount;
            Deck.localScale = remaining <= 0 ? Vector3.zero : new Vector3(1, remaining, 1);
        }

        // Turning the stock over sends the card to the waste pile; once the stock
        // runs out the same click recycles the waste back into it. Needs a collider
        // on this GameObject for VRChat to offer the interact.
        public override void Interact()
        {
            if (Solitaire == null || !Solitaire._IsGameStarted())
            {
                Debug.Log("DeckManager: Game hasn't started yet; stock interaction disabled.");
                return;
            }
            if (!Solitaire._IsLocalGameOwner())
            {
                Debug.Log("DeckManager: Only the player who started the game may use the deck.");
                return;
            }
            Solitaire._OnStockClicked();
        }

        public void NextCard()
        {
            if (_IsStockEmpty())
            {
                currentCard = null;
                RefreshDeckVisual();
            }
            else
            {
                CardCurrent += 1;
                RequestSerialization();
                
                Networking.SetOwner(playerLocal, Pool.gameObject);
                currentCard = Pool.TryToSpawn();
                if (currentCard == null) return;
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
                Pool.Return(card.gameObject);
                if (!card.gameObject.activeSelf) continue;
                card._Drop();
            }
            
            CardCurrent = -1;
            RequestSerialization();

            Pool.Shuffle();
        }

        public void _ReturnCard(GameObject card)
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            CardCurrent -= 1;
            // Don't keep pointing the deck's top-of-stack at a card we just pooled.
            if (currentCard == card) currentCard = null;
            Pool.Return(card);
            // CardCurrent is synced; Udon coalesces repeat requests in a frame, so
            // calling this per card is fine.
            RequestSerialization();
            SetCurrentCardToTop();
        }

        private void SetCurrentCardToTop()
        {
            if (!currentCard) return;
            currentCard.transform.localPosition = new Vector3(0, CardCount * 0.002f, 0);
            SmartObjectSync sync = currentCard.GetComponent<SmartObjectSync>();
            if (sync)
            {
                sync.worldSpaceTeleport = false;
                sync.worldSpaceSleep = false;
                sync.worldSpacePhysics = false;
                sync.TakeOwnership(false);
                sync.TeleportToLocalSpace(currentCard.transform.localPosition,
                    currentCard.transform.localRotation, Vector3.zero, Vector3.zero);
            }
        }
    }
}