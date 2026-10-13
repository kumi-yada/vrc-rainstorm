using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// Shows the local player's system time, updating live. Purely local: every
// player sees their own timezone, so no sync is needed.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Clock : UdonSharpBehaviour
{
    [Tooltip("Text label that displays the current local time.")]
    public TextMeshProUGUI clockText;

    [Tooltip("Show seconds in addition to hours and minutes.")]
    public bool showSeconds = false;

    [Tooltip("Use 12-hour format with AM/PM instead of 24-hour.")]
    public bool use12Hour = false;

    [Tooltip("Show the date on a line above the time.")]
    public bool showDate = false;

    private float _nextUpdateTime;

    private void Update()
    {
        if (Time.time < _nextUpdateTime)
        {
            return;
        }

        _nextUpdateTime = Time.time + 1f;

        if (clockText == null)
        {
            return;
        }

        clockText.text = _BuildClock();
    }

    private string _BuildClock()
    {
        DateTime now = DateTime.Now;

        int hour = now.Hour;
        string suffix = "";

        if (use12Hour)
        {
            suffix = hour >= 12 ? " PM" : " AM";
            hour = hour % 12;
            if (hour == 0)
            {
                hour = 12;
            }
        }

        string time = _Pad(hour) + ":" + _Pad(now.Minute);
        if (showSeconds)
        {
            time += ":" + _Pad(now.Second);
        }

        time += suffix;

        if (showDate)
        {
            time = now.Year + "-" + _Pad(now.Month) + "-" + _Pad(now.Day) + "\n" + time;
        }

        return time;
    }

    private string _Pad(int value)
    {
        return value < 10 ? "0" + value : value.ToString();
    }
}
