using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Transform-based Rubik's cube. Setup:
/// 1. Root GameObject carries this UdonBehaviour + VRCPickup + a BoxCollider
///    big enough to cover the whole 3x3x3 volume.
/// 2. Add 26 cubie children (cube mesh each, no Rigidbody). Place them on the
///    -1/0/+1 grid, skipping the center (0,0,0). Cubie local position and
///    rotation are read at Start as the solved state.
/// 3. Optionally assign `cubies` in the Inspector; if empty, the script uses
///    the root's children in hierarchy order.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RubikCube : UdonSharpBehaviour
{
    private const int CUBIE_COUNT = 26;
    private const float STEP_ANGLE = 6f;
    private const float GESTURE_MIN_ANGLE = 0.30f;

    [Header("Cubies (optional; falls back to root children)")]
    public Transform[] cubies;

    [Header("Canvas (toggled by holding hand trigger / desktop click)")]
    public GameObject canvas;

    [Header("Shuffle Hold Indicator (scales X 0-1 while holding)")]
    public Transform shuffleIndicatorPivot;
    public Transform shuffleIndicator;

    [Header("Input")]
    public bool keyboardControls = true;

    [Header("Shuffle")]
    public KeyCode shuffleKey = KeyCode.Q;
    public float shuffleHoldSeconds = 2f;
    public int shuffleMoveCount = 20;

    private bool _shuffleActive;
    private int _shuffleRemaining;
    private float _keyHoldTime;
    private bool _keyShuffleFired;

    private VRC_Pickup _pickup;

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

    private bool _hasReceivedState;
    private int _appliedCounter;

    private Vector3 _indicatorOriginalScale;

    private bool _gestureActive;
    private int _gestureAxis;
    private int _gestureLayer;
    private Vector3 _gesturePrevUp;
    private float _gestureAngle;

    [UdonSynced] private Vector3[] _cubiePos = new Vector3[CUBIE_COUNT];
    [UdonSynced] private Quaternion[] _cubieRot = new Quaternion[CUBIE_COUNT];
    [UdonSynced] private int _lastMoveAxis;
    [UdonSynced] private int _lastMoveLayer;
    [UdonSynced] private int _lastMoveDir;
    [UdonSynced] private int _moveCounter;

    void Start()
    {
        _BuildCubies();

        _pickup = GetComponent<VRC_Pickup>();
        if (_pickup != null)
            _pickup.AutoHold = Networking.LocalPlayer.IsUserInVR()
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
        _BeginRotation(axis, layer, dir);
    }

    private void _BeginRotation(int axis, int layer, int dir)
    {
        if (_isMoving) return;
        _moveAxis = axis;
        _moveLayer = layer;
        _moveDir = dir;
        _moveAngle = 0f;

        for (int i = 0; i < _cubieCount; i++)
        {
            _basePos[i] = _cubies[i].localPosition;
            _baseRot[i] = _cubies[i].localRotation;
            _inLayer[i] = Mathf.RoundToInt(_AxisComponent(_cubies[i].localPosition, axis)) == layer;
        }

        _isMoving = true;
        _RotationStep();
    }

    public void _RotationStep()
    {
        _moveAngle += STEP_ANGLE;
        float a = _moveAngle;
        if (a > 90f) a = 90f;

        Quaternion q = Quaternion.AngleAxis(a * _moveDir, _GetAxis(_moveAxis));
        for (int i = 0; i < _cubieCount; i++)
        {
            if (!_inLayer[i]) continue;
            _cubies[i].localPosition = q * _basePos[i];
            _cubies[i].localRotation = q * _baseRot[i];
        }

        if (_moveAngle >= 90f)
        {
            _FinishRotation();
            return;
        }
        SendCustomEventDelayedFrames(nameof(_RotationStep), 1);
    }

    private void _FinishRotation()
    {
        _SnapTransforms();
        _isMoving = false;

        if (Networking.IsOwner(gameObject))
        {
            _WriteState();
            RequestSerialization();
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
                _BeginRotation(_lastMoveAxis, _lastMoveLayer, _lastMoveDir);
            else
                _ApplyState();
        }
    }

    public override void OnPickup()
    {
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (_pickup != null) _pickup.pickupable = false;
    }

    public override void OnDrop()
    {
        if (_pickup != null) _pickup.pickupable = true;
    }

    public override void OnPickupUseDown()
    {
        _ToggleCanvas();
    }

    public void _ToggleCanvas()
    {
        if (canvas != null)
            canvas.SetActive(!canvas.activeSelf);
    }

    private void _PollOtherHandRotate()
    {
        if (_pickup == null) return;
        VRC_Pickup.PickupHand hand = _pickup.currentHand;
        if (hand == VRC_Pickup.PickupHand.None)
        {
            _gestureActive = false;
            return;
        }

        string button = hand == VRC_Pickup.PickupHand.Right
            ? "Oculus_CrossPlatform_PrimaryHandTrigger"
            : "Oculus_CrossPlatform_SecondaryHandTrigger";

        VRCPlayerApi.TrackingDataType trackType = hand == VRC_Pickup.PickupHand.Right
            ? VRCPlayerApi.TrackingDataType.LeftHand
            : VRCPlayerApi.TrackingDataType.RightHand;

        if (Input.GetButtonDown(button))
        {
            _BeginRotateGesture(trackType);
        }
        else if (Input.GetButton(button) && _gestureActive)
        {
            _UpdateRotateGesture(trackType);
        }
        else if (Input.GetButtonUp(button) && _gestureActive)
        {
            _EndRotateGesture();
        }
    }

    private void _BeginRotateGesture(VRCPlayerApi.TrackingDataType trackType)
    {
        if (_gestureActive) return;

        VRCPlayerApi.TrackingData td = Networking.LocalPlayer.GetTrackingData(trackType);
        Vector3 localPos = transform.InverseTransformPoint(td.position);

        if (localPos.magnitude < 0.1f) return;

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

        _gestureAxis = axis;
        _gestureLayer = Mathf.Clamp(Mathf.RoundToInt(dominantVal), -1, 1);
        _gestureAngle = 0f;
        _gesturePrevUp = _LocalHandUp(td);
        _gestureActive = true;
    }

    private void _UpdateRotateGesture(VRCPlayerApi.TrackingDataType trackType)
    {
        VRCPlayerApi.TrackingData td = Networking.LocalPlayer.GetTrackingData(trackType);
        Vector3 up = _LocalHandUp(td);
        Vector3 axis = _GetAxis(_gestureAxis);
        _gestureAngle += _SignedAngle(_gesturePrevUp, up, axis);
        _gesturePrevUp = up;
    }

    private void _EndRotateGesture()
    {
        _gestureActive = false;
        if (Mathf.Abs(_gestureAngle) < GESTURE_MIN_ANGLE) return;
        int dir = _gestureAngle > 0f ? 1 : -1;
        _RotateFace(_gestureAxis, _gestureLayer, dir);
    }

    private Vector3 _LocalHandUp(VRCPlayerApi.TrackingData td)
    {
        return transform.InverseTransformDirection(td.rotation * Vector3.up);
    }

    private float _SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
    {
        Vector3 fromP = from - axis * Vector3.Dot(from, axis);
        Vector3 toP = to - axis * Vector3.Dot(to, axis);
        return Mathf.Atan2(Vector3.Dot(axis, Vector3.Cross(fromP, toP)), Vector3.Dot(fromP, toP));
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
        _PollOtherHandRotate();

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
                if (shuffleIndicator != null)
                {
                    shuffleIndicator.gameObject.SetActive(true);
                    float t = Mathf.Clamp01(_keyHoldTime / shuffleHoldSeconds);
                    float x = Mathf.Lerp(_indicatorOriginalScale.x, 0f, t);
                    shuffleIndicator.localScale = new Vector3(x, _indicatorOriginalScale.y, _indicatorOriginalScale.z);
                }
                if (shuffleIndicatorPivot != null)
                {
                    shuffleIndicatorPivot.gameObject.SetActive(true);
                    VRCPlayerApi.TrackingData head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    shuffleIndicatorPivot.LookAt(head.position, Vector3.up);
                }
                if (_keyHoldTime >= shuffleHoldSeconds)
                {
                    _keyShuffleFired = true;
                    _StartShuffle();
                }
            }
        }
        else
        {
            _keyShuffleFired = false;
            _keyHoldTime = 0f;
            if (shuffleIndicator != null)
            {
                shuffleIndicator.localScale = _indicatorOriginalScale;
                shuffleIndicator.gameObject.SetActive(false);
            }
            if (shuffleIndicatorPivot != null)
                shuffleIndicatorPivot.gameObject.SetActive(false);
        }
    }
}
