using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace RBS.SleepKit2.Udon
{
    public class UseInteractionSender : UdonSharpBehaviour
    {
        [SerializeField] private UdonBehaviour targetUdon;

        [SerializeField] private string customEventName = "MyCustomEvent";

        public override void Interact()
        {
            if (targetUdon == null) return;
            targetUdon.SendCustomEvent(customEventName);
        }
    }
}