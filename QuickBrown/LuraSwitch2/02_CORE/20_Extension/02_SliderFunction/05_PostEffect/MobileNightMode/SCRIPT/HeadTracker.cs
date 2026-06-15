
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class HeadTracker : UdonSharpBehaviour
{
    [SerializeField] private Transform TargetObject;

    void Update()
    {
        if (TargetObject == null) return;

        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer == null) return;

        VRCPlayerApi.TrackingData headTracking = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        TargetObject.SetPositionAndRotation(headTracking.position, headTracking.rotation);
    }
}
