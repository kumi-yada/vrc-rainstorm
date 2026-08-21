using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace RBS.SleepKit2.Udon
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)] // ローカル動作なので None
    public class SleepKitSettingsManager : UdonSharpBehaviour
    {
        [Header("Join Toast Manager Reference")] // ヘッダー名を変更
        [Tooltip("シーンに1つだけ存在するJoinToastManagerへの参照")]
        public JoinToastManager joinToastManager; // ToastManagerSettingsからJoinToastManagerに変更

        [Header("Alarm Audio")] // アラーム音関連のヘッダーを追加
        [Tooltip("アラーム音を再生するAudioSource")]
        public AudioSource alarmAudioSource; // AudioSourceへの参照を追加

        [Header("Night Mode Mesh")]
        [Tooltip("シーンに1つだけ配置するナイトモード用メッシュのRenderer")]
        public Renderer nightModeRenderer;

        [Tooltip("ナイトモードマテリアルの値プロパティ名")]
        public string nightModePropertyName = "_NightMode"; // NightModeManagerに合わせてデフォルト値設定

        // --- 定数 (固定キーに変更) ---
        private const string AlarmTimeKey = "RBS_SLK2_SHARED_ALARM_TIME";
        private const string AlarmOnKey = "RBS_SLK2_SHARED_ALARM_IS_ON";
        private const string TimeFormatKey = "RBS_SLK2_SHARED_ALARM_TIME_FORMAT";
        private const string NightModeValueKey = "RBS_SLK2_SHARED_NIGHT_MODE_VALUE";

        // --- 設定値 ---
        private DateTime _alarmTriggerDateTime = DateTime.MinValue;
        private bool _isAlarmOn = false;
        private bool _is24HourFormat = true;
        private float _nightModeValue = 1.0f; // デフォルト値を1に変更

        // --- Manager参照リスト (配列とカウンターで管理) ---
        private UdonSharpBehaviour[] _alarmManagers = new UdonSharpBehaviour[10]; // 初期サイズ10
        private int _alarmManagerCount = 0;
        private UdonSharpBehaviour[] _nightModeManagers = new UdonSharpBehaviour[10]; // 初期サイズ10
        private int _nightModeManagerCount = 0;

        // --- イベント名 ---
        private const string AlarmSettingsUpdateEvent = "_OnAlarmSettingsUpdate";
        private const string NightModeUpdateEvent = "_OnNightModeUpdate";
        private const string AlarmStartedEvent = "_OnAlarmStarted"; // アラーム開始イベント名
        private const string AlarmStoppedEvent = "_OnAlarmStopped"; // アラーム停止イベント名

        // --- アラームチェック用 ---
        private bool _isAlarmPlaying = false; // アラーム再生中フラグ

        void Start()
        {
            LoadSettings();
            // 初回ロード時のマテリアル更新
            UpdateNightModeMaterial();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            LoadSettings();
            UpdateNightModeMaterial(); // 復元時もマテリアル更新
            NotifyAlarmSettingsChanged();
            NotifyNightModeChanged();
        }

        // --- アラーム時刻チェック ---
        void Update()
        {
            // アラームがONで、まだ再生中でなく、設定時刻を過ぎていたら
            if (_isAlarmOn && !_isAlarmPlaying && _alarmTriggerDateTime <= DateTime.Now)
            {
                // アラーム時刻がMinValueでないことも確認（初期状態など）
                if (_alarmTriggerDateTime != DateTime.MinValue)
                {
                    PlayAlarm();
                }
                else
                {
                    // 時刻未設定でONになっている場合などはOFFにする
                    SetIsAlarmOn(false);
                }
            }
        }

        private void PlayAlarm()
        {
            Debug.Log("[SleepKitSettingsManager] Playing Alarm!");
            _isAlarmPlaying = true; // 再生中フラグを立てる
            NotifyAlarmStarted(); // AlarmManagerに通知

            if (Utilities.IsValid(alarmAudioSource))
            {
                alarmAudioSource.Play();
            }
            else
            {
                Debug.LogError("[SleepKitSettingsManager] alarmAudioSourceが設定されていません！");
            }

            // アラーム再生時に自動でOFFにする処理は削除済み
            // SendCustomEventDelayedSeconds(nameof(ResetAlarmPlayingFlag), 1f); // 遅延でのフラグ解除を削除
        }

        public void ResetAlarmPlayingFlag()
        {
            // このメソッドが呼ばれるのは手動停止時のみになったので、
            // _isAlarmPlaying のチェックは不要かもしれないが念のため残す
            if (_isAlarmPlaying)
            {
                _isAlarmPlaying = false;
                NotifyAlarmStopped(); // AlarmManagerに通知
            }
        }

        // --- Player Join/Leave Events ---
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // joinToastManager を使用するように修正
            if (Utilities.IsValid(player) && !player.isLocal && Utilities.IsValid(joinToastManager))
            {
                if (joinToastManager.IsToastEnabled()) // JoinToastManagerのメソッドを使用
                {
                    ToastManager tm = joinToastManager.GetToastManager(); // JoinToastManager経由で取得
                    if (Utilities.IsValid(tm))
                    {
                        tm.ShowToast("Player Joined", player.displayName + " has joined.");
                    }
                }
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // joinToastManager を使用するように修正
            if (Utilities.IsValid(player) && !player.isLocal && Utilities.IsValid(joinToastManager))
            {
                if (joinToastManager.IsToastEnabled()) // JoinToastManagerのメソッドを使用
                {
                    ToastManager tm = joinToastManager.GetToastManager(); // JoinToastManager経由で取得
                    if (Utilities.IsValid(tm))
                    {
                        tm.ShowToast("Player Left", player.displayName + " has left.");
                    }
                }
            }
        }

        // --- Manager登録/解除 ---
        public void RegisterAlarmManager(UdonSharpBehaviour manager)
        {
            if (!Utilities.IsValid(manager))
                return;

            // 既に登録されていないか確認
            for (int i = 0; i < _alarmManagerCount; i++)
            {
                if (_alarmManagers[i] == manager)
                    return; // 既に登録済み
            }

            // 配列サイズチェックと拡張
            if (_alarmManagerCount >= _alarmManagers.Length)
            {
                var newArray = new UdonSharpBehaviour[_alarmManagers.Length * 2];
                Array.Copy(_alarmManagers, newArray, _alarmManagerCount);
                _alarmManagers = newArray;
            }
            _alarmManagers[_alarmManagerCount++] = manager;

            // 登録時に現在の設定を通知
            manager.SendCustomEvent(AlarmSettingsUpdateEvent);
        }

        public void UnregisterAlarmManager(UdonSharpBehaviour manager)
        {
            if (!Utilities.IsValid(manager))
                return;
            int foundIndex = -1;
            for (int i = 0; i < _alarmManagerCount; i++)
            {
                if (_alarmManagers[i] == manager)
                {
                    foundIndex = i;
                    break;
                }
            }
            if (foundIndex != -1)
            {
                // 配列から削除 (末尾と入れ替えてカウントを減らす)
                _alarmManagers[foundIndex] = _alarmManagers[_alarmManagerCount - 1];
                _alarmManagers[_alarmManagerCount - 1] = null; // 不要になった末尾をクリア
                _alarmManagerCount--;
            }
        }

        public void RegisterNightModeManager(UdonSharpBehaviour manager)
        {
            if (!Utilities.IsValid(manager))
                return;

            // 既に登録されていないか確認
            for (int i = 0; i < _nightModeManagerCount; i++)
            {
                if (_nightModeManagers[i] == manager)
                    return; // 既に登録済み
            }

            // 配列サイズチェックと拡張
            if (_nightModeManagerCount >= _nightModeManagers.Length)
            {
                var newArray = new UdonSharpBehaviour[_nightModeManagers.Length * 2];
                Array.Copy(_nightModeManagers, newArray, _nightModeManagerCount);
                _nightModeManagers = newArray;
            }
            _nightModeManagers[_nightModeManagerCount++] = manager;

            // 登録時に現在の設定を通知
            manager.SendCustomEvent(NightModeUpdateEvent);
        }

        public void UnregisterNightModeManager(UdonSharpBehaviour manager)
        {
            if (!Utilities.IsValid(manager))
                return;
            int foundIndex = -1;
            for (int i = 0; i < _nightModeManagerCount; i++)
            {
                if (_nightModeManagers[i] == manager)
                {
                    foundIndex = i;
                    break;
                }
            }
            if (foundIndex != -1)
            {
                // 配列から削除 (末尾と入れ替えてカウントを減らす)
                _nightModeManagers[foundIndex] = _nightModeManagers[_nightModeManagerCount - 1];
                _nightModeManagers[_nightModeManagerCount - 1] = null; // 不要になった末尾をクリア
                _nightModeManagerCount--;
            }
        }

        // --- 設定値取得 ---
        public DateTime GetAlarmTriggerDateTime() => _alarmTriggerDateTime;

        public bool IsAlarmOn() => _isAlarmOn;

        public bool Is24HourFormat() => _is24HourFormat;

        public float GetNightModeValue() => _nightModeValue;

        // --- 設定値設定 ---
        public void SetAlarmTriggerDateTime(DateTime value)
        {
            if (_alarmTriggerDateTime != value)
            {
                _alarmTriggerDateTime = value;
                SaveSettings();
                NotifyAlarmSettingsChanged();
            }
        }

        public void SetIsAlarmOn(bool value)
        {
            if (_isAlarmOn != value)
            {
                _isAlarmOn = value;
                // アラームOFF時は時刻もリセット
                if (!_isAlarmOn)
                {
                    _alarmTriggerDateTime = DateTime.MinValue;
                    // アラームOFF時に、もし再生中だったら音を止めてフラグをリセットし、停止イベントを送る
                    if (_isAlarmPlaying)
                    {
                        if (Utilities.IsValid(alarmAudioSource))
                        {
                            alarmAudioSource.Stop();
                        }
                        ResetAlarmPlayingFlag(); // 再生中フラグをリセットし、停止イベントを通知
                    }
                }
                // アラームONにしたときは再生中フラグをリセット
                else // value が true の場合
                {
                    _isAlarmPlaying = false;
                    // ONにしたことを通知（再生停止状態であることを伝えるため）
                    NotifyAlarmStopped();
                }
                SaveSettings();
                NotifyAlarmSettingsChanged();
            }
        }

        public void SetIs24HourFormat(bool value)
        {
            if (_is24HourFormat != value)
            {
                _is24HourFormat = value;
                SaveSettings();
                NotifyAlarmSettingsChanged(); // 時刻表示形式もアラームUIに関わるため通知
            }
        }

        public void SetNightModeValue(float value)
        {
            // Mathf.Approximately はUdonSharpでは使えない可能性があるため、単純比較
            if (_nightModeValue != value)
            {
                _nightModeValue = Mathf.Clamp01(value); // 念のため0-1にクランプ
                SaveSettings();
                NotifyNightModeChanged();
            }
        }

        // --- Private Methods ---
        // GetPrefixedKeyメソッドを削除

        private void LoadSettings()
        {
            // 固定キーを使用
            // string alarmTimeKey = GetPrefixedKey(AlarmTimeKeyBase);
            // string alarmOnKey = GetPrefixedKey(AlarmOnKeyBase);
            // string timeFormatKey = GetPrefixedKey(TimeFormatKeyBase);
            // string nightModeValueKey = GetPrefixedKey(NightModeValueKeyBase);

            // デフォルト値を設定してからロード試行
            _is24HourFormat = true;
            if (
                PlayerData.TryGetBool(
                    Networking.LocalPlayer,
                    TimeFormatKey, // 固定キーを使用
                    out bool savedTimeFormat
                )
            )
                _is24HourFormat = savedTimeFormat;

            _isAlarmOn = false;
            if (PlayerData.TryGetBool(Networking.LocalPlayer, AlarmOnKey, out bool savedAlarmOn)) // 固定キーを使用
                _isAlarmOn = savedAlarmOn;

            _alarmTriggerDateTime = DateTime.MinValue;
            if (
                PlayerData.TryGetLong(
                    Networking.LocalPlayer,
                    AlarmTimeKey, // 固定キーを使用
                    out long savedAlarmTicks
                )
            )
            {
                if (savedAlarmTicks != DateTime.MinValue.Ticks)
                {
                    _alarmTriggerDateTime = new DateTime(
                        savedAlarmTicks,
                        DateTimeKind.Utc
                    ).ToLocalTime();
                }
            }

            // アラームがONなのに過去時刻の場合はOFFにする
            if (_isAlarmOn && _alarmTriggerDateTime <= DateTime.Now)
            {
                _isAlarmOn = false;
                // ここでSaveSettingsを呼ぶと無限ループの可能性があるので注意。
                // Load後に状態がおかしい場合は、次のSave時に修正される想定。
            }
            // ロード時に再生中フラグをリセット
            _isAlarmPlaying = false;

            _nightModeValue = 1.0f; // Load失敗時のデフォルト値も1に変更
            if (
                PlayerData.TryGetFloat(
                    Networking.LocalPlayer,
                    NightModeValueKey, // 固定キーを使用
                    out float savedNightModeValue
                )
            )
                _nightModeValue = Mathf.Clamp01(savedNightModeValue);
        }

        private void SaveSettings()
        {
            // 固定キーを使用
            // string alarmTimeKey = GetPrefixedKey(AlarmTimeKeyBase);
            // string alarmOnKey = GetPrefixedKey(AlarmOnKeyBase);
            // string timeFormatKey = GetPrefixedKey(TimeFormatKeyBase);
            // string nightModeValueKey = GetPrefixedKey(NightModeValueKeyBase);

            long alarmTriggerTicks =
                _isAlarmOn && _alarmTriggerDateTime != DateTime.MinValue
                    ? _alarmTriggerDateTime.ToUniversalTime().Ticks
                    : DateTime.MinValue.Ticks;

            PlayerData.SetLong(AlarmTimeKey, alarmTriggerTicks); // 固定キーを使用
            PlayerData.SetBool(AlarmOnKey, _isAlarmOn); // 固定キーを使用
            PlayerData.SetBool(TimeFormatKey, _is24HourFormat); // 固定キーを使用
            PlayerData.SetFloat(NightModeValueKey, _nightModeValue); // 固定キーを使用

            // PlayerDataはローカル永続化なので、RequestSerializationは不要
        }

        private void NotifyAlarmSettingsChanged()
        {
            // Debug.Log($"[SleepKitSettingsManager] Notifying {_alarmManagerCount} AlarmManagers.");
            for (int i = 0; i < _alarmManagerCount; i++)
            {
                if (Utilities.IsValid(_alarmManagers[i]))
                {
                    _alarmManagers[i].SendCustomEvent(AlarmSettingsUpdateEvent);
                }
                // 無効な参照があればリストから削除する処理を追加しても良いかもしれない
            }
        }

        // アラーム再生開始を通知するメソッド
        private void NotifyAlarmStarted()
        {
            for (int i = 0; i < _alarmManagerCount; i++)
            {
                if (Utilities.IsValid(_alarmManagers[i]))
                {
                    _alarmManagers[i].SendCustomEvent(AlarmStartedEvent);
                }
            }
        }

        // アラーム再生停止を通知するメソッド
        private void NotifyAlarmStopped()
        {
            for (int i = 0; i < _alarmManagerCount; i++)
            {
                if (Utilities.IsValid(_alarmManagers[i]))
                {
                    _alarmManagers[i].SendCustomEvent(AlarmStoppedEvent);
                }
            }
        }

        private void NotifyNightModeChanged()
        {
            // マテリアル更新処理を追加
            UpdateNightModeMaterial();

            // Debug.Log($"[SleepKitSettingsManager] Notifying {_nightModeManagerCount} NightModeManagers.");
            for (int i = 0; i < _nightModeManagerCount; i++)
            {
                if (Utilities.IsValid(_nightModeManagers[i]))
                {
                    _nightModeManagers[i].SendCustomEvent(NightModeUpdateEvent);
                }
                // 無効な参照があればリストから削除する処理を追加しても良いかもしれない
            }
        }

        // ナイトモードマテリアルを更新するメソッド
        private void UpdateNightModeMaterial()
        {
            if (Utilities.IsValid(nightModeRenderer))
            {
                // sharedMaterial を取得してプロパティを設定
                Material sharedMat = nightModeRenderer.sharedMaterial;
                if (sharedMat != null)
                {
                    sharedMat.SetFloat(nightModePropertyName, _nightModeValue);
                }
                else
                {
                    Debug.LogWarning(
                        "[SleepKitSettingsManager] nightModeRendererにマテリアルが設定されていません。"
                    );
                }
            }
            else
            {
                Debug.LogWarning(
                    "[SleepKitSettingsManager] nightModeRendererが設定されていません。"
                );
            }
        }
    }
}
