
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

    // The interactable collider sharing this object. VRChat offers the "Rotate"
    // prompt off whatever collider sits on the behaviour's GameObject, so turning
    // it off is how the pivot stops advertising itself to players whose game this
    // is not. Resolved lazily, because the whole object is deactivated while the
    // table is idle and Start may not have run yet when the state first arrives.
    private Collider _interactCollider;

    // Whether the local player may interact right now: a game is running at the
    // table and the local player is the one playing it. Pushed in by the game that
    // owns the pivot (see _SetGameState), because this behaviour is kept generic
    // and has no way to ask about the game's state itself.
    private bool _allowed;

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
        _RefreshInteractable();
    }

    // Called by the table's game whenever its state changes. `running` is true
    // while any game is on - so a spectator's client sees it too - and `localOwner`
    // is true only on the machine of the player whose game it is. The pivot only
    // invites an interact from that player, and drops back to its default angle
    // while the table is idle so the next game always starts from a known position.
    public void _SetGameState(bool running, bool localOwner)
    {
        _allowed = running && localOwner;

        // The whole control goes away while the table is idle - not just its
        // collider. The label would otherwise float over a table nobody is using,
        // and a disabled-but-visible control is not the same as a hidden one. It
        // comes back for everyone once a game is on, since the label is the current
        // angle; only the player whose game it is gets a live collider. The pivot it
        // turns is a separate transform, so hiding this object leaves the table
        // alone.
        if (gameObject.activeSelf != running) gameObject.SetActive(running);
        _RefreshInteractable();

        if (!running && _open)
        {
            _open = false;
            // Only the object's owner can put the reset on the wire. That is the
            // player who last interacted, i.e. the one whose game just ended, so in
            // practice this lands; a client that does not own it still resets its own
            // copy below and every other client reaches the same place on its own.
            if (Networking.IsOwner(gameObject)) RequestSerialization();
            _Apply();
        }
    }

    private void _RefreshInteractable()
    {
        if (_interactCollider == null) _interactCollider = GetComponent<Collider>();
        if (_interactCollider != null) _interactCollider.enabled = _allowed;
    }

    public override void Interact()
    {
        // Backstop for the collider swap: while another player's game is running,
        // or none is, the pivot must not move for this player even if a press slips
        // through the frame the state changed on.
        if (!_allowed) return;

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
