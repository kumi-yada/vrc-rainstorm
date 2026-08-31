
using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
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
                if (Pool == null || Pool.Pool == null) return 0;
                int remaining = Pool.Pool.Length - 1 - cardCurrent;
                return remaining > 0 ? remaining : 0;
            }
        }

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
                _RefreshInteractable();
            }
        }

        // The stock deck only invites an interact once a game is actually running.
        // Disabling the collider stops VRChat from offering the prompt at all;
        // the checks inside Interact() are a backstop for the moment ownership
        // changes before the collider swaps.
        public void _RefreshInteractable()
        {
            if (interactCollider == null) return;
            bool interactable = Solitaire != null && Solitaire._IsGameStarted();
            interactCollider.enabled = interactable;
        }
        
        [Header("Identity")]
        [Tooltip("Which game's table this deck belongs to. A table only adopts a player's deck PlayerObject when the keys match, so two card games can each keep their own per-player deck in one world. Leave at 0 unless there is more than one.")]
        public int DeckKey = 0;

        [Header("Layout")]
        [Tooltip("How many distinct suits are in play. 4 with a 52-card pool is a standard deck; 2 with a 104-card pool is Spider two-suit (4 copies of each suit); 1 with 52 is Spider one-suit. Must be at least 1 and no more than SuitPalette's length.")]
        public int SuitsInPlay = 4;

        [Tooltip("Which suits are dealt, in the order successive 13-card copies cycle through them. Only the first SuitsInPlay entries are used.")]
        public Suit[] SuitPalette = new Suit[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades };

        [Header("References")]
        public Transform Deck;
        public VRCObjectPool Pool;

        [Tooltip("Assigned by Solitaire at startup; drives what a draw does.")]
        [HideInInspector] public Solitaire Solitaire;
        
        private VRCPlayerApi playerLocal;
        private CardLogic[] cards;
        private GameObject currentCard;
        private Collider interactCollider;
        
        
        private void Start()
        {
            playerLocal = Networking.LocalPlayer;
            interactCollider = GetComponent<Collider>();

            // Pool is an inspector reference, so it can sit on any GameObject rather
            // than having to share this one. Decks authored before that carry no
            // reference at all, so fall back to the old same-object lookup - without
            // it every previously-wired deck would come up empty.
            if (Pool == null) Pool = GetComponent<VRCObjectPool>();
            if (Pool == null || Pool.Pool == null)
            {
                Debug.Log($"DeckManager: {name} has no VRCObjectPool assigned (key {DeckKey}); this deck cannot deal.");
                return;
            }

            // Identity is keyed off the pool index and assigned once, here. Pool.Shuffle
            // reorders the array rather than the identities, so a card's face stays
            // glued to its GameObject and only the draw order is random.
            int suitCount = ResolveSuitCount();
            int copies = Pool.Pool.Length / CardLogic.RankDefinitionsCount;

            cards = new CardLogic[Pool.Pool.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                // Pool objects are already inactive by this point.
                cards[i] = Pool.Pool[i].GetComponentInChildren<CardLogic>(true);

                if (i < copies * CardLogic.RankDefinitionsCount)
                {
                    int col = i % CardLogic.RankDefinitionsCount;
                    // Successive 13-card copies cycle through the palette, so a
                    // 104-card two-suit deck lands 4 copies of each of its two suits.
                    int suitIndex = (i / CardLogic.RankDefinitionsCount) % suitCount;
                    Suit suit = SuitPalette[suitIndex];
                    cards[i].SetCardIdentity((Rank)(col + 1), suit);
                }
                else
                {
                    // Whatever is left over past a whole number of 13-card copies.
                    cards[i].SetJoker(i - copies * CardLogic.RankDefinitionsCount);
                }
                cards[i].ApplyFaceTexture();
            }
            Debug.Log($"DeckManager: Built {Pool.Pool.Length} cards as {copies} copies of {CardLogic.RankDefinitionsCount} ranks over {suitCount} suit(s), key {DeckKey}.");

            // Pool is only known now, so the derived count is only meaningful now.
            RefreshDeckVisual();
            _RefreshInteractable();
        }
        
        // Clamps the configured suit count to something indexable and repairs an empty
        // palette, so a half-configured deck still deals rather than throwing. Also
        // reports a pool size that cannot divide evenly into the suits asked for -
        // that silently deals one suit more often than the others.
        private int ResolveSuitCount()
        {
            if (SuitPalette == null || SuitPalette.Length == 0)
            {
                Debug.Log("DeckManager: SuitPalette is empty; falling back to the standard four suits.");
                SuitPalette = new Suit[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades };
            }

            int count = SuitsInPlay;
            if (count < 1) count = 1;
            if (count > SuitPalette.Length) count = SuitPalette.Length;
            if (count != SuitsInPlay)
            {
                Debug.Log($"DeckManager: SuitsInPlay {SuitsInPlay} is out of range for a {SuitPalette.Length}-entry SuitPalette; using {count}.");
            }

            int ranks = CardLogic.RankDefinitionsCount;
            if (Pool != null && Pool.Pool != null)
            {
                int copies = Pool.Pool.Length / ranks;
                if (Pool.Pool.Length % ranks != 0)
                {
                    Debug.Log($"DeckManager: pool holds {Pool.Pool.Length} cards, which is not a whole number of {ranks}-rank copies; the remainder becomes jokers.");
                }
                else if (copies % count != 0)
                {
                    Debug.Log($"DeckManager: {copies} copies do not divide evenly into {count} suits; some suits will be dealt more often than others.");
                }
            }
            return count;
        }

        // Puts the deck where the table wants it.
        //
        // The pool has to come along: undealt cards are parented under the pool's
        // transform and SetCurrentCardToTop stacks them at its origin, so leaving it
        // behind would strand the face-down stock away from the deck mesh and its
        // interact collider. When the pool is this same object, or a child of it, the
        // first move already carried it and touching it again would only flatten its
        // local offset.
        public void _MoveTo(Transform home)
        {
            if (home == null) return;

            transform.position = home.position;
            transform.rotation = home.rotation;

            if (Pool == null) return;
            if (IsUnderThisDeck(Pool.transform)) return;
            Pool.transform.position = home.position;
            Pool.transform.rotation = home.rotation;
        }

        // Walks parents by hand rather than using Transform.IsChildOf, to stay on the
        // API surface the rest of this project already relies on.
        private bool IsUnderThisDeck(Transform candidate)
        {
            Transform current = candidate;
            int guard = 0;
            while (current != null && guard < 64)
            {
                if (current == transform) return true;
                current = current.parent;
                guard++;
            }
            return false;
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
                _RefreshInteractable();
                return;
            }
            if (!Networking.IsOwner(playerLocal, gameObject))
            {
                Debug.Log("DeckManager: Only the player who started the game may use the deck.");
                return;
            }
            Solitaire._OnStockClicked();
        }

        public override void OnDeserialization()
        {
            _RefreshInteractable();
            if (Solitaire != null) Solitaire._RefreshStartInteractable();
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

        private const int MaxSerializationRetries = 5;
        private int serializationRetries;

        // Losing this behaviour's state loses the whole game: the stock index and
        // the game owner drive what every other client is allowed to do. A throttled
        // serialization is dropped silently, so keep asking until it lands.
        public override void OnPostSerialization(SerializationResult result)
        {
            if (result.success)
            {
                serializationRetries = 0;
                return;
            }
            if (serializationRetries >= MaxSerializationRetries) return;
            serializationRetries++;
            SendCustomEventDelayedSeconds(nameof(_RetrySerialization), 0.25f * serializationRetries);
        }

        public void _RetrySerialization()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            if (!Networking.IsOwner(local, gameObject)) return;
            RequestSerialization();
        }

        public void _ResetDeck()
        {
            // cards is null when Start bailed on a missing pool, and every line below
            // dereferences one or the other.
            if (Pool == null || cards == null) return;

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
            if (Pool == null) return;
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