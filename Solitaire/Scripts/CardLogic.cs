using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

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

    // A card. Nothing here is synced.
    //
    // Where this card sits, which way it is facing and whether it is in someone's
    // hand all live in Solitaire's board arrays, which travel as a single packet.
    // This behaviour holds a local mirror of its own row of that board and renders
    // it; every write goes back through Solitaire, which owns the wire format.
    //
    // That split is the whole point. When each card carried its own synced link, a
    // single logical move depended on one of 52 independent network objects landing,
    // any of which could be throttled away or arrive out of order - so the table was
    // routinely observed half-applied, and a dropped packet was permanent because
    // the link was a delta with no later packet to correct it. A card is now a view
    // of shared state, and shared state is absolute.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
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
        [Tooltip("If true, everyone can see this card's value. If false, only the card's owner sees the front, others see the hidden face. Mirror of the board; write it with SetFaceVisible.")]
        [HideInInspector] public bool FaceVisible;

        [Tooltip("If true, the card is physically flipped face-up (rotated 180 degrees about its local Z axis). Mirror of the board; write it with SetFaceUp.")]
        [HideInInspector] public bool FaceUp;

        [Tooltip("The material displaying the face texture atlas. Must be assigned so the correct renderer/slot is targeted.")]
        [SerializeField] private Material FaceMaterial;

        [Header("Placement")]
        [Tooltip("SlotId of the slot this card is sitting in, or -1 when the card is loose. Mirror of the board; write it with _SetPrevSlot or _ForcePlace.")]
        [HideInInspector] public int PrevSlotId = -1;

        [Tooltip("This card's own slot - the spot directly on top of it, where the next card in the pile lands.")]
        [HideInInspector] public CardSlot Slot;

        // This card's row in the board arrays, and its identity on the wire. Assigned
        // by Solitaire.Init from the index in the deck's VRCObjectPool, which is the
        // one card identity every client already agrees on: Shuffle permutes a
        // private draw order inside the pool, never the Pool array itself.
        [HideInInspector] public int PoolIndex = -1;

        public Rank CardRank => _rank;
        public Suit CardSuit => _suit;
        [HideInInspector] public bool IsJoker;
        [HideInInspector] public int JokerIndex;

        private VRCPickup pickup;
        private Renderer faceRenderer;
        private Material faceMaterial;
        private int faceMaterialIndex;
        private bool initialized;
        private bool rejecting;
        private bool suppressDrop;
        private int savedPrevSlotId = -1;

        // Held state lives on Solitaire so that one synced pair of hand slots covers
        // the whole deck, rather than every card carrying a grab payload it almost
        // never uses.
        public bool Grabbed
        {
            get
            {
                if (Solitaire == null) return false;
                return Solitaire._IsCardGrabbed(this);
            }
        }

        // The slot this card is stacked on. Resolved from PrevSlotId, so every
        // client walks the same chain without the reference itself going anywhere
        // near the network.
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

        // The pool activating this card and the board naming it are two independent
        // network messages with no ordering guarantee. When the board wins the race
        // this behaviour was still disabled, so nothing placed the card. Re-deriving
        // on enable closes the gap, and because the board is absolute rather than a
        // delta, re-applying it costs nothing when the card was already right.
        //
        // The catch-up has to be deferred. This fires from inside the pool's
        // TryToSpawn, which the dealer calls from inside Solitaire's own event, and
        // the catch-up reads back through Solitaire. Udon restores the program
        // counter across a re-entrant call but not the heap, and UdonSharp keeps
        // method locals on the heap - so calling Solitaire from here would scribble
        // over the locals of the deal loop still running up the stack. A zero-frame
        // delay runs it once that stack has unwound.
        private void OnEnable()
        {
            SendCustomEventDelayedFrames(nameof(_OnSpawned), 0);
        }

        public void _OnSpawned()
        {
            if (!initialized) Init();
            if (Solitaire != null)
            {
                // Hands off entirely: the coalesced re-apply covers this card's
                // face and pickup flag along with everyone else's, and doing them
                // here as well would pay for a pile walk per card in a frame where
                // a whole batch of cards can come up at once.
                Solitaire._OnCardSpawned(this);
                return;
            }
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

            // The card rigidbody must stay kinematic. A kinematic body can't be
            // knocked by collisions, so a resting card stays put and never needs
            // continuous sync. Make the cards dynamic and a full tableau turns
            // into a pile of colliding rigidbodies all fighting each other.
            VRCPlayerApi local = Networking.LocalPlayer;
            if (pickup != null && Utilities.IsValid(local))
            {
                pickup.AutoHold = local.IsUserInVR()
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
            bool showFace = FaceUp && (localOwns || FaceVisible);

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

        // Push one row of the board into this card. Solitaire calls this on every
        // client - the dealer straight after it writes, everyone else out of
        // OnDeserialization - so the two paths cannot drift. Placement and rendering
        // are deliberately left to the caller: the whole board has to be mirrored
        // before any of it can be laid out, because a fan window's offset depends on
        // how many cards are above and that answer is only right once every link is
        // in.
        public void _ApplyState(int slotId, bool faceUp, bool faceVisible)
        {
            if (!initialized) Init();
            PrevSlotId = slotId;
            FaceUp = faceUp;
            FaceVisible = faceVisible;
        }

        // Face-down tableau and reserve cards aren't grabbable at all - they get
        // turned over by play, once whatever was covering them moves off.
        public void _RefreshPickupable()
        {
            if (!initialized) Init();

            // Cheapest question first, because on every client but the dealer's it
            // is the only one that matters. Only the player who started the game
            // may grab cards; everyone else sees them anchored so VRChat never
            // offers the pickup, and cards stay anchored while a deal is running so
            // nothing can be pulled out of a pile that is still being built. Asked
            // up front, a spectator's sweep over the whole deck costs nothing -
            // asked last, as it used to be, every card walked its pile twice to
            // reach an answer that was never in doubt.
            if (Solitaire != null
                && (Solitaire._IsDealing()
                    || !Solitaire._IsGameStarted()
                    || !Solitaire._IsLocalGameOwner()))
            {
                if (pickup != null) pickup.pickupable = false;
                return;
            }

            CardSlot pileSlot = PrevSlot;
            bool allowed = FaceUp || Solitaire == null
                || (!Solitaire._IsTableauChain(pileSlot) && !Solitaire._IsReserveChain(pileSlot));

            // Pile pick-up policy comes from the base slot: all face-up cards, just
            // the top, or none. Face-down cards stay blocked regardless.
            if (allowed && pileSlot != null)
            {
                CardPickupMode mode = pileSlot._GetPickupMode();
                if (mode == CardPickupMode.None) allowed = false;
                else if (mode == CardPickupMode.TopOnly && pileSlot._GetTopCard() != this) allowed = false;
            }

            // Spider will not let a group move unless the cards riding on this one
            // continue it as a same-suit run. Solitaire owns that rule and returns
            // true for modes that do not restrict it.
            if (allowed && Solitaire != null && !Solitaire._IsGroupMovable(this))
            {
                allowed = false;
            }

            if (pickup != null) pickup.pickupable = allowed;
        }

        // ---- Writes. Each of these keeps its old name and signature so the call
        // sites in Solitaire read exactly as they did; what changed is that the
        // change now lands in one shared array instead of this card's own synced
        // fields, and reaches everyone else in a single packet with whatever else
        // moved in the same frame.

        public void SetFaceVisible(bool visible)
        {
            if (!initialized) Init();
            if (Solitaire == null) return;
            Solitaire._WriteCard(this, PrevSlotId, FaceUp, visible);
        }

        public void ToggleFaceVisible()
        {
            SetFaceVisible(!FaceVisible);
        }

        public void SetFaceUp(bool up)
        {
            if (!initialized) Init();
            if (Solitaire == null) return;
            Solitaire._WriteCard(this, PrevSlotId, up, up);
        }

        // Link this card onto a slot.
        public void _SetPrevSlot(CardSlot slot)
        {
            if (!initialized) Init();
            if (Solitaire == null) return;
            Solitaire._WriteCard(this, slot != null ? slot.SlotId : -1, FaceUp, FaceVisible);
        }

        // Place without consulting any rule - used by the dealer.
        public void _ForcePlace(CardSlot slot, bool faceUp)
        {
            if (!initialized) Init();
            if (Solitaire == null) return;
            Solitaire._WriteCard(this, slot != null ? slot.SlotId : -1, faceUp, faceUp);
        }

        // Unlink and send the card back to its pool parent.
        public void _Detach(Transform home)
        {
            if (!initialized) Init();
            if (Solitaire != null) Solitaire._WriteCard(this, -1, false, false);
            _ReturnHome(home);
        }

        // Park a card the board no longer places back under the pool. Only the
        // parenting: activating and positioning an undealt card is the pool's job.
        public void _ReturnHome(Transform home)
        {
            if (!initialized) Init();
            if (Grabbed) return;
            if (home == null || CardRoot == null) return;
            if (CardRoot.parent == home) return;
            CardRoot.SetParent(home, false);
        }

        // Snap the card onto whatever PrevSlotId currently points at. Runs on every
        // client, which is what keeps the piles identical everywhere.
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
        }

        // Re-derive where this card belongs and move it only if the answer changed.
        //
        // A fan window makes a card's resting place depend on how many cards are above
        // it, so growing or shrinking a pile silently restates the position of cards
        // that never moved. Re-applying all of them through _ApplyPlacement would work
        // and would also reparent every one of them, which is a pile's worth of
        // transform churn per draw. Almost none have actually shifted, so compare first.
        public void _RefreshPlacement()
        {
            if (!initialized) Init();
            if (Grabbed) return;

            CardSlot slot = PrevSlot;
            if (slot == null || slot == Slot) return;

            Transform mover = CardRoot;
            if (mover == null) mover = transform;

            Vector3 local = slot._GetOffsetForNext();
            if (mover.parent == slot.transform
                && (mover.localPosition - local).sqrMagnitude < PlacementEpsilonSqr)
            {
                return;
            }
            _ApplyPlacement();
        }

        // (0.1mm)^2. The offsets a fan window switches between are centimetres apart,
        // so this only has to be tighter than that and looser than float noise.
        private const float PlacementEpsilonSqr = 1e-8f;

        // Refuse a pickup: put the card straight back where it came from.
        //
        // `rejecting` is cleared at the top of the next OnPickup and nowhere else.
        // VRChat may fire OnDrop out of the Drop() below either synchronously or a
        // frame later, and if OnDrop cleared the flag on the way through, the
        // synchronous case would land back in OnPickup with nothing left to say the
        // grab had been refused - and it would go on to take the card anyway.
        public void _Reject()
        {
            if (!initialized) Init();
            rejecting = true;
            if (pickup != null) pickup.Drop();
        }

        public override void OnPickup()
        {
            if (!initialized) Init();
            rejecting = false;
            savedPrevSlotId = PrevSlotId;

            if (Solitaire != null) Solitaire._OnCardPickup(this);
            // _Reject already asked the pickup to drop; OnDrop restores the link.
            if (rejecting) return;

            if (Solitaire != null)
            {
                // Claim a hand before unlinking: _WriteCard re-applies placement, and
                // a card the board knows is held is one _ApplyPlacement leaves alone.
                Solitaire._BeginGrab(this);
                Solitaire._WriteCard(this, -1, FaceUp, FaceVisible);
            }
            ApplyFaceTexture();
        }

        public override void OnDrop()
        {
            if (!initialized) Init();
            if (Solitaire != null) Solitaire._EndGrab(this);

            // The table was torn down under a held card; the reset owns what happens
            // next and the drop rules must not run over the top of it.
            if (suppressDrop)
            {
                suppressDrop = false;
                return;
            }

            // Left set for the next OnPickup to clear - see _Reject.
            if (rejecting)
            {
                if (Solitaire != null) Solitaire._WriteCard(this, savedPrevSlotId, FaceUp, FaceVisible);
                ApplyFaceTexture();
                return;
            }

            if (Solitaire != null) Solitaire._OnCardDrop(this);
            else _ApplyPlacement();
            ApplyFaceTexture();
        }

        public void _SnapBack()
        {
            if (!initialized) Init();
            if (Solitaire == null) return;
            Solitaire._WriteCard(this, savedPrevSlotId, FaceUp, FaceVisible);
        }

        public CardSlot _GetCurrentSlot()
        {
            return PrevSlot;
        }

        public void _Drop()
        {
            if (!initialized) Init();
            if (pickup != null) pickup.Drop();
        }

        // Release a held card without running the drop rules. Used when the table is
        // reset out from under someone's hand.
        //
        // Gated on IsHeld and cleared by OnDrop rather than here, because Drop() may
        // not call OnDrop until the next frame: clearing it inline would let the
        // deferred OnDrop run the drop rules over a table that no longer exists, and
        // clearing it nowhere would leave the flag armed to swallow a real drop.
        public void _ForceRelease()
        {
            if (!initialized) Init();
            if (pickup == null || !pickup.IsHeld) return;
            suppressDrop = true;
            pickup.Drop();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            ApplyFaceTexture();
        }
    }
}
