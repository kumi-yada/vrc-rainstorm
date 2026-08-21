using System; // Array.Copy用
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace RBS.SleepKit2.Udon
{
    // 名前を ToastManagerSettings に変更する方が適切かもしれないが、
    // 既存の参照を維持するため、クラス名はこのままにする
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class JoinToastManager : UdonSharpBehaviour // 実質的な役割は ToastManagerSettings
    {
        [Header("ToastManager Reference")]
        [Tooltip("シーンに1つだけ存在するToastManagerへの参照")]
        public ToastManager toastManager; // ToastManagerへの参照は残す

        // PersistenceManager参照を削除
        // [Header("PersistenceManagerの参照をInspectorから設定してください")]
        // [HideInInspector] public PersistenceManager persistenceManager;

        [Header("Default Settings")]
        [Tooltip("デフォルトで他のプレイヤー参加時に通知するか")]
        public bool defaultShowToast = true;

        [Tooltip("デフォルトで通知設定を永続化するか")]
        public bool defaultPersistSetting = false; // デフォルト値を設定

        [Header("UI Elements")]
        [Tooltip("永続化トグルの参照（状態を復元します）")]
        public Toggle toastPersistToggle; // HideInInspectorを削除し、インスペクターで設定可能に

        // --- 設定値 ---
        private bool _showJoinNotification = true; // showToast からリネーム
        private bool _persistJoinNotification = false; // toastPersist からリネーム

        // --- PlayerData Keys (固定キーに変更) ---
        private const string ToastStateKey = "RBS_SLK2_SHARED_TOAST_STATE"; // 0=OFF, 1=ON
        private const string ToastPersistKey = "RBS_SLK2_SHARED_TOAST_PERSIST"; // bool

        // --- UI Update Event ---
        private const string SettingsUpdateEvent = "_OnToastSettingsUpdate"; // イベント名を変更

        // --- UI Manager参照リスト (UI更新通知用) ---
        private UdonSharpBehaviour[] _uiManagers = new UdonSharpBehaviour[10]; // 初期サイズ10
        private int _uiManagerCount = 0;

        // GetPrefixedKeyメソッドを削除

        void Start()
        {
            // 初期値を設定
            _showJoinNotification = defaultShowToast;
            _persistJoinNotification = defaultPersistSetting;

            LoadSettings();
            // 初回UI更新のためにイベント発行
            NotifySettingsChanged();
        }

        /// <summary>
        /// デフォルトの状態に戻し、トーストを有効にします。
        /// </summary>
        public void EnableToast() // UIから呼ばれる想定なので名前はそのまま
        {
            SetShowJoinNotification(true);
        }

        /// <summary>
        /// トーストを無効にします。
        /// </summary>
        public void DisableToast() // UIから呼ばれる想定なので名前はそのまま
        {
            SetShowJoinNotification(false);
        }

        // Toggle UI から呼ばれることを想定したメソッドを追加
        public void ToggleToast(bool value)
        {
            SetShowJoinNotification(value);
        }

        /// <summary>
        /// 永続化トグルを切り替えます。
        /// 永続化がOFFになったときはデフォルトの状態(ON)に戻して保存します。
        /// </summary>
        public void ToggleToastPersistence(bool value) // 引数を受け取るように変更
        {
            if (_persistJoinNotification != value)
            {
                _persistJoinNotification = value;
                SaveSettings(); // 永続化設定自体を保存

                if (!_persistJoinNotification)
                {
                    // 永続化がOFFの場合は、デフォルト値(ON)に戻して保存
                    SetShowJoinNotification(true); // 内部メソッド呼び出しに変更
                }
                NotifySettingsChanged(); // UI更新のために通知
            }
            // UIのToggle更新はNotifySettingsChanged経由で行うため削除
            // if (toastPersistToggle != null)
            // {
            //     toastPersistToggle.SetIsOnWithoutNotify(toastPersist);
            // }
        }

        /// <summary>
        /// Udon のデータストア（Persistence）からの復元処理。
        /// ワールドに再入室した際などに呼ばれます。
        /// </summary>
        /// <param name="player">復元対象のプレイヤー</param>
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            // ローカルプレイヤーのみ対象
            if (!player.isLocal)
                return;
            LoadSettings();
            NotifySettingsChanged(); // UI更新のために通知
            // UIのToggle更新はNotifySettingsChanged経由で行うため削除
            // if (toastPersistToggle != null)
            // {
            //     toastPersistToggle.SetIsOnWithoutNotify(toastPersist);
            // }
        }

        // OnPlayerJoined と OnPlayerLeft を削除 (SleepKitSettingsManagerが担当)

        // ===== PlayerData 関連のヘルパー関数を削除 (直接アクセスするため) =====
        // private int GetPlayerInt(...)
        // private bool GetPlayerBool(...)


        // --- UI Manager 登録/解除 ---
        public void RegisterUIManager(UdonSharpBehaviour uiManager)
        {
            if (!Utilities.IsValid(uiManager))
                return;
            for (int i = 0; i < _uiManagerCount; i++)
            {
                if (_uiManagers[i] == uiManager)
                    return;
            }
            if (_uiManagerCount >= _uiManagers.Length)
            {
                var newArray = new UdonSharpBehaviour[_uiManagers.Length * 2];
                Array.Copy(_uiManagers, newArray, _uiManagerCount);
                _uiManagers = newArray;
            }
            _uiManagers[_uiManagerCount++] = uiManager;
            // 登録時に現在の設定を通知
            uiManager.SendCustomEvent(SettingsUpdateEvent);
        }

        public void UnregisterUIManager(UdonSharpBehaviour uiManager)
        {
            if (!Utilities.IsValid(uiManager))
                return;
            int foundIndex = -1;
            for (int i = 0; i < _uiManagerCount; i++)
            {
                if (_uiManagers[i] == uiManager)
                {
                    foundIndex = i;
                    break;
                }
            }
            if (foundIndex != -1)
            {
                _uiManagers[foundIndex] = _uiManagers[_uiManagerCount - 1];
                _uiManagers[_uiManagerCount - 1] = null;
                _uiManagerCount--;
            }
        }

        // --- 設定値取得 (外部公開用) ---
        public bool IsToastEnabled() => _showJoinNotification;

        public bool IsToastPersistent() => _persistJoinNotification;

        // --- 内部処理 ---
        private void SetShowJoinNotification(bool value)
        {
            if (_showJoinNotification != value)
            {
                _showJoinNotification = value;
                if (_persistJoinNotification) // 永続化がONの場合のみ保存
                {
                    SaveSettings();
                }
                NotifySettingsChanged(); // UI更新のために通知
            }
        }

        private void LoadSettings()
        {
            // 永続化設定をロード (デフォルトは defaultPersistSetting)
            if (
                PlayerData.TryGetBool(
                    Networking.LocalPlayer,
                    ToastPersistKey,
                    out bool savedPersist
                )
            )
            {
                _persistJoinNotification = savedPersist;
            }
            else
            {
                _persistJoinNotification = defaultPersistSetting;
            }

            // 通知ON/OFF設定をロード (デフォルトは defaultShowToast)
            if (_persistJoinNotification) // 永続化がONの場合のみロード試行
            {
                // 固定キーを使用
                if (PlayerData.TryGetInt(Networking.LocalPlayer, ToastStateKey, out int savedState))
                {
                    _showJoinNotification = (savedState == 1);
                }
                else
                {
                    _showJoinNotification = defaultShowToast; // ロード失敗時はデフォルト
                }
            }
            else
            {
                _showJoinNotification = defaultShowToast; // 永続化OFFなら常にデフォルト
            }
        }

        private void SaveSettings()
        {
            // 永続化設定自体を保存
            PlayerData.SetBool(ToastPersistKey, _persistJoinNotification);

            // 永続化がONの場合のみ、現在の通知ON/OFF状態を保存
            if (_persistJoinNotification)
            {
                // 固定キーを使用
                PlayerData.SetInt(ToastStateKey, _showJoinNotification ? 1 : 0);
            }
            // 永続化OFFの場合は保存しない（Load時にデフォルトに戻る）
        }

        private void NotifySettingsChanged()
        {
            // Debug.Log($"[JoinToastManager] Notifying {_uiManagerCount} UIManagers.");
            for (int i = 0; i < _uiManagerCount; i++)
            {
                if (Utilities.IsValid(_uiManagers[i]))
                {
                    _uiManagers[i].SendCustomEvent(SettingsUpdateEvent);
                }
            }
            // 自身のUIも更新
            UpdateSelfUI();
        }

        // 自身のUI（永続化トグル）を更新するメソッド
        private void UpdateSelfUI()
        {
            if (toastPersistToggle != null)
            {
                toastPersistToggle.SetIsOnWithoutNotify(_persistJoinNotification);
            }
        }

        // SleepKitSettingsManagerからToastManagerインスタンスを取得するためのメソッド
        public ToastManager GetToastManager()
        {
            if (!Utilities.IsValid(toastManager))
            {
                Debug.LogError("[JoinToastManager] ToastManagerが設定されていません！");
            }
            return toastManager;
        }
    }
}
