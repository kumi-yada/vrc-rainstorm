using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Billboard : UdonSharpBehaviour
{
    [Tooltip("Only rotate around the Y axis")]
    public bool yAxisOnly = false;

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
        if (yAxisOnly)
        {
            Vector3 target = head.position;
            target.y = selfTransform.position.y;
            selfTransform.LookAt(target);
        }
        else
        {
            selfTransform.LookAt(head.position);
        }
        selfTransform.Rotate(0f, 180f, 0f);
    }
}