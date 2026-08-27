using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace org.kumagee
{
    public enum Rank
    {
        Ace = 1,
        Two,
        Three,
        Four,
        Five,
        Six,
        Seven,
        Eight,
        Nine,
        Ten,
        Jack,
        Queen,
        King
    }

    public enum Suit
    {
        Clubs,
        Diamonds,
        Hearts,
        Spades
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class CardLogic : UdonSharpBehaviour
    {
        
        public const int RankDefinitionsCount = 13;
        
        private const int AtlasColumns = 13;
        private const int AtlasRows = 10;
        private const int JokerRowIndex = 4;
        private const int HiddenColIndex = 2;
        
        public DeckManager DeckManager;
        [SerializeField] private Rank _rank;
        [SerializeField] private Suit _suit;
        
        [Header("Face")]
        [Tooltip("If true, everyone can see this card's value. If false, only the card's owner sees the front, others see the hidden face.")]
        [HideInInspector] [UdonSynced] public bool FaceVisible;

        [Tooltip("If true, the card is physically flipped face-up (rotated 180 degrees about its local Z axis).")]
        [HideInInspector] [UdonSynced] public bool FaceUp;

        [Tooltip("The material displaying the face texture atlas. Must be assigned so the correct renderer/slot is targeted.")]
        [SerializeField] private Material FaceMaterial;
        
        public Rank CardRank => _rank;
        public Suit CardSuit => _suit;
        [HideInInspector] public bool IsJoker;
        [HideInInspector] public int JokerIndex;
        [HideInInspector] [UdonSynced] public bool Grabbed;
        [HideInInspector] public bool UseGravity;
        
        private VRCPickup pickup;
        private VRCObjectSync sync;
        private Renderer faceRenderer;
        private Material faceMaterial;
        private int faceMaterialIndex;
        private bool toBeReturned;
        private bool initialized;
        private CardSlot currentSlot;
        
        
        private void Start()
        {
            if (!initialized) Init();
            ApplyFaceTexture();
        }

        private void Init()
        {
            initialized = true;
            ResolveFaceMaterial();
            pickup = GetComponent<VRCPickup>();
            if (pickup == null && transform.parent != null)
            {
                pickup = transform.parent.GetComponent<VRCPickup>();
            }
            if (pickup != null) sync = pickup.GetComponent<VRCObjectSync>();
            if (pickup != null)
            {
                pickup.AutoHold = Networking.LocalPlayer.IsUserInVR()
                    ? VRC_Pickup.AutoHoldMode.No
                    : VRC_Pickup.AutoHoldMode.Yes;
            }
        }
        
        private void ResolveFaceMaterial()
        {
            faceRenderer = null;
            faceMaterial = null;
            faceMaterialIndex = 0;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            if (FaceMaterial != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    if (materials == null) continue;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == FaceMaterial)
                        {
                            faceRenderer = renderer;
                            faceMaterialIndex = i;
                            break;
                        }
                    }
                    if (faceRenderer != null) break;
                }
            }

            if (faceRenderer == null)
            {
                faceRenderer = renderers[0];
            }

            faceMaterial = faceRenderer.materials[faceMaterialIndex];
        }

        public void SetCardIdentity(Rank rank, Suit suit)
        {
            _rank = rank;
            _suit = suit;
            IsJoker = false;
        }
        
        public void SetJoker(int index)
        {
            IsJoker = true;
            JokerIndex = index;
        }
        
        public void ApplyFaceTexture()
        {
            if (!initialized) Init();
            if (!faceRenderer) return;
            if (!IsJoker && _rank == (Rank)0) return;
            
            float cellX = 1f / (float)AtlasColumns;
            float cellY = 1f / (float)AtlasRows;
            
            bool localOwns = Networking.IsOwner(Networking.LocalPlayer, gameObject);
            bool showFace = localOwns || FaceVisible;
            
            int col;
            int row;
            if (!showFace)
            {
                col = HiddenColIndex;
                row = JokerRowIndex;
            }
            else if (IsJoker)
            {
                col = JokerIndex;
                row = JokerRowIndex;
            }
            else
            {
                col = (int)_rank - 1;
                row = (int)_suit;
            }
            
            faceMaterial.SetTextureOffset("_MainTex", new Vector2(col * cellX, -row * cellY));
            ApplyFacing();
        }

        private void ApplyFacing()
        {
            if (faceRenderer == null) return;
            Transform visual = faceRenderer.transform;
            Vector3 angles = visual.localEulerAngles;
            angles.z = FaceUp ? 180f : 0f;
            visual.localEulerAngles = angles;
        }
        
        public void SetFaceVisible(bool visible)
        {
            if (!initialized) Init();
            FaceVisible = visible;
            RequestSerialization();
            ApplyFaceTexture();
        }
        
        public void ToggleFaceVisible()
        {
            SetFaceVisible(!FaceVisible);
        }

        public void SetFaceUp(bool up)
        {
            if (!initialized) Init();
            FaceUp = up;
            FaceVisible = up;
            RequestSerialization();
            ApplyFaceTexture();
        }
        
        public override void OnPickup()
        {
            if (!initialized) Init();
            if (Grabbed) return;
            Grabbed = true;
            toBeReturned = false;
            if (sync != null) sync.SetKinematic(!UseGravity);
            ApplyFaceTexture();
        }
        
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            ApplyFaceTexture();
        }
        
        public override void OnDeserialization()
        {
            ApplyFaceTexture();
        }
        
        private void OnTriggerEnter(Collider other)
        {
            if (DeckManager == null) return;
            if (other.transform == DeckManager.Deck)
            {
                toBeReturned = true;
            }

            CardSlot slot = other.GetComponent<CardSlot>();
            if (slot != null)
            {
                currentSlot = slot;
            }
        }
        
        private void OnTriggerExit(Collider other)
        {
            if (DeckManager == null) return;
            if (other.transform == DeckManager.Deck)
            {
                toBeReturned = false;
            }

            CardSlot slot = other.GetComponent<CardSlot>();
            if (currentSlot != null && slot == currentSlot)
            {
                slot._RemoveCard(this);
                currentSlot = null;
            }
        }
        
        public void _Drop()
        {
            if (!initialized) Init();
            if (pickup != null) pickup.Drop();
        }
        
        public override void OnDrop()
        {
            if (!initialized) Init();
            Grabbed = false;
            if (toBeReturned)
            {
                if (DeckManager != null) DeckManager._ReturnCard(gameObject);
            }
            ApplyFaceTexture();

            if (currentSlot != null)
            {
                currentSlot._PlaceCard(this);
                currentSlot = null;
            }
        }
    }
}