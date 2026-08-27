using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace org.kumagee
{
    public abstract class SlotRule : UdonSharpBehaviour
    {
        public abstract bool AllowedToPlace(CardLogic[] cards, CardLogic card);
    }
}