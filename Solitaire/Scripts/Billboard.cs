using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Billboard : UdonSharpBehaviour
{
    private Transform selfTransform;

    void Start()
    {
        selfTransform = transform;
    }

    void Update()
    {
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (!Utilities.IsValid(localPlayer))
        {
            return;
        }

        VRCPlayerApi.TrackingData head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        selfTransform.LookAt(head.position);
        selfTransform.Rotate(0f, 180f, 0f);
    }
}