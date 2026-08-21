using UdonSharp;
using UnityEngine;
using UnityEngine.UI; // Toggle を使うため
using VRC.SDKBase;

namespace RBS.SleepKit2.Udon
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SleepKitUIManager : UdonSharpBehaviour
    {
        [Header("UI References")]
        [Tooltip("Join/Leave通知をONにするToggle")]
        public Toggle joinNotificationToggleOn; // ON用トグル

        [Tooltip("Join/Leave通知をOFFにするToggle")]
        public Toggle joinNotificationToggleOff; // OFF用トグル

        [Tooltip("Join/Leave通知設定の永続化を切り替えるToggle (オプション)")]
        public Toggle persistNotificationToggle;

        // 他にこのUI Managerが管理するUIがあればここに追加

        [Header("Settings Manager Reference (Set in Inspector or by Editor Script)")] // Tooltip変更
        [Tooltip("シーン内のJoinToastManagerへの参照（インスペクターで設定推奨）")]
        public JoinToastManager joinToastManager; // 参照先をJoinToastManagerに変更

        // --- UI Update Event Name (JoinToastManagerから発行される) ---
        private const string SettingsUpdateEvent = "_OnToastSettingsUpdate"; // JoinToastManager側のイベント名に合わせる

        void Start()
        {
            // 参照はEditorスクリプトまたはインスペクターで設定されることを期待
            if (!Utilities.IsValid(joinToastManager))
            {
                Debug.LogError("[SleepKitUIManager] JoinToastManager が設定されていません！");
                // UI無効化
                if (joinNotificationToggleOn != null)
                    joinNotificationToggleOn.interactable = false;
                if (joinNotificationToggleOff != null)
                    joinNotificationToggleOff.interactable = false;
                if (persistNotificationToggle != null)
                    persistNotificationToggle.interactable = false;
                return;
            }

            InitializeUIManager();
        }

        private void InitializeUIManager()
        {
            // JoinToastManagerに自身を登録して更新通知を受け取れるようにする
            joinToastManager.RegisterUIManager(this);
            // 初期UI状態を設定
            UpdateUI();
        }

        void OnDestroy()
        {
            // 破棄される際にJoinToastManagerから登録解除
            if (Utilities.IsValid(joinToastManager))
            {
                joinToastManager.UnregisterUIManager(this);
            }
        }

        // --- UI Event Handlers ---

        // JoinNotificationToggleOn の値が変更されたときに呼ばれる (OnClick などで呼ぶ)
        public void EnableJoinNotificationUI()
        {
            if (!Utilities.IsValid(joinToastManager))
                return;
            joinToastManager.EnableToast(); // JoinToastManagerのメソッドを呼び出す
            // UpdateUIはSettingsからの通知で呼ばれる
        }

        // JoinNotificationToggleOff の値が変更されたときに呼ばれる (OnClick などで呼ぶ)
        public void DisableJoinNotificationUI()
        {
            if (!Utilities.IsValid(joinToastManager))
                return;
            joinToastManager.DisableToast(); // JoinToastManagerのメソッドを呼び出す
            // UpdateUIはSettingsからの通知で呼ばれる
        }

        // PersistNotificationToggleの値が変更されたときに呼ばれる
        public void OnPersistNotificationToggleChanged()
        {
            if (!Utilities.IsValid(joinToastManager) || persistNotificationToggle == null)
                return;
            joinToastManager.ToggleToastPersistence(persistNotificationToggle.isOn);
        }

        // --- Settings Update Handler ---

        // JoinToastManagerから設定変更通知を受け取ったときに呼ばれる
        public void _OnToastSettingsUpdate() // メソッド名をJoinToastManager側に合わせる
        {
            UpdateUI();
        }

        // --- Private Methods ---

        // 現在の設定に基づいてUIの状態を更新する
        private void UpdateUI()
        {
            if (!Utilities.IsValid(joinToastManager))
                return;

            bool isEnabled = joinToastManager.IsToastEnabled(); // JoinToastManagerから取得
            bool isPersistent = joinToastManager.IsToastPersistent(); // JoinToastManagerから取得

            // ON/OFFトグルの状態を更新
            if (joinNotificationToggleOn != null)
            {
                joinNotificationToggleOn.SetIsOnWithoutNotify(isEnabled);
            }
            if (joinNotificationToggleOff != null)
            {
                joinNotificationToggleOff.SetIsOnWithoutNotify(!isEnabled);
            }

            // 永続化トグルの状態を更新 (nullチェック追加)
            if (persistNotificationToggle != null)
            {
                persistNotificationToggle.SetIsOnWithoutNotify(isPersistent);
            }
            // 他に管理するUIがあればここで更新
        }
    }
}
