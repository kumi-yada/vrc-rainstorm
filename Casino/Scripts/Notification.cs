
using UdonSharp;
using UnityEngine;
using TMPro;

public class Notification : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject display;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private AudioPool audioPool;
    [SerializeField] private AudioClip notificationClip;
    [SerializeField] private float displayDuration = 3f;

    private string _currentMessage = "";

    public void Notify(string message)
    {
        if (display == null || messageText == null) return;
        if (message == _currentMessage) return;

        _currentMessage = message;
        messageText.text = message;
        display.SetActive(true);

        if (audioPool != null && notificationClip != null)
            audioPool.PlayLocal(notificationClip, transform.position);

        SendCustomEventDelayedSeconds(nameof(Hide), displayDuration);
    }

    public void Hide()
    {
        if (display != null) display.SetActive(false);
        _currentMessage = "";
    }
}
