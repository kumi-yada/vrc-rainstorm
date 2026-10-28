
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;
using VRC.SDK3.Components;
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

    [Tooltip("Image shown as the angle indicator (bi_alt.png). Grey at 0°, white otherwise.")]
    public Image angleImage;

    [Tooltip("Sprite assigned to angleImage (Casino/Images/bi_alt.png).")]
    public Sprite angleSprite;

    [Tooltip("Seconds the pivot takes to reach the toggled angle.")]
    public float tweenDuration = 0.25f;

    [Tooltip("Localization manager translating the interact prompt. Falls back to a scene search if empty.")]
    public LocalizationManager localization;

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

    // The in-flight rotation tween, killed and recreated on every _Apply so a
    // rapid re-toggle cuts the previous motion short instead of piling up.
    private VRCTweenHandle _angleTween;

    // The interactable collider sharing this object. VRChat offers the interact
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
        _ResolveLocalization();
        // Registered so a language switch rebuilds the hover prompt, which this
        // behaviour owns.
        if (localization != null) localization.Register(this);

        _CaptureBase();
        _Apply();
        _RefreshInteractable();
    }

    // Found lazily rather than only in Start: the synced state can be applied on
    // a freshly activated object before Start has run, and the prompt would
    // otherwise be written while the manager is still unresolved.
    private void _ResolveLocalization()
    {
        if (localization != null) return;
        GameObject locGo = GameObject.Find("Localization");
        if (locGo != null) localization = locGo.GetComponent<LocalizationManager>();
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
        // collider. The indicator would otherwise float over a table nobody is
        // using, and a disabled-but-visible control is not the same as a hidden
        // one. It comes back for everyone once a game is on, since the indicator
        // reflects the current angle; only the player whose game it is gets a
        // live collider. The pivot it turns is a separate transform, so hiding
        // this object leaves the table alone.
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
        if (angleImage != null) angleImage.enabled = _allowed;
        _RefreshInteractText();
    }

    // The prompt names the move the next press will make - raising the pivot
    // from its resting angle, or lowering it back down - so it flips with the
    // synced state on every client.
    private void _RefreshInteractText()
    {
        _ResolveLocalization();
        InteractionText = _Text(_open ? "ANGLE_DOWN" : "ANGLE_UP");
    }

    private string _Text(string key)
    {
        if (localization == null) return key;
        return localization.GetText(key);
    }

    // A language switch re-reads the prompt; the angle indicator is a sprite
    // and needs no translation.
    public void _OnLanguageChanged()
    {
        _RefreshInteractText();
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
            Quaternion target = _baseRotation * Quaternion.AngleAxis(angle, Vector3.right);
            _angleTween.Kill();
            _angleTween = pivot.TweenLocalRotation(target.eulerAngles, tweenDuration, VRCTweenEase.OutCubic)
                .OnComplete(this, nameof(_FireAngleChanged));
        }
        else
        {
            _FireAngleChanged();
        }

        if (angleImage != null)
        {
            if (angleSprite != null) angleImage.sprite = angleSprite;

            float shown = Mathf.Abs(angle);
            angleImage.color = shown == 0f ? new Color(0.25f, 0.25f, 0.25f) : Color.white;
        }

        _RefreshInteractText();
    }

    // Fires on the local client for its own toggles and, via OnDeserialization,
    // for everyone else's - so every client re-parks its own copy of the deck.
    // Dispatched from the tween's OnComplete so the deck settles against the
    // pivot's final angle, not the one it had when the interact landed.
    public void _FireAngleChanged()
    {
        if (_angleChangedTarget != null && !string.IsNullOrEmpty(_angleChangedEvent))
        {
            _angleChangedTarget.SendCustomEvent(_angleChangedEvent);
        }
    }
}
