using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace RBS.SleepKit2.Udon
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ClockDisplay : UdonSharpBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI dateText; // 日付表示用

        [SerializeField]
        private TextMeshProUGUI timeText; // 時刻表示用

        // 曜日の日本語表記
        private readonly string[] japaneseWeekdays = { "日", "月", "火", "水", "木", "金", "土" };

        private void Update()
        {
            DateTime now = DateTime.Now;

            // 日付: 例) 1月1日(月)
            string formattedDate = string.Format(
                "{0}月{1}日({2})",
                now.Month,
                now.Day,
                japaneseWeekdays[(int)now.DayOfWeek]
            );
            // 時刻: 例) 01:01
            string formattedTime = string.Format("{0:D2}:{1:D2}", now.Hour, now.Minute);

            if (dateText != null)
            {
                dateText.text = formattedDate;
            }

            if (timeText != null)
            {
                timeText.text = formattedTime;
            }
        }
    }
}
