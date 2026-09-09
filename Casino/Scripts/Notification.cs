
using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;
using RBS.SleepKit2.Udon;

public class Notification : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private ToastManager toast;

    private string _currentMessage = "";

    public void Notify(string message)
    {
        if (toast == null) return;
        if (message == _currentMessage) return;

        _currentMessage = message;
        toast.ShowToast("", message);

        SendCustomEventDelayedSeconds(nameof(ResetCurrentMessage), toast.displayDuration + toast.fadeOutDuration);
    }

    public void ResetCurrentMessage()
    {
        _currentMessage = "";
    }
}
