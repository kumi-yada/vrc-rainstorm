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
        public Solitaire Solitaire;
        [HideInInspector] public Transform CardRoot;
        [SerializeField] private Rank _rank;
        [SerializeField] private Suit _suit;

        [Header("Face")]
        [Tooltip("If true, everyone can see this card's value. If false, only the card's owner sees the front, others see the hidden face.")]
        [HideInInspector] [UdonSynced] public bool FaceVisible;

        [Tooltip("If true, the card is physically flipped face-up (rotated 180 degrees about its local Z axis).")]
        [HideInInspector] [UdonSynced] public bool FaceUp;

        [Tooltip("The material displaying the face texture atlas. Must be assigned so the correct renderer/slot is targeted.")]
        [SerializeField] private Material FaceMaterial;

        [Header("Placement")]
        [Tooltip("SlotId of the slot this card is sitting in, or -1 when the card is loose. Udon can only sync primitives, so this int is the wire format for PrevSlot.")]
        [HideInInspector] [UdonSynced] public int PrevSlotId = -1;

        [Tooltip("This card's own slot - the spot directly on top of it, where the next card in the pile lands.")]
        [HideInInspector] public CardSlot Slot;

        public Rank CardRank => _rank;
        public Suit CardSuit => _suit;
        [HideInInspector] public bool IsJoker;
        [HideInInspector] public int JokerIndex;
        [HideInInspector] [UdonSynced] public bool Grabbed;
        [HideInInspector] public bool UseGravity;

        private VRCPickup pickup;
        private SmartObjectSync sync;
        private Renderer faceRenderer;
        private Material faceMaterial;
        private int faceMaterialIndex;
        private bool initialized;
        private bool rejecting;

        // The slot this card is stacked on. Resolved from the synced PrevSlotId, so
        // every client walks the same chain without the reference itself going over
        // the network.
        public CardSlot PrevSlot
        {
            get
            {
                if (Solitaire == null) return null;
                return Solitaire._ResolveSlot(PrevSlotId);
            }
        }


        private void Start()
        {
            if (!initialized) Init();
            ApplyFaceTexture();
            _RefreshPickupable();
        }

        private void Init()
        {
            initialized = true;
            ResolveFaceMaterial();
            if (Slot == null) Slot = GetComponent<CardSlot>();
            if (Slot != null) Slot.Owner = this;
            pickup = GetComponent<VRCPickup>();
            if (pickup == null && transform.parent != null)
            {
                pickup = transform.parent.GetComponent<VRCPickup>();
            }
            if (pickup != null) sync = pickup.GetComponent<SmartObjectSync>();
            if (sync != null)
            {
                sync.worldSpaceTeleport = false;
                sync.worldSpaceSleep = false;
                sync.worldSpacePhysics = false;
                sync.respawnIntoStartingState = false;
            }
            if (pickup != null)
            {
                pickup.AutoHold = Networking.LocalPlayer.IsUserInVR()
                    ? VRC_Pickup.AutoHoldMode.No
                    : VRC_Pickup.AutoHoldMode.Yes;
            }
            CardRoot = pickup != null ? pickup.transform : transform;
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
                // The atlas row runs 2,3,...,10,J,Q,K,A, so the ace is the last
                // column rather than the first and every other rank shifts down one.
                col = ((int)_rank + AtlasColumns - 2) % AtlasColumns;
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
            TakeOwnership();
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
            TakeOwnership();
            FaceUp = up;
            FaceVisible = up;
            RequestSerialization();
            ApplyFaceTexture();
            _RefreshPickupable();
        }

        // Face-down tableau cards aren't grabbable at all - they get turned over by
        // play, once whatever was covering them moves off.
        public void _RefreshPickupable()
        {
            if (!initialized) Init();
            bool allowed = FaceUp || Solitaire == null || !Solitaire._IsTableauChain(PrevSlot);

            // Only the player who started the game may grab cards. Everyone else
            // sees them as anchored so VRChat never offers the pickup.
            if (Solitaire != null)
            {
                if (!Solitaire._IsGameStarted() || !Solitaire._IsLocalGameOwner())
                {
                    allowed = false;
                }
            }

            // SmartObjectSync owns pickup.pickupable and re-asserts it on every state
            // change, so drive its flag rather than the pickup's directly.
            if (sync != null) sync.pickupable = allowed;
            else if (pickup != null) pickup.pickupable = allowed;
        }

        private void TakeOwnership()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            if (!Networking.IsOwner(local, gameObject)) Networking.SetOwner(local, gameObject);
        }

        // Link this card onto a slot and tell everyone else about it. The single
        // synced int is the whole story: every client re-derives parenting, layout
        // and pile membership from it.
        public void _SetPrevSlot(CardSlot slot)
        {
            if (!initialized) Init();
            TakeOwnership();
            PrevSlotId = slot != null ? slot.SlotId : -1;
            RequestSerialization();
            _ApplyPlacement();
            _RefreshPickupable();
        }

        // Place without consulting any rule - used by the dealer.
        public void _ForcePlace(CardSlot slot, bool faceUp)
        {
            if (!initialized) Init();
            TakeOwnership();
            FaceUp = faceUp;
            FaceVisible = faceUp;
            Grabbed = false;
            PrevSlotId = slot != null ? slot.SlotId : -1;
            RequestSerialization();
            _ApplyPlacement();
            ApplyFaceTexture();
            _RefreshPickupable();
        }

        // Unlink and send the card back to its pool parent.
        public void _Detach(Transform home)
        {
            if (!initialized) Init();
            TakeOwnership();
            PrevSlotId = -1;
            Grabbed = false;
            FaceUp = false;
            FaceVisible = false;
            RequestSerialization();
            if (home != null && CardRoot != null && CardRoot.parent != home)
            {
                CardRoot.SetParent(home, false);
            }
            ApplyFaceTexture();
            _RefreshPickupable();
        }

        // Snap the card onto whatever PrevSlotId currently points at. Runs on every
        // client, including remotes reacting to OnDeserialization, which is what
        // keeps the piles identical everywhere.
        public void _ApplyPlacement()
        {
            if (!initialized) Init();
            if (Grabbed) return;

            CardSlot slot = PrevSlot;
            if (slot == null) return;
            if (slot == Slot) return;

            Transform mover = CardRoot;
            if (mover == null) mover = transform;

            Vector3 local = slot._GetOffsetForNext();
            bool align = slot._GetAlignRotation();
            Quaternion worldRot = mover.rotation;

            if (mover.parent != slot.transform) mover.SetParent(slot.transform, false);
            mover.localPosition = local;
            if (align) mover.localRotation = Quaternion.identity;
            else mover.rotation = worldRot;
            Quaternion localRot = mover.localRotation;

            if (sync != null)
            {
                sync.worldSpaceTeleport = false;
                sync.worldSpaceSleep = false;
                sync.worldSpacePhysics = false;
                // SmartObjectSync syncs local-space pose against transform.parent, so
                // only the owner writes it; remotes already share the same parent.
                if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
                {
                    sync.TeleportToLocalSpace(local, localRot, Vector3.zero, Vector3.zero);
                }
            }
        }

        // Refuse a pickup: put the card straight back where it came from.
        public void _Reject()
        {
            if (!initialized) Init();
            rejecting = true;
            if (pickup != null) pickup.Drop();
        }

        public override void OnPickup()
        {
            if (!initialized) Init();
            // Clear any stale reject from a Drop() that never produced an OnDrop,
            // otherwise it would swallow this pickup's drop instead.
            rejecting = false;
            Grabbed = true;
            TakeOwnership();
            RequestSerialization();
            if (Solitaire != null) Solitaire._OnCardPickup(this);
            ApplyFaceTexture();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            ApplyFaceTexture();
        }

        public override void OnDeserialization()
        {
            _ApplyPlacement();
            ApplyFaceTexture();
            _RefreshPickupable();
            // Cards riding on top of this one don't serialize when it moves, so the
            // link change has to push their layout along with it.
            if (Solitaire != null) Solitaire._RepositionAbove(this);
        }

        public void _Drop()
        {
            if (!initialized) Init();
            if (pickup != null) pickup.Drop();
        }

        public CardSlot _GetCurrentSlot()
        {
            return PrevSlot;
        }

        public override void OnDrop()
        {
            if (!initialized) Init();
            Grabbed = false;
            RequestSerialization();

            if (rejecting)
            {
                rejecting = false;
                _ApplyPlacement();
                ApplyFaceTexture();
                return;
            }

            if (Solitaire != null) Solitaire._OnCardDrop(this);
            else _ApplyPlacement();
            ApplyFaceTexture();
        }
    }
}
