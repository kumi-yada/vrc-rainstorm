using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class IntroCard : UdonSharpBehaviour
{
    [SerializeField] private GameObject canvas;
    [SerializeField] private GameObject spinTarget;
    [SerializeField] private float duration = 0.25f;
    [SerializeField] private float shownScale = 1f;
    [SerializeField] private float hiddenScale = 0f;
    [SerializeField] private float rotationPerSecond = 30f;
    [SerializeField] private float bobAmplitude = 0.05f;
    [SerializeField] private float bobDuration = 2f;

    private VRCTweenHandle _showTween;
    private VRCTweenHandle _hideTween;
    private VRCTweenHandle _hideDelay;
    private VRCTweenHandle _bobTween;

    void Start()
    {
        if (canvas == null)
        {
            canvas = gameObject;
        }
        canvas.SetActive(false);
        canvas.transform.localScale = Vector3.one * hiddenScale;
        _StartFloat();
    }

    void Update()
    {
        Transform t = spinTarget == null ? transform : spinTarget.transform;
        t.Rotate(0f, rotationPerSecond * Time.deltaTime, 0f, Space.World);
    }

    private void _StartFloat()
    {
        _bobTween.Kill();

        Transform t = spinTarget == null ? transform : spinTarget.transform;
        _bobTween = t.TweenPosition(t.position + Vector3.up * bobAmplitude, bobDuration, VRCTweenEase.InOutSine)
            .SetLoops(-1, VRCTweenLoopType.Yoyo);
    }

    public override void Interact()
    {
        if (canvas.activeSelf)
        {
            HideCanvas();
        }
        else
        {
            ShowCanvas();
        }
    }

    public void ShowCanvas()
    {
        _showTween.Kill();
        _hideTween.Kill();
        _hideDelay.Kill();

        canvas.SetActive(true);
        canvas.transform.localScale = Vector3.one * hiddenScale;
        _StartFloat();
        _showTween = canvas.transform.TweenScale(Vector3.one * shownScale, duration, VRCTweenEase.OutCubic);
    }

    public void HideCanvas()
    {
        _showTween.Kill();
        _hideTween.Kill();
        _bobTween.Kill();

        _hideTween = canvas.transform.TweenScale(Vector3.one * hiddenScale, duration, VRCTweenEase.InCubic);
        _hideDelay = VRCTween.DelayedCall(this, nameof(_FinishHide), duration);
    }

    public void _FinishHide()
    {
        canvas.SetActive(false);
    }

    void OnDestroy()
    {
        _showTween.Kill();
        _hideTween.Kill();
        _hideDelay.Kill();
        _bobTween.Kill();
    }
}
