using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class Area3D : UdonSharpBehaviour
{
    [Header("Target Behaviour")]
    [Tooltip("UdonBehaviour whose custom event runs when the local player enters/exits the trigger.")]
    public UdonBehaviour targetBehaviour;

    [Tooltip("Event name to invoke on targetBehaviour when the local player enters the trigger.")]
    public string enterEvent = "OnAreaEnter";

    [Tooltip("Event name to invoke on targetBehaviour when the local player exits the trigger.")]
    public string exitEvent = "OnAreaExit";

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        SendCustomEvent(targetBehaviour, enterEvent);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        SendCustomEvent(targetBehaviour, exitEvent);
    }

    private void SendCustomEvent(UdonBehaviour behaviour, string eventName)
    {
        if (behaviour == null) return;
        if (string.IsNullOrEmpty(eventName)) return;
        behaviour.SendCustomEvent(eventName);
    }
}
