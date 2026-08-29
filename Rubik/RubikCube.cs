using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using MMMaellon;

/// <summary>
/// Transform-based Rubik's cube. Setup:
/// 1. Root GameObject carries this UdonBehaviour + VRCPickup + a BoxCollider
///    big enough to cover the whole 3x3x3 volume.
/// 2. Add 26 cubie children (cube mesh each, no Rigidbody). Place them on the
///    -1/0/+1 grid, skipping the center (0,0,0). Cubie local position and
///    rotation are read at Start as the solved state.
/// 3. Optionally assign `cubies` in the Inspector; if empty, the script uses
///    the root's children in hierarchy order.
///
/// VR twist gesture: hold the cube in one hand, then squeeze grip with the
/// other hand near a face. The cube is made non-pickupable while held so the
/// second grip cannot steal it; instead that hand's wrist twist drives the
/// layer under it. The layer lifts out to show what is selected and tracks the
/// wrist 1:1, clicking haptically as it crosses each quarter turn; it only
/// snaps to the nearest quarter turn once the grip is released.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RubikCube : UdonSharpBehaviour
{
    private const int CUBIE_COUNT = 26;
    private const float STEP_ANGLE = 6f;

    [Header("Cubies (optional; falls back to root children)")]
    public Transform[] cubies;

    [Header("Canvas (toggled by a single click on the hand trigger)")]
    public GameObject canvas;
    [Tooltip("Release the trigger within this many seconds to count as a single click / tap.")]
    public float tapSeconds = 0.25f;

    [Header("Shuffle Hold Indicator (scales X 0-1 while holding)")]
    public Transform shuffleIndicatorPivot;
    public Transform shuffleIndicator;
    [Tooltip("Hold the trigger this long before the scale indicator appears.")]
    public float indicatorDelay = 0.4f;

    [Header("Input")]
    public bool keyboardControls = true;

    [Header("Twist Gesture (grip with the hand that is not holding the cube)")]
    public bool twistGesture = true;
    [Tooltip("Hand must be at least this far from the cube centre, in cubie units.")]
    public float twistMinDistance = 0.8f;
    [Tooltip("Hand must be no further than this from the cube centre, in cubie units. Controller tracking sits behind the fingers, so leave some slack.")]
    public float twistMaxDistance = 6f;
    [Tooltip("How far the selected layer lifts out of the cube while twisting, in cubie units.")]
    public float layerLiftAmount = 0.08f;
    public bool twistHaptics = true;

    [Header("Shuffle")]
    public KeyCode shuffleKey = KeyCode.Q;
    public float shuffleHoldSeconds = 2f;
    public int shuffleMoveCount = 20;

    private bool _shuffleActive;
    private int _shuffleRemaining;
    private float _keyHoldTime;
    private bool _keyShuffleFired;
    private float _useHoldTime;
    private bool _useHeld;
    private bool _useShuffleFired;

    private VRC_Pickup _pickup;
    private SmartObjectSync _sync;
    private VRCPlayerApi _localPlayer;

    private Transform[] _cubies;
    private Vector3[] _homePos;
    private Quaternion[] _homeRot;
    private Vector3[] _basePos;
    private Quaternion[] _baseRot;
    private bool[] _inLayer = new bool[CUBIE_COUNT];
    private int _cubieCount;

    private bool _isMoving;
    private int _moveAxis;
    private int _moveLayer;
    private int _moveDir;
    private float _moveAngle;
    private float _moveTarget;
    private float _moveSpeed;

    private bool _hasReceivedState;
    private int _appliedCounter;

    private Vector3 _indicatorOriginalScale;

    private bool _gestureActive;
    private int _gestureAxis;
    private int _gestureLayer;
    private float _gestureAngle;
    private int _gestureDetent;
    private Quaternion _gesturePrevRot;
    private VRC_Pickup.PickupHand _gestureHand;
    private VRCPlayerApi.TrackingDataType _gestureTrack;

    [UdonSynced] private Vector3[] _cubiePos = new Vector3[CUBIE_COUNT];
    [UdonSynced] private Quaternion[] _cubieRot = new Quaternion[CUBIE_COUNT];
    [UdonSynced] private int _lastMoveAxis;
    [UdonSynced] private int _lastMoveLayer;
    [UdonSynced] private int _lastMoveDir;
    [UdonSynced] private int _moveCounter;

    void Start()
    {
        _BuildCubies();

        _localPlayer = Networking.LocalPlayer;

        // SmartObjectSync owns `pickupable` at runtime (it rewrites the VRCPickup
        // field every time the hold state changes), so it has to be the thing we
        // talk to when locking the cube into one hand.
        _sync = GetComponent<SmartObjectSync>();
        if (_sync != null && _sync.pickup != null) _pickup = _sync.pickup;
        else _pickup = GetComponent<VRC_Pickup>();

        if (_pickup != null && Utilities.IsValid(_localPlayer))
            _pickup.AutoHold = _localPlayer.IsUserInVR()
                ? VRC_Pickup.AutoHoldMode.No
                : VRC_Pickup.AutoHoldMode.Yes;

        if (shuffleIndicator != null)
        {
            _indicatorOriginalScale = shuffleIndicator.localScale;
            shuffleIndicator.localScale = new Vector3(_indicatorOriginalScale.x, _indicatorOriginalScale.y, _indicatorOriginalScale.z);
            shuffleIndicator.gameObject.SetActive(false);
        }
        if (shuffleIndicatorPivot != null)
            shuffleIndicatorPivot.gameObject.SetActive(false);
    }

    private void _BuildCubies()
    {
        if (cubies != null && cubies.Length > 0)
        {
            _cubieCount = cubies.Length;
            if (_cubieCount > CUBIE_COUNT) _cubieCount = CUBIE_COUNT;
            _cubies = new Transform[_cubieCount];
            for (int i = 0; i < _cubieCount; i++) _cubies[i] = cubies[i];
        }
        else
        {
            int childCount = transform.childCount;
            Transform[] candidates = new Transform[childCount];
            int count = 0;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<MeshRenderer>() != null && count < CUBIE_COUNT)
                    candidates[count++] = child;
            }
            _cubieCount = count;
            _cubies = new Transform[_cubieCount];
            for (int i = 0; i < _cubieCount; i++) _cubies[i] = candidates[i];
        }

        _homePos = new Vector3[_cubieCount];
        _homeRot = new Quaternion[_cubieCount];
        _basePos = new Vector3[_cubieCount];
        _baseRot = new Quaternion[_cubieCount];
        for (int i = 0; i < _cubieCount; i++)
        {
            _homePos[i] = _cubies[i].localPosition;
            _homeRot[i] = _cubies[i].localRotation;
        }
    }

    public void _RotateFace(int axis, int layer, int dir)
    {
        if (_isMoving) return;
        if (axis < 0 || axis > 2) return;
        if (layer < -1 || layer > 1) return;
        if (dir != 1 && dir != -1) return;
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        _moveDir = dir;
        _BeginRotation(axis, layer, dir * 90f);
    }

    private void _BeginRotation(int axis, int layer, float targetAngle)
    {
        if (_isMoving) return;
        _moveAxis = axis;
        _moveLayer = layer;
        _moveAngle = 0f;
        _moveTarget = targetAngle;
        _moveSpeed = targetAngle > 0f ? STEP_ANGLE : -STEP_ANGLE;
        _CaptureBases(axis, layer);

        _isMoving = true;
        _RotationStep();
    }

    private void _CaptureBases(int axis, int layer)
    {
        for (int i = 0; i < _cubieCount; i++)
        {
            _basePos[i] = _cubies[i].localPosition;
            _baseRot[i] = _cubies[i].localRotation;
            _inLayer[i] = Mathf.RoundToInt(_AxisComponent(_cubies[i].localPosition, axis)) == layer;
        }
    }

    public void _RotationStep()
    {
        float remaining = _moveTarget - _moveAngle;
        float step = Mathf.Abs(remaining) < STEP_ANGLE ? remaining : _moveSpeed;
        _moveAngle += step;
        _ApplyRotation();

        if (Mathf.Abs(_moveTarget - _moveAngle) < 0.01f)
        {
            _FinishRotation();
            return;
        }
        SendCustomEventDelayedFrames(nameof(_RotationStep), 1);
    }

    private void _ApplyRotation()
    {
        Vector3 axis = _GetAxis(_moveAxis);
        Quaternion q = Quaternion.AngleAxis(_moveAngle, axis);
        // While twisting, push the selected layer out along its axis so it is
        // obvious which slice the gesture grabbed. The offset is parallel to the
        // rotation axis, so it survives the turn unchanged and vanishes as soon
        // as the gesture ends.
        Vector3 lift = _gestureActive ? axis * (_moveLayer * layerLiftAmount) : Vector3.zero;
        for (int i = 0; i < _cubieCount; i++)
        {
            if (!_inLayer[i]) continue;
            _cubies[i].localPosition = q * _basePos[i] + lift;
            _cubies[i].localRotation = q * _baseRot[i];
        }
    }

    private void _FinishRotation()
    {
        _SnapTransforms();
        _isMoving = false;

        if (Networking.IsOwner(gameObject))
        {
            if (Mathf.Abs(_moveTarget) > 0.5f)
            {
                _WriteState();
                RequestSerialization();
            }
        }
        else
        {
            _ApplyState();
        }

        if (_shuffleActive && Networking.IsOwner(gameObject))
            _ShuffleNextMove();
    }

    public void _StartShuffle()
    {
        if (_shuffleActive) return;
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        _shuffleActive = true;
        _shuffleRemaining = shuffleMoveCount;
        if (!_isMoving) _ShuffleNextMove();
    }

    private void _ShuffleNextMove()
    {
        if (!_shuffleActive) return;
        if (_shuffleRemaining <= 0)
        {
            _shuffleActive = false;
            return;
        }
        _shuffleRemaining--;
        int axis = Random.Range(0, 3);
        int layer = Random.Range(-1, 2);
        int dir = Random.Range(0, 2) == 0 ? -1 : 1;
        _RotateFace(axis, layer, dir);
    }

    private void _WriteState()
    {
        _lastMoveAxis = _moveAxis;
        _lastMoveLayer = _moveLayer;
        _lastMoveDir = _moveDir;
        for (int i = 0; i < _cubieCount; i++)
        {
            _cubiePos[i] = _cubies[i].localPosition;
            _cubieRot[i] = _cubies[i].localRotation;
        }
        _moveCounter = _moveCounter + 1;
        _hasReceivedState = true;
        _appliedCounter = _moveCounter;
    }

    private void _ApplyState()
    {
        for (int i = 0; i < _cubieCount; i++)
        {
            _cubies[i].localPosition = _cubiePos[i];
            _cubies[i].localRotation = _cubieRot[i];
        }
    }

    private void _SnapTransforms()
    {
        for (int i = 0; i < _cubieCount; i++)
        {
            Vector3 p = _cubies[i].localPosition;
            p.x = Mathf.Round(p.x);
            p.y = Mathf.Round(p.y);
            p.z = Mathf.Round(p.z);
            _cubies[i].localPosition = p;
            _cubies[i].localRotation = _SnapQuaternion(_cubies[i].localRotation);
        }
    }

    private Quaternion _SnapQuaternion(Quaternion q)
    {
        return new Quaternion(
            _SnapComponent(q.x),
            _SnapComponent(q.y),
            _SnapComponent(q.z),
            _SnapComponent(q.w)
        );
    }

    private float _SnapComponent(float v)
    {
        if (v > 0.85f) return 1f;
        if (v < -0.85f) return -1f;
        if (v > 0.35f) return 0.70710678f;
        if (v < -0.35f) return -0.70710678f;
        return 0f;
    }

    private Vector3 _GetAxis(int axis)
    {
        if (axis == 0) return Vector3.right;
        if (axis == 1) return Vector3.up;
        return Vector3.forward;
    }

    private float _AxisComponent(Vector3 v, int axis)
    {
        if (axis == 0) return v.x;
        if (axis == 1) return v.y;
        return v.z;
    }

    public override void OnDeserialization()
    {
        if (!_hasReceivedState)
        {
            _hasReceivedState = true;
            _appliedCounter = _moveCounter;
            if (_moveCounter > 0) _ApplyState();
            return;
        }
        if (_moveCounter != _appliedCounter)
        {
            _appliedCounter = _moveCounter;
            if (!_isMoving)
                _BeginRotation(_lastMoveAxis, _lastMoveLayer, _lastMoveDir * 90f);
            else
                _ApplyState();
        }
    }

    public override void OnPickup()
    {
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        _SetPickupable(false);
    }

    public override void OnDrop()
    {
        _EndRotateGesture();
        _SetPickupable(true);
    }

    /// <summary>
    /// Setting VRCPickup.pickupable directly is not enough: SmartObjectSync
    /// re-applies `pickup.pickupable = pickupable &amp;&amp; allowTheftFromSelf` when the
    /// object enters a held state, which is what let the free hand rip the cube
    /// out of the holding hand. Route through SmartObjectSync so the value sticks.
    /// </summary>
    private void _SetPickupable(bool value)
    {
        if (_sync != null) _sync.pickupable = value;
        else if (_pickup != null) _pickup.pickupable = value;
    }

    public override void OnPickupUseDown()
    {
        _useHeld = true;
        _useShuffleFired = false;
        _useHoldTime = 0f;
    }

    public override void OnPickupUseUp()
    {
        if (!_useHeld) return;
        _useHeld = false;
        _HideIndicator();
        if (!_useShuffleFired && _useHoldTime <= tapSeconds)
            _ToggleCanvas();
    }

    public void _ToggleCanvas()
    {
        if (canvas != null)
            canvas.SetActive(!canvas.activeSelf);
    }

    /// <summary>
    /// Grip on the hand that is *not* holding the cube starts/ends a twist.
    /// This is a raw input event, so it fires even though the cube itself is
    /// locked non-pickupable and cannot be grabbed by that hand.
    /// </summary>
    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        if (!twistGesture) return;
        if (!Utilities.IsValid(_localPlayer) || !_localPlayer.IsUserInVR()) return;
        if (_pickup == null) return;

        VRC_Pickup.PickupHand holdHand = _pickup.currentHand;
        if (holdHand == VRC_Pickup.PickupHand.None) return;
        if (_localPlayer.GetPickupInHand(holdHand) != _pickup) return;

        VRC_Pickup.PickupHand grabHand = args.handType == HandType.LEFT
            ? VRC_Pickup.PickupHand.Left
            : VRC_Pickup.PickupHand.Right;
        if (grabHand == holdHand) return;

        if (value) _BeginRotateGesture(grabHand);
        else _EndRotateGesture();
    }

    private void _BeginRotateGesture(VRC_Pickup.PickupHand hand)
    {
        if (_gestureActive) return;
        if (_isMoving) return;

        VRCPlayerApi.TrackingDataType trackType = hand == VRC_Pickup.PickupHand.Left
            ? VRCPlayerApi.TrackingDataType.LeftHand
            : VRCPlayerApi.TrackingDataType.RightHand;

        VRCPlayerApi.TrackingData td = _localPlayer.GetTrackingData(trackType);
        Vector3 localPos = transform.InverseTransformPoint(td.position);

        // Ignore grips that are not aimed at the cube at all.
        float dist = localPos.magnitude;
        if (dist < twistMinDistance || dist > twistMaxDistance) return;

        float absX = Mathf.Abs(localPos.x);
        float absY = Mathf.Abs(localPos.y);
        float absZ = Mathf.Abs(localPos.z);

        int axis;
        float dominantVal;
        if (absX >= absY && absX >= absZ)
        {
            axis = 0;
            dominantVal = localPos.x;
        }
        else if (absY >= absX && absY >= absZ)
        {
            axis = 1;
            dominantVal = localPos.y;
        }
        else
        {
            axis = 2;
            dominantVal = localPos.z;
        }

        int layer = Mathf.Clamp(Mathf.RoundToInt(dominantVal), -1, 1);
        if (layer == 0) layer = dominantVal >= 0f ? 1 : -1;

        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(_localPlayer, gameObject);

        _gestureAxis = axis;
        _gestureLayer = layer;
        _gestureAngle = 0f;
        _gestureDetent = 0;
        _gestureHand = hand;
        _gestureTrack = trackType;
        _gesturePrevRot = _LocalHandRot(td);
        _gestureActive = true;

        _moveAxis = axis;
        _moveLayer = layer;
        _moveAngle = 0f;
        _CaptureBases(axis, layer);
        _isMoving = true;
        _ApplyRotation();
        _Haptic(0.4f);
    }

    private void _UpdateRotateGesture()
    {
        if (!_gestureActive) return;
        if (_pickup == null || _pickup.currentHand == VRC_Pickup.PickupHand.None)
        {
            _EndRotateGesture();
            return;
        }

        VRCPlayerApi.TrackingData td = _localPlayer.GetTrackingData(_gestureTrack);
        Quaternion rot = _LocalHandRot(td);
        Quaternion delta = rot * Quaternion.Inverse(_gesturePrevRot);
        _gesturePrevRot = rot;

        _gestureAngle = Mathf.Clamp(
            _gestureAngle + _TwistAngle(delta, _GetAxis(_gestureAxis)), -180f, 180f);

        // Detents are felt, not seen: nudging the displayed angle toward one
        // would jump ~30 degrees every time the nearest detent changes. The
        // layer tracks the wrist 1:1 and only snaps once the grip is released.
        int detent = Mathf.RoundToInt(_gestureAngle / 90f);
        if (detent != _gestureDetent)
        {
            _gestureDetent = detent;
            _Haptic(1f);
        }

        _moveAngle = _gestureAngle;
        _ApplyRotation();
    }

    private void _EndRotateGesture()
    {
        if (!_gestureActive) return;
        _gestureActive = false;
        if (!_isMoving) return;

        _moveTarget = Mathf.Round(_gestureAngle / 90f) * 90f;
        // Quarter-turn count, not just a sign: a 180 degree twist has to replay
        // as 180 degrees on remote clients too.
        _moveDir = Mathf.RoundToInt(_moveTarget / 90f);

        _ApplyRotation(); // drop the layer lift before animating to the snap

        if (Mathf.Abs(_moveTarget - _moveAngle) < 0.01f)
        {
            _FinishRotation();
            return;
        }

        _moveSpeed = _moveTarget > _moveAngle ? STEP_ANGLE : -STEP_ANGLE;
        _RotationStep();
    }

    private Quaternion _LocalHandRot(VRCPlayerApi.TrackingData td)
    {
        return Quaternion.Inverse(transform.rotation) * td.rotation;
    }

    /// <summary>
    /// Swing-twist decomposition: the component of `delta` about `axis`, in
    /// degrees. Robust regardless of how the wrist is oriented, unlike
    /// projecting a single reference vector which degenerates when that vector
    /// lines up with the axis.
    /// </summary>
    private float _TwistAngle(Quaternion delta, Vector3 axis)
    {
        float d = delta.x * axis.x + delta.y * axis.y + delta.z * axis.z;
        float angle = 2f * Mathf.Atan2(d, delta.w) * Mathf.Rad2Deg;
        if (angle > 180f) angle -= 360f;
        else if (angle < -180f) angle += 360f;
        return angle;
    }

    private void _Haptic(float amplitude)
    {
        if (!twistHaptics) return;
        if (_gestureHand == VRC_Pickup.PickupHand.None) return;
        if (!Utilities.IsValid(_localPlayer)) return;
        _localPlayer.PlayHapticEventInHand(_gestureHand, 0.05f, amplitude, 100f);
    }

    public bool IsSolved()
    {
        for (int i = 0; i < _cubieCount; i++)
        {
            Vector3 p = _cubies[i].localPosition;
            if (Mathf.RoundToInt(p.x) != Mathf.RoundToInt(_homePos[i].x)) return false;
            if (Mathf.RoundToInt(p.y) != Mathf.RoundToInt(_homePos[i].y)) return false;
            if (Mathf.RoundToInt(p.z) != Mathf.RoundToInt(_homePos[i].z)) return false;

            Quaternion r = _cubies[i].localRotation;
            Quaternion h = _homeRot[i];
            float dot = r.x * h.x + r.y * h.y + r.z * h.z + r.w * h.w;
            if (dot < 0.99f) return false;
        }
        return true;
    }

    void Update()
    {
        _UpdateRotateGesture();

        if (_useHeld && !_useShuffleFired)
        {
            _useHoldTime += Time.deltaTime;
            float t = Mathf.Clamp01(_useHoldTime / shuffleHoldSeconds);
            if (_useHoldTime >= indicatorDelay)
                _ShowIndicator(t);
            if (_useHoldTime >= shuffleHoldSeconds)
            {
                _useShuffleFired = true;
                _StartShuffle();
                _HideIndicator();
            }
        }

        if (!keyboardControls) return;

        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Keypad8)) _RotateFace(1, 1, 1);
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.Keypad2)) _RotateFace(1, 1, -1);
        if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.Keypad9)) _RotateFace(0, 1, 1);
        if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.Keypad3)) _RotateFace(0, 1, -1);

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Keypad4)) _RotateFace(1, -1, 1);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Keypad6)) _RotateFace(1, -1, -1);
        if (Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.Keypad7)) _RotateFace(0, -1, 1);
        if (Input.GetKeyDown(KeyCode.End) || Input.GetKeyDown(KeyCode.Keypad1)) _RotateFace(0, -1, -1);

        if (Input.GetKeyDown(KeyCode.Insert) || Input.GetKeyDown(KeyCode.Keypad0)) _RotateFace(2, -1, 1);
        if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.KeypadPeriod)) _RotateFace(2, -1, -1);
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) _RotateFace(2, 1, 1);
        if (Input.GetKeyDown(KeyCode.Slash) || Input.GetKeyDown(KeyCode.KeypadDivide)) _RotateFace(2, 1, -1);

        if (Input.GetKey(shuffleKey))
        {
            if (!_keyShuffleFired)
            {
                _keyHoldTime += Time.deltaTime;
                _ShowIndicator(Mathf.Clamp01(_keyHoldTime / shuffleHoldSeconds));
                if (_keyHoldTime >= shuffleHoldSeconds)
                {
                    _keyShuffleFired = true;
                    _StartShuffle();
                    _HideIndicator();
                }
            }
        }
        else
        {
            _keyShuffleFired = false;
            _keyHoldTime = 0f;
            if (!_useHeld)
                _HideIndicator();
        }
    }

    private void _ShowIndicator(float t)
    {
        if (shuffleIndicator != null)
        {
            shuffleIndicator.gameObject.SetActive(true);
            float x = Mathf.Lerp(_indicatorOriginalScale.x, 0f, t);
            shuffleIndicator.localScale = new Vector3(x, _indicatorOriginalScale.y, _indicatorOriginalScale.z);
        }
        if (shuffleIndicatorPivot != null)
        {
            shuffleIndicatorPivot.gameObject.SetActive(true);
            VRCPlayerApi.TrackingData head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            shuffleIndicatorPivot.LookAt(head.position, Vector3.up);
        }
    }

    private void _HideIndicator()
    {
        if (shuffleIndicator != null)
        {
            shuffleIndicator.localScale = _indicatorOriginalScale;
            shuffleIndicator.gameObject.SetActive(false);
        }
        if (shuffleIndicatorPivot != null)
            shuffleIndicatorPivot.gameObject.SetActive(false);
    }
}
