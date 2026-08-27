
using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class CardSlot : UdonSharpBehaviour
    {
        [Header("Layout")]
        [Tooltip("Offset between each stacked card, in this slot's local X (right), Y (up) and Z (forward).")]
        public Vector3 Offset = new Vector3(0f, 0.002f, 0f);

        [Tooltip("Maximum number of cards this slot holds. 0 = unlimited.")]
        public int MaxCards = 12;

        [Tooltip("Snap the card's rotation to this slot's rotation when it is released.")]
        public bool AlignRotation = true;

        [Header("Rules")]
        public bool OnlyLastPickable = true;
        public bool IsPickable = true;

        [Header("Events")]
        [Tooltip("UdonBehaviour notified via SendCustomEvent when a card is placed in this slot.")]
        public UdonBehaviour PlacedTarget;

        [Tooltip("Event name sent to PlacedTarget when a card is placed.")]
        public string PlacedEvent = "_OnCardPlaced";

        [Tooltip("UdonBehaviour notified via SendCustomEvent when a card is removed from this slot.")]
        public UdonBehaviour RemovedTarget;

        [Tooltip("Event name sent to RemovedTarget when a card is removed.")]
        public string RemovedEvent = "_OnCardRemoved";

        private const int Capacity = 64;

        private CardLogic[] cards;
        private int cardCount;
        private bool initialized;
        private SlotRule rule;

        private void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (cards == null) cards = new CardLogic[Capacity];
            if (rule == null) rule = GetComponent<SlotRule>();
        }

        private bool Add(CardLogic logic, bool force = false)
        {
            if (MaxCards > 0 && cardCount >= MaxCards) return false;
            if (cardCount >= Capacity) return false;

            int firstFreeIndex = -1;
            for (int i = 0; i < MaxCards; i++)
            {
                if (cards[i] == null && firstFreeIndex < 0) firstFreeIndex = i;
                if (cards[i] == logic) return false;
            }
            if (firstFreeIndex < 0) {
                Debug.Log($"CardSlot: No free index found for card {logic.name} in slot {name}, but cardCount={cardCount}. Adding at end.");
                return false;
            }

            if (!force && rule != null)
            {
                if (!rule.AllowedToPlace(BuildStack(), logic))
                {
                    Debug.Log($"CardSlot: Card {logic.name} not allowed to be placed in slot {name}");
                    return false;
                }
            }

            cards[firstFreeIndex] = logic;
            cardCount++;
            Debug.Log($"CardSlot: Added card {logic.name} to slot {name} at index {firstFreeIndex}");
            return true;
        }

        private CardLogic[] BuildStack()
        {
            CardLogic[] stack = new CardLogic[cardCount];
            for (int i = 0; i < cardCount; i++)
            {
                stack[i] = cards[i];
            }
            return stack;
        }

        private bool Remove(CardLogic logic)
        {
            for (int i = 0; i < cardCount; i++)
            {
                if (cards[i] == logic)
                {
                    ShiftCardsLeft(i);
                    cardCount--;
                    return true;
                }
            }
            return false;
        }

        private void ShiftCardsLeft(int startIndex)
        {
            for (int i = startIndex; i < cardCount - 1; i++)
            {
                cards[i] = cards[i + 1];
            }
            cards[cardCount - 1] = null;
        }

        public int _GetCardCount()
        {
            Initialize();
            return cardCount;
        }

        public CardLogic _GetCardAt(int index)
        {
            Initialize();
            if (index < 0 || index >= cardCount) return null;
            return cards[index];
        }

        public CardLogic _GetTopCard()
        {
            return _GetCardAt(cardCount - 1);
        }

        public bool _PlaceCard(CardLogic logic)
        {
            Initialize();
            if (logic == null) return false;
            if (!Add(logic)) return false;
            Repack();
            SendPlacedEvent();
            return true;
        }

        public bool _ForceAdd(CardLogic logic)
        {
            Initialize();
            if (logic == null) return false;
            if (!Add(logic, true)) return false;
            Repack();
            return true;
        }

        public void _RemoveCard(CardLogic logic)
        {
            Initialize();
            if (logic == null) return;
            if (!Remove(logic)) return;
            Repack();
            SendRemovedEvent();
        }

        private void SendPlacedEvent()
        {
            if (PlacedTarget != null && !string.IsNullOrEmpty(PlacedEvent))
            {
                PlacedTarget.SendCustomEvent(PlacedEvent);
            }
        }

        private void SendRemovedEvent()
        {
            if (RemovedTarget != null && !string.IsNullOrEmpty(RemovedEvent))
            {
                RemovedTarget.SendCustomEvent(RemovedEvent);
            }
        }

        public void _SetPickable(bool value)
        {
            Initialize();
            IsPickable = value;
            Repack();
        }

        public void _Clear()
        {
            Initialize();
            for (int i = 0; i < MaxCards; i++)
            {
                if (cards[i] == null) continue;
                cards[i] = null;
            }
            cardCount = 0;
        }

        private void Repack()
        {
            int index = 0;
            int lastIndex = GetLastCardIndex();
            Debug.Log($"CardSlot: Repacking cards in slot {name}, cardCount={cardCount}, lastIndex={lastIndex}");
            for (int i = 0; i < MaxCards; i++)
            {
                CardLogic logic = cards[i];
                if (logic == null) continue;
                // if (logic.Grabbed) continue;
                PlaceCard(logic, index, i == lastIndex || !OnlyLastPickable);
                index++;
            }
        }

        private int GetLastCardIndex()
        {
            for (int i = cardCount - 1; i >= 0; i--)
            {
                if (cards[i] != null) return i;
            }
            return -1;
        }

        private void PlaceCard(CardLogic logic, int index, bool pickup = false)
        {
            Transform mover = logic.transform;
            if (mover == null) mover = logic.transform;

            Vector3 worldOffset = transform.TransformDirection(
                new Vector3(Offset.x * index, Offset.y * index, Offset.z * index));
            Vector3 pos = transform.position + worldOffset;
            mover.position = pos;
            Debug.Log($"CardSlot: Placing card {logic.name} at position {pos} in slot {name}");

            if (AlignRotation) mover.rotation = transform.rotation;

            VRCObjectSync sync = mover.GetComponent<VRCObjectSync>();
            if (sync != null)
            {
                sync.SetKinematic(true);
                sync.FlagDiscontinuity();
            }

            VRCPickup pickupComponent = mover.GetComponent<VRCPickup>();
            if (pickupComponent != null)
            {
                pickupComponent.pickupable = IsPickable && pickup;
            }
        }
    }
}
