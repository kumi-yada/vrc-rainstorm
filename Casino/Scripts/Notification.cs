
using UdonSharp;
using UnityEngine;
using TMPro;

public class Notification : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject display;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private AudioPool audioPool;
    [SerializeField] private AudioClip notificationClip;

    [Header("Timing")]
    [SerializeField] private float fadeInDuration = 0.25f;
    [SerializeField] private float fadeOutDuration = 0.25f;
    [SerializeField] private float displayDuration = 3f;

    private const int FadeSteps = 10;

    private const int StateIdle = 0;
    private const int StateFadingIn = 1;
    private const int StateVisible = 2;
    private const int StateFadingOut = 3;

    private int _state = StateIdle;
    private float _fadeInStepDelay;
    private float _fadeOutStepDelay;
    private float _fadeInStepAmount;
    private float _fadeOutStepAmount;

    private string _currentMessage = "";

    private void Start()
    {
        _fadeInStepDelay = fadeInDuration / FadeSteps;
        _fadeOutStepDelay = fadeOutDuration / FadeSteps;
        _fadeInStepAmount = 1f / FadeSteps;
        _fadeOutStepAmount = 1f / FadeSteps;

        if (display != null) display.SetActive(false);
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    public void Notify(string message)
    {
        if (display == null || canvasGroup == null || messageText == null) return;
        if (message == _currentMessage) return;

        _currentMessage = message;
        messageText.text = message;
        display.SetActive(true);
        canvasGroup.alpha = 0f;
        _state = StateFadingIn;

        if (audioPool != null && notificationClip != null)
            audioPool.PlayLocal(notificationClip, transform.position);

        SendCustomEventDelayedSeconds(nameof(FadeInStep), _fadeInStepDelay);
    }

    public void FadeInStep()
    {
        if (_state != StateFadingIn) return;

        canvasGroup.alpha += _fadeInStepAmount;
        if (canvasGroup.alpha >= 1f)
        {
            canvasGroup.alpha = 1f;
            _state = StateVisible;
            SendCustomEventDelayedSeconds(nameof(Hide), displayDuration);
        }
        else
        {
            SendCustomEventDelayedSeconds(nameof(FadeInStep), _fadeInStepDelay);
        }
    }

    public void Hide()
    {
        if (_state != StateVisible) return;
        _state = StateFadingOut;
        SendCustomEventDelayedSeconds(nameof(FadeOutStep), _fadeOutStepDelay);
    }

    public void FadeOutStep()
    {
        if (_state != StateFadingOut) return;

        canvasGroup.alpha -= _fadeOutStepAmount;
        if (canvasGroup.alpha <= 0f)
        {
            canvasGroup.alpha = 0f;
            _state = StateIdle;
            if (display != null) display.SetActive(false);
            _currentMessage = "";
        }
        else
        {
            SendCustomEventDelayedSeconds(nameof(FadeOutStep), _fadeOutStepDelay);
        }
    }
}
