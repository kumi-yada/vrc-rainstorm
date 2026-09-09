
using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;

public class Notification : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private TextMeshProUGUI textField;

    [Header("Settings")]
    [SerializeField] private float hideTimeout = 3f;
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float targetOffsetY = -0.2f;
    [SerializeField] private float startOffsetY = -0.6f;
    [SerializeField] private float distanceZ = 0.5f;

    private float _hideTime = -1f;
    private bool _isAnimating;
    private bool _isHiding;
    private Vector3 _targetPos;
    private Vector3 _hideTargetPos;
    private string _currentMessage = "";

    void Start()
    {
        if (canvasRect != null)
        {
            canvasRect.gameObject.SetActive(false);
        }
    }

    public void Notify(string message)
    {
        if (canvasRect == null || textField == null) return;
        if (canvasRect.gameObject.activeSelf && message == _currentMessage) return;

        _currentMessage = message;
        textField.text = message;
        canvasRect.gameObject.SetActive(true);

        canvasRect.localPosition = new Vector3(0f, startOffsetY, distanceZ);
        _targetPos = new Vector3(0f, targetOffsetY, distanceZ);

        _isAnimating = true;
        _hideTime = Time.time + hideTimeout;
    }

    void Update()
    {
        if (_isAnimating)
        {
            canvasRect.localPosition = Vector3.Lerp(
                canvasRect.localPosition,
                _targetPos,
                Time.deltaTime * moveSpeed
            );

            if (Vector3.Distance(canvasRect.localPosition, _targetPos) < 0.01f)
            {
                canvasRect.localPosition = _targetPos;
                _isAnimating = false;
            }
        }

        if (_hideTime > 0f && Time.time >= _hideTime)
        {
            _hideTime = -1f;
            _isHiding = true;
            _hideTargetPos = new Vector3(0f, startOffsetY, distanceZ);
        }

        if (_isHiding)
        {
            canvasRect.localPosition = Vector3.Lerp(
                canvasRect.localPosition,
                _hideTargetPos,
                Time.deltaTime * moveSpeed
            );

            if (Vector3.Distance(canvasRect.localPosition, _hideTargetPos) < 0.01f)
            {
                canvasRect.localPosition = _hideTargetPos;
                _isHiding = false;
                _currentMessage = "";
                canvasRect.gameObject.SetActive(false);
            }
        }
    }
}
