// using VRC.Udon; // UdonBehaviourはUdonSharpBehaviourに含まれる
using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
// using VRC.SDK3.Persistence; // PlayerDataはManager側で扱うため不要に
using VRC.SDKBase;

namespace RBS.SleepKit2.Udon
{
    public class AlarmManager : UdonSharpBehaviour
    {
        #region Inspector Variables

        [Header("Manager Reference")]
        [Tooltip("SleepKitSettingsManagerへの参照")]
        public SleepKitSettingsManager settingsManager;

        [Header("Alarm Settings")]
        [Tooltip("アラームをON/OFFするボタン (複数可)")]
        public Button[] alarmToggleButtons;

        [Tooltip("アラーム切り替えボタン上のテキスト (複数可)")]
        public TMP_Text[] alarmToggleButtonTexts;

        [Tooltip("+1時間設定ボタン (複数可)")]
        public Button[] add1HourButtons;

        [Tooltip("-1時間設定ボタン (複数可)")]
        public Button[] subtract1HourButtons;

        [Tooltip("+5分設定ボタン (複数可)")]
        public Button[] add5MinutesButtons;

        [Tooltip("-5分設定ボタン (複数可)")]
        public Button[] subtract5MinutesButtons;

        [Tooltip("アラーム時刻の表示形式を切り替えるボタン (複数可)")]
        public Button[] toggleTimeFormatButtons;

        [Tooltip("何時間何分後に鳴るかを表示するTMP_Text (複数可)")]
        public TMP_Text[] timeRemainingTexts;

        [Tooltip("何時何分に鳴るかを表示するTMP_Text (複数可)")]
        public TMP_Text[] alarmTimeTexts;

        [Tooltip("AM設定ボタン (複数可)")]
        public Button[] setAMButtons;

        [Tooltip("PM設定ボタン (複数可)")]
        public Button[] setPMButtons;

        [Tooltip("AM設定ボタンのテキスト (複数可)")]
        public TMP_Text[] setAMButtonTexts;

        [Tooltip("PM設定ボタンのテキスト (複数可)")]
        public TMP_Text[] setPMButtonTexts;

        [Tooltip("AM/PM表示用TMP_Text (複数可)")]
        public TMP_Text[] amPmTexts;

        // alarmAudioSources を削除

        [Header("Toast Settings")]
        [Tooltip("ToastManagerへの参照")]
        public ToastManager toastManager; // これはアラーム鳴動時のToast表示用？ 残すか確認

        [Header("AM/PM ボタンの色を参照するテキスト")]
        [Tooltip("AM/PM ボタンのハイライト色を取得する際に参照する TMP_Text コンポーネント")]
        public TMP_Text referenceTextForColor;

        // PersistenceManagerはManager側で管理するため削除

        #endregion

        #region Constants

        // PersistenceキーはManager側で管理

        #endregion

        #region Private Fields

        // 設定値はManagerから取得するため、ローカル変数は不要
        private bool isAlarmRinging = false; // アラーム鳴動中フラグはUI表示のために残す
        #endregion

        #region Unity Methods

        void Start()
        {
            ValidateUIElements();
            if (Utilities.IsValid(settingsManager))
            {
                settingsManager.RegisterAlarmManager(this);
                // 初期UI更新はManagerからの通知イベントで行う
            }
            else
            {
                Debug.LogError("[AlarmManager] SleepKitSettingsManagerが設定されていません！");
                // 必要であれば、Managerが見つからない場合のフォールバック処理
            }
        }

        // Updateメソッドを復活させ、残り時間更新処理を追加
        void Update()
        {
            if (Utilities.IsValid(settingsManager) && settingsManager.IsAlarmOn())
            {
                UpdateTimeRemaining();
            }
        }

        // OnPlayerRestoredはManager側で処理するため不要

        void OnDestroy()
        {
            if (Utilities.IsValid(settingsManager))
            {
                settingsManager.UnregisterAlarmManager(this);
            }
        }

        #endregion

        #region Public UI Event Handlers

        /// <summary>
        /// アラームのON/OFFを切り替えます。
        /// </summary>
        public void ToggleAlarm()
        {
            if (!Utilities.IsValid(settingsManager))
                return;

            bool currentIsAlarmOn = settingsManager.IsAlarmOn();

            if (currentIsAlarmOn || isAlarmRinging) // 鳴動中も停止できるように変更
            {
                StopAlarm(); // StopAlarm内でManagerに通知
            }
            else
            {
                DateTime currentAlarmTime = settingsManager.GetAlarmTriggerDateTime();
                // アラーム時刻が過去の場合は、朝9時に補正します。
                if (currentAlarmTime <= DateTime.Now)
                {
                    DateTime now = DateTime.Now;
                    DateTime todayNine = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0);
                    currentAlarmTime = now < todayNine ? todayNine : todayNine.AddDays(1);
                    settingsManager.SetAlarmTriggerDateTime(currentAlarmTime); // 補正した時刻を設定
                }
                settingsManager.SetIsAlarmOn(true); // ManagerにON状態を設定
                // UI更新はManagerからの通知で行われる
            }
        }

        /// <summary>
        /// +1時間ボタン用のメソッド
        /// </summary>
        public void OnAdd1Hour() => AdjustAlarmTime(1, 0);

        /// <summary>
        /// -1時間ボタン用のメソッド
        /// </summary>
        public void OnSubtract1Hour() => AdjustAlarmTime(-1, 0);

        /// <summary>
        /// +5分ボタン用のメソッド
        /// </summary>
        public void OnAdd5Minutes() => AdjustAlarmTime(0, 5);

        /// <summary>
        /// -5分ボタン用のメソッド
        /// </summary>
        public void OnSubtract5Minutes() => AdjustAlarmTime(0, -5);

        /// <summary>
        /// 24時間制と12時間制を切り替えます。
        /// </summary>
        public void ToggleTimeFormat()
        {
            if (!Utilities.IsValid(settingsManager))
                return;
            settingsManager.SetIs24HourFormat(!settingsManager.Is24HourFormat());
        }

        /// <summary>
        /// 12時間表示時にAMを設定します。
        /// </summary>
        public void SetAM()
        {
            if (!Utilities.IsValid(settingsManager))
                return;
            bool is24Hour = settingsManager.Is24HourFormat();
            DateTime currentAlarmTime = settingsManager.GetAlarmTriggerDateTime();

            if (!is24Hour && currentAlarmTime != DateTime.MinValue && currentAlarmTime.Hour >= 12)
            {
                settingsManager.SetAlarmTriggerDateTime(currentAlarmTime.AddHours(-12));
            }
        }

        /// <summary>
        /// 12時間表示時にPMを設定します。
        /// </summary>
        public void SetPM()
        {
            if (!Utilities.IsValid(settingsManager))
                return;
            bool is24Hour = settingsManager.Is24HourFormat();
            DateTime currentAlarmTime = settingsManager.GetAlarmTriggerDateTime();

            if (!is24Hour && currentAlarmTime != DateTime.MinValue && currentAlarmTime.Hour < 12)
            {
                settingsManager.SetAlarmTriggerDateTime(currentAlarmTime.AddHours(12));
            }
        }

        /// <summary>
        /// SleepKitSettingsManagerからの設定更新通知を受け取ります。
        /// </summary>
        public void _OnAlarmSettingsUpdate()
        {
            // isAlarmRinging の更新はイベント経由で行うため、ここでは何もしない
            UpdateUI();
        }

        /// <summary>
        /// SleepKitSettingsManagerからアラーム開始通知を受け取ります。
        /// </summary>
        public void _OnAlarmStarted()
        {
            isAlarmRinging = true;
            UpdateUI();
        }

        /// <summary>
        /// SleepKitSettingsManagerからアラーム停止通知を受け取ります。
        /// </summary>
        public void _OnAlarmStopped()
        {
            isAlarmRinging = false;
            UpdateUI();
        }

        #endregion

        #region Private Helper Methods

        // GetPrefixedKeyはManager側で管理するため削除

        /// <summary>
        /// 必須のUI要素がすべてアサインされているか検証します。
        /// </summary>
        private void ValidateUIElements()
        {
            // alarmAudioSources のチェックを削除
            if (
                IsArrayNullOrEmpty(alarmToggleButtons)
                || IsArrayNullOrEmpty(alarmToggleButtonTexts)
                || IsArrayNullOrEmpty(add1HourButtons)
                || IsArrayNullOrEmpty(subtract1HourButtons)
                || IsArrayNullOrEmpty(add5MinutesButtons)
                || IsArrayNullOrEmpty(subtract5MinutesButtons)
                || IsArrayNullOrEmpty(toggleTimeFormatButtons)
                || IsArrayNullOrEmpty(timeRemainingTexts)
                || IsArrayNullOrEmpty(alarmTimeTexts)
                || IsArrayNullOrEmpty(setAMButtons)
                || IsArrayNullOrEmpty(setPMButtons)
                || IsArrayNullOrEmpty(setAMButtonTexts)
                || IsArrayNullOrEmpty(setPMButtonTexts)
                || IsArrayNullOrEmpty(amPmTexts)
            )
            {
                Debug.LogError(
                    "一部のUI要素がアサインされていません。インスペクターで設定してください。"
                );
            }
        }

        /// <summary>
        /// UnityEngine.Object型の配列がnullまたは要素が空かを判定します。
        /// </summary>
        private bool IsArrayNullOrEmpty(UnityEngine.Object[] array)
        {
            return array == null || array.Length == 0;
        }

        /// <summary>
        /// アラーム時刻を指定の時間だけ調整します。
        /// </summary>
        private void AdjustAlarmTime(int hours, int minutes)
        {
            if (!Utilities.IsValid(settingsManager))
                return;

            DateTime now = DateTime.Now;
            bool currentIsAlarmOn = settingsManager.IsAlarmOn();
            DateTime currentAlarmTime = settingsManager.GetAlarmTriggerDateTime();

            if (!currentIsAlarmOn)
            {
                DateTime todayNine = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0);
                currentAlarmTime = now < todayNine ? todayNine : todayNine.AddDays(1);
            }

            // 現在のアラーム時刻の「時刻部分」を取得
            TimeSpan currentTimeOfDay = currentAlarmTime.TimeOfDay;
            // 調整分を加算
            TimeSpan adjustment = new TimeSpan(hours, minutes, 0);
            TimeSpan newTimeOfDay = currentTimeOfDay.Add(adjustment);

            // % 演算子の代わりにループを用いて 24時間内に正規化
            newTimeOfDay = NormalizeTimeSpan(newTimeOfDay);

            // 現在の日付を基準に、新たな時刻を持つ日時候補を作成
            DateTime candidate = new DateTime(
                now.Year,
                now.Month,
                now.Day,
                newTimeOfDay.Hours,
                newTimeOfDay.Minutes,
                newTimeOfDay.Seconds
            );
            // 候補が現在時刻以下なら翌日とする
            if (candidate <= now)
            {
                candidate = candidate.AddDays(1);
            }

            DateTime alarmTriggerDateTime = candidate; // ローカル変数に代入
            settingsManager.SetAlarmTriggerDateTime(alarmTriggerDateTime);
            if (!currentIsAlarmOn)
                settingsManager.SetIsAlarmOn(true); // 時間調整したら自動でONにする
            // UI更新はManagerからの通知で行われる
        }

        /// <summary>
        /// TimeSpan を 0 ～ 24 時間の範囲に正規化します。
        /// </summary>
        private TimeSpan NormalizeTimeSpan(TimeSpan time)
        {
            // 負の値の場合、1日（24時間）ずつ加算
            while (time < TimeSpan.Zero)
            {
                time += TimeSpan.FromDays(1);
            }
            // 24時間以上の場合、1日ずつ減算
            while (time >= TimeSpan.FromDays(1))
            {
                time -= TimeSpan.FromDays(1);
            }
            return time;
        }

        // TriggerAlarmメソッドを削除

        /// <summary>
        /// アラームを停止します。
        /// </summary>
        public void StopAlarm()
        {
            // AudioSource停止処理を削除

            // ManagerにOFFを通知する処理のみ残す
            if (
                Utilities.IsValid(settingsManager)
                && (settingsManager.IsAlarmOn() || isAlarmRinging)
            ) // 鳴動中もOFFにできるように
            {
                settingsManager.SetIsAlarmOn(false); // ManagerにOFF状態を設定
            }
            // isAlarmRinging フラグの更新は Manager 側からの通知で行う想定
        }

        /// <summary>
        /// 残り時間およびアラーム時刻の表示を更新します。
        /// </summary>
        private void UpdateTimeRemaining()
        {
            if (!Utilities.IsValid(settingsManager))
                return;
            DateTime currentAlarmTime = settingsManager.GetAlarmTriggerDateTime();
            if (currentAlarmTime == DateTime.MinValue)
                return; // 時刻未設定なら何もしない

            TimeSpan remaining = currentAlarmTime - DateTime.Now;
            if (remaining.TotalSeconds < 0)
            {
                remaining = TimeSpan.Zero;
            }

            string timeRemainingStr =
                $"アラームまで: {remaining.Hours}時間 {remaining.Minutes}分 {remaining.Seconds}秒";
            foreach (TMP_Text txt in timeRemainingTexts)
            {
                if (txt != null)
                    txt.text = timeRemainingStr;
            }

            string formattedTime = FormatAlarmTime();
            foreach (TMP_Text txt in alarmTimeTexts)
            {
                if (txt != null)
                    txt.text = formattedTime;
            }
        }

        /// <summary>
        /// 現在の表示形式に合わせたアラーム時刻の文字列を返すと同時にAM/PMの値を更新します。
        /// </summary>
        private string FormatAlarmTime()
        {
            if (!Utilities.IsValid(settingsManager))
                return string.Empty;
            DateTime currentAlarmTime = settingsManager.GetAlarmTriggerDateTime();
            bool is24Hour = settingsManager.Is24HourFormat();

            if (currentAlarmTime == DateTime.MinValue)
            {
                return string.Empty;
            }

            string formattedTime = is24Hour
                ? currentAlarmTime.ToString("HH:mm")
                : currentAlarmTime.ToString("hh:mm");

            string amPmValue = is24Hour ? "24H" : currentAlarmTime.ToString("tt");
            foreach (TMP_Text txt in amPmTexts)
            {
                if (txt != null)
                    txt.text = amPmValue;
            }
            return formattedTime;
        }

        /// <summary>
        /// 現在のアラーム状態に応じたUI全体の更新を行います。
        /// </summary>
        private void UpdateUI()
        {
            if (!Utilities.IsValid(settingsManager))
                return;

            bool currentIsAlarmOn = settingsManager.IsAlarmOn();
            DateTime currentAlarmTime = settingsManager.GetAlarmTriggerDateTime();
            bool is24Hour = settingsManager.Is24HourFormat();
            // isAlarmRinging はイベントで更新されるフィールド値を使用

            if (currentIsAlarmOn || isAlarmRinging) // 鳴動中もON状態としてUIを更新
            {
                // アラームON時：ボタンラベルは「停止」または「ON」
                string toggleText = isAlarmRinging ? "停止" : "ON"; // isAlarmRinging の値を使用
                foreach (TMP_Text txt in alarmToggleButtonTexts)
                {
                    if (txt != null)
                        txt.text = toggleText;
                }

                bool interactable = !isAlarmRinging; // isAlarmRinging の値を使用
                SetButtonsInteractable(add1HourButtons, interactable);
                SetButtonsInteractable(subtract1HourButtons, interactable);
                SetButtonsInteractable(add5MinutesButtons, interactable);
                SetButtonsInteractable(subtract5MinutesButtons, interactable);
                SetButtonsInteractable(toggleTimeFormatButtons, interactable);
                SetButtonsInteractable(setAMButtons, interactable && !is24Hour);
                SetButtonsInteractable(setPMButtons, interactable && !is24Hour);

                // 12時間制の場合、現在のAM/PM状態に応じてボタンテキストの色を切替
                if (!is24Hour && currentAlarmTime != DateTime.MinValue)
                {
                    string currentPeriod = currentAlarmTime.ToString("tt");

                    // Inspector でアサインした referenceTextForColor を使う
                    if (referenceTextForColor != null)
                    {
                        // 参照先テキストの color を取得
                        var color = referenceTextForColor.color;
                        Color amColor = currentPeriod.Equals(
                            "AM",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? new Color(color.r, color.g, color.b, 1.0f)
                            : new Color(color.r, color.g, color.b, 0.5f);
                        Color pmColor = currentPeriod.Equals(
                            "PM",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? new Color(color.r, color.g, color.b, 1.0f)
                            : new Color(color.r, color.g, color.b, 0.5f);
                        SetTextColor(setAMButtonTexts, amColor);
                        SetTextColor(setPMButtonTexts, pmColor);
                    }
                    else
                    {
                        Debug.LogWarning(
                            "AlarmManager: referenceTextForColor がアサインされていません。"
                        );
                    }
                }
                else
                {
                    SetTextColor(setAMButtonTexts, Color.white);
                    SetTextColor(setPMButtonTexts, Color.white);
                }

                UpdateTimeRemaining();
            }
            else
            {
                // アラームOFF時
                foreach (TMP_Text txt in alarmToggleButtonTexts)
                {
                    if (txt != null)
                        txt.text = "OFF";
                }
                SetAllAdjustmentButtonsInteractable(true); // OFF時は調整可能にする

                if (currentAlarmTime == DateTime.MinValue)
                {
                    SetTextForArray(timeRemainingTexts, "アラームが設定されていません。");
                    SetTextForArray(alarmTimeTexts, string.Empty);
                    SetTextForArray(amPmTexts, string.Empty);
                }
                else
                {
                    SetTextForArray(timeRemainingTexts, "アラームが停止されています。");
                    string alarmTimeStr = is24Hour
                        ? currentAlarmTime.ToString("HH:mm")
                        : currentAlarmTime.ToString("hh:mm");
                    SetTextForArray(alarmTimeTexts, alarmTimeStr);
                    SetTextForArray(
                        amPmTexts,
                        is24Hour ? string.Empty : currentAlarmTime.ToString("tt")
                    );
                }
            }
        }

        /// <summary>
        /// 各種時刻調整関連ボタンの有効/無効状態を一括設定します。
        /// </summary>
        private void SetAllAdjustmentButtonsInteractable(bool state)
        {
            if (!Utilities.IsValid(settingsManager))
                return;
            bool is24Hour = settingsManager.Is24HourFormat();

            SetButtonsInteractable(add1HourButtons, state);
            SetButtonsInteractable(subtract1HourButtons, state);
            SetButtonsInteractable(add5MinutesButtons, state);
            SetButtonsInteractable(subtract5MinutesButtons, state);
            SetButtonsInteractable(toggleTimeFormatButtons, state);
            SetButtonsInteractable(setAMButtons, state && !is24Hour);
            SetButtonsInteractable(setPMButtons, state && !is24Hour);

            // OFF時も色はデフォルトに戻す
            SetTextColor(setAMButtonTexts, Color.white);
            SetTextColor(setPMButtonTexts, Color.white);
        }

        /// <summary>
        /// 指定したボタン配列の各要素の interactable 状態を設定します。
        /// </summary>
        private void SetButtonsInteractable(Button[] buttons, bool state)
        {
            if (buttons == null)
                return;
            foreach (Button btn in buttons)
            {
                if (btn != null)
                    btn.interactable = state;
            }
        }

        /// <summary>
        /// 指定したテキスト配列の各要素の色を設定します。
        /// </summary>
        private void SetTextColor(TMP_Text[] texts, Color color)
        {
            if (texts == null)
                return;
            foreach (TMP_Text txt in texts)
            {
                if (txt != null)
                    txt.color = color;
            }
        }

        /// <summary>
        /// 指定したテキスト配列の各要素に同じ文字列を設定します。
        /// </summary>
        private void SetTextForArray(TMP_Text[] texts, string text)
        {
            if (texts == null)
                return;
            foreach (TMP_Text txt in texts)
            {
                if (txt != null)
                    txt.text = text;
            }
        }

        // SaveAlarmSettings と LoadAlarmSettings はManager側で処理するため削除

        #endregion
    }
}
