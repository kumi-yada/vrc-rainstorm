
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// Interact toggles a pivot between two angles. The toggled state is synced, so
// everyone sees the same angle and late joiners get the current one on join.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ChangeAngle : UdonSharpBehaviour
{
    [Tooltip("Pivot rotated between the two angles.")]
    public Transform pivot;

    [Tooltip("Angle in degrees applied when toggled off.")]
    public float angleA = 0f;

    [Tooltip("Angle in degrees applied when toggled on.")]
    public float angleB = 45f;

    [Tooltip("Label showing the other angle value.")]
    public TextMeshProUGUI angleText;

    // Objects parented under the pivot follow it for free, but something parked at
    // a transform beneath the pivot without being a child of it - a per-player deck
    // PlayerObject, say - has to be moved again by hand after every change. Such a
    // behaviour registers here and is sent the registered event once the pivot has
    // moved. Set at runtime rather than in the inspector so nothing has to be wired.
    private UdonBehaviour _angleChangedTarget;
    private string _angleChangedEvent;

    [UdonSynced] private bool _open = false;

    private Quaternion _baseRotation;
    private bool _baseCaptured = false;

    // Registered by whoever needs to follow the pivot. Kept generic: this behaviour
    // stays reusable and knows nothing about what the target does with the event.
    public void _SetAngleChangedTarget(UdonBehaviour target, string eventName)
    {
        _angleChangedTarget = target;
        _angleChangedEvent = eventName;
    }

    private void Start()
    {
        _CaptureBase();
        _Apply();
    }

    public override void Interact()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        _open = !_open;
        RequestSerialization();
        _Apply();
    }

    public override void OnDeserialization()
    {
        _Apply();
    }

    private void _CaptureBase()
    {
        if (_baseCaptured || pivot == null)
        {
            return;
        }

        _baseRotation = pivot.localRotation;
        _baseCaptured = true;
    }

    private void _Apply()
    {
        float angle = _open ? angleB : angleA;

        if (pivot != null)
        {
            _CaptureBase();
            pivot.localRotation = _baseRotation * Quaternion.AngleAxis(angle, Vector3.right);
        }

        if (angleText != null)
        {
            float shown = Mathf.Abs(_open ? angleB : angleA);
            angleText.text = shown.ToString() + "°";
        }

        // Fires on the local client for its own toggles and, via OnDeserialization,
        // for everyone else's - so every client re-parks its own copy of the deck.
        if (_angleChangedTarget != null && !string.IsNullOrEmpty(_angleChangedEvent))
        {
            _angleChangedTarget.SendCustomEvent(_angleChangedEvent);
        }
    }
}
