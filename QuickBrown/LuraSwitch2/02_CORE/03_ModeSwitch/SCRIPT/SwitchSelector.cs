
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDKBase.Editor.Attributes;
using VRC.SDK3.Persistence;

namespace QuickBrown.LuraSwitch
{
    public enum SwitchSelectorDefaultIndex
    {
        None = -1,
        Switch_0 = 0,
        Switch_1 = 1,
        Switch_2 = 2,
        Switch_3 = 3,
        Switch_4 = 4,
        Switch_5 = 5,
        Switch_6 = 6,
        Switch_7 = 7,
        Switch_8 = 8,
        Switch_9 = 9,
        Switch_10 = 10,
    }

    public enum SwitchSelectorAllowAllOff
    {
        Disable = 0,
        Enable = 1,
    }

    public enum SwitchSelectorSyncMode
    {
        Local = 0,
        LocalSave = 1,
        Global = 2,
    }

    public enum SwitchSelectorVisualMode
    {
        [InspectorName("2D_Interact")]
        Mode2D = 0,

        [InspectorName("2D_UI")]
        Mode2DUI = 1,

        [InspectorName("3D")]
        Mode3D = 2,
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SwitchSelector : UdonSharpBehaviour
    {
        #region インスペクターフィールド

        [HelpBox(
            "JP:\n複数のスイッチのうち、どれか1つだけがONになるように制御します。\n\nEN:\nControls multiple switches so that only one is ON at a time.", HelpBoxAttribute.MessageType.Info)]
        [SerializeField] private SwitchBase[] switches;
        [SerializeField] private SwitchSelectorDefaultIndex defaultActiveIndex = SwitchSelectorDefaultIndex.Switch_0;

        [Header("■ Allow All OFF")]
        [Tooltip("Disable: 常にどれか1つON（全OFF不可）\nEnable: 全OFFも可能（DefaultIndexをNoneにすると初期状態が全OFF）")]
        [SerializeField] private SwitchSelectorAllowAllOff allowAllOff = SwitchSelectorAllowAllOff.Disable;

        [Header("■ Sync Mode (Override)")]
        [Tooltip("Local: 各プレイヤーで排他（保存なし）\nLocalSave: SwitchSelectorが保存/復元（参照先SwitchBaseは全てLocalへ強制）\nGlobal: 全員で同じ排他（参照先SwitchBaseは全てGlobalへ強制）")]
        [SerializeField] private SwitchSelectorSyncMode syncMode = SwitchSelectorSyncMode.Local;

        [Header("■ Visual Mode (Override)")]
        [Tooltip("参照先SwitchBaseの表示モードを、SwitchSelectorの決定で上書きします（OnValidateで即時反映）。")]
        [SerializeField] private SwitchSelectorVisualMode visualMode = SwitchSelectorVisualMode.Mode3D;

        [Header("■ LocalSave")]
        [Tooltip("各プレイヤーごとに『どのスイッチがONか』を保存/復元します（PlayerData int）。")]
        [SerializeField] private string persistanceKey = "SwitchSelector_ActiveIndex";

        [Space(100)]
        [Header("----------System (Optional)----------")]
        [HelpBox("JP:\n複数スイッチの土台を3D／2Dで切り替えるためのオプションです。\n 通常設定は不要です。\n\nEN:\nOptional settings for switching the base of multiple switches between 3D/2D.\nNormally no configuration needed.", HelpBoxAttribute.MessageType.Info)]
        [Tooltip("3Dのときのみ表示するオブジェクト（未設定なら何もしません）")]
        [SerializeField] private GameObject[] Switch3DObjects;

        [Tooltip("2D(Interact/UI)のときのみ表示するオブジェクト（未設定なら何もしません）")]
        [SerializeField] private GameObject[] Switch2DObjects;

        #endregion

        #region ランタイムフィールド

        private bool _localSaveRestored;
        private int _localSaveRestoredIndex;

        private bool _initialized;
        private bool _initialSelectionApplied;
        private int _activeIndex = -1;

        #endregion

        #region 公開プロパティ

        public int ActiveIndex => _activeIndex;
        public int DefaultIndex => (int)defaultActiveIndex;
        public bool HasLocalSaveRestored => _localSaveRestored;
        public int LocalSaveRestoredIndex => _localSaveRestoredIndex;
        public SwitchSelectorSyncMode SyncMode => syncMode;

        #endregion

        #region 公開API

        /// <summary>
        /// 現在ONになっているスイッチのインデックスを取得します。
        /// </summary>
        public int GetCurrentOnIndex()
        {
            return FindFirstOnIndex();
        }

        /// <summary>
        /// 外部同期から選択を適用します。
        /// </summary>
        public void ApplySelectionFromExternal(int index)
        {
            ApplySelection(index, save: false);
        }

        /// <summary>
        /// 参照先スイッチの現在状態から、選択状態とインタラクト可能状態を再構築します。
        /// 主にネットワーク同期受信後の整合性維持に使用します。
        /// </summary>
        public void RefreshSelectionStateFromSwitches()
        {
            if (!_initialized)
            {
                return;
            }

            if (switches == null || switches.Length == 0)
            {
                return;
            }

            _activeIndex = FindFirstOnIndex();
            UpdateInteractableStates();
        }

        #endregion

        #region 初期化

        /// <summary>
        /// 初期化処理
        /// </summary>
        private void Start()
        {
            _initialized = true;

            if (switches == null || switches.Length == 0)
            {
                return;
            }

            for (int i = 0; i < switches.Length; i++)
            {
                var s = switches[i];
                if (s == null)
                {
                    continue;
                }

                s.SetSelector(this);
            }

            // 1フレーム遅延させて、参照先Switchの初期化後に選択を確定させる。
            SendCustomEventDelayedFrames(nameof(DeferredInitialize), 1);
        }

        /// <summary>
        /// 遅延初期化処理。SwitchBaseの初期化後に実行されます。
        /// </summary>
        public void DeferredInitialize()
        {
            if (_initialSelectionApplied)
            {
                return;
            }

            ForceSyncModeToSwitches();
            ForceVisualModeToSwitches();
            ApplySelectorVisualObjectsRuntime();


            if (syncMode == SwitchSelectorSyncMode.Global && !Networking.IsMaster)
            {
                RefreshSelectionStateFromSwitches();
            }
            else
            {
                ApplyInitialSelection();
            }

            UpdateInteractableStates();
            _initialSelectionApplied = true;
        }

        /// <summary>
        /// 初期選択を適用します。
        /// </summary>
        private void ApplyInitialSelection()
        {
            int active;

            if (syncMode == SwitchSelectorSyncMode.LocalSave && _localSaveRestored)
            {
                active = _localSaveRestoredIndex;
            }
            else
            {
                active = FindFirstOnIndex();
            }

            if (active < 0)
            {
                active = (int)defaultActiveIndex;
            }

            // allowAllOff が Enable で active が -1 なら全OFF状態を適用
            if (active < 0 && allowAllOff == SwitchSelectorAllowAllOff.Enable)
            {
                ApplyAllOff();
                return;
            }

            // active が -1 の場合は Switch_0 にフォールバック（後方互換）
            if (active < 0)
            {
                active = 0;
            }

            ApplySelection(active, save: false);
        }

        #endregion

        #region 強制適用処理

        /// <summary>
        /// 参照先の全スイッチに同期モードを強制適用します。
        /// </summary>
        private void ForceSyncModeToSwitches()
        {
            if (switches == null)
            {
                return;
            }

            // SwitchSelectorのSyncModeに合わせて参照先SwitchBaseのSyncModeを強制（ランタイムでも適用）
            SwitchSyncMode forcedSwitchMode = syncMode == SwitchSelectorSyncMode.Global
                ? SwitchSyncMode.Global
                : SwitchSyncMode.Local;

            for (int i = 0; i < switches.Length; i++)
            {
                var s = switches[i];
                if (s == null)
                {
                    continue;
                }

                if (s.Mode != SwitchMode.Toggle)
                {
                    continue;
                }

                // LocalSave の場合も SwitchBase側の保存は使わない（SwitchSelectorが担当）
                s.Runtime_SetSyncMode(forcedSwitchMode);
            }
        }

        /// <summary>
        /// 参照先の全スイッチに視覚モードを強制適用します。
        /// </summary>
        private void ForceVisualModeToSwitches()
        {
            if (switches == null)
            {
                return;
            }

            var forcedVisualMode = ConvertToSwitchBaseVisualMode(visualMode);

            for (int i = 0; i < switches.Length; i++)
            {
                var s = switches[i];
                if (s == null)
                {
                    continue;
                }

                if (s.Mode != SwitchMode.Toggle)
                {
                    continue;
                }

                s.Runtime_SetVisualMode(forcedVisualMode);
            }
        }

        /// <summary>
        /// ランタイム用：セレクターの視覚オブジェクトを表示モードに応じて切り替えます。
        /// </summary>
        private void ApplySelectorVisualObjectsRuntime()
        {
            SetActiveSafe(Switch3DObjects, visualMode == SwitchSelectorVisualMode.Mode3D);
            SetActiveSafe(Switch2DObjects, visualMode != SwitchSelectorVisualMode.Mode3D);
        }

        #endregion

        #region ユーティリティ

        /// <summary>
        /// SwitchSelectorのビジュアルモードをSwitchBaseのビジュアルモードに変換します。
        /// </summary>
        /// <param name="v">変換元のビジュアルモード</param>
        /// <returns>変換後のビジュアルモード</returns>
        private SwitchVisualMode ConvertToSwitchBaseVisualMode(SwitchSelectorVisualMode v)
        {
            switch (v)
            {
                case SwitchSelectorVisualMode.Mode2D:
                    return SwitchVisualMode.Mode2D;
                case SwitchSelectorVisualMode.Mode2DUI:
                    return SwitchVisualMode.Mode2DUI;
                case SwitchSelectorVisualMode.Mode3D:
                default:
                    return SwitchVisualMode.Mode3D;
            }
        }

        /// <summary>
        /// GameObjectの配列に対して安全にアクティブ状態を設定します。
        /// </summary>
        /// <param name="targets">対象のGameObject配列</param>
        /// <param name="active">設定するアクティブ状態</param>
        private void SetActiveSafe(GameObject[] targets, bool active)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t == null)
                {
                    continue;
                }

                t.SetActive(active);
            }
        }

        /// <summary>
        /// 指定されたスイッチのインデックスを取得します。
        /// </summary>
        /// <param name="target">検索対象のスイッチ</param>
        /// <returns>インデックス。見つからない場合は-1。</returns>
        private int IndexOf(SwitchBase target)
        {
            if (target == null || switches == null)
            {
                return -1;
            }

            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i] == target)
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion

        #region 永続化

        /// <summary>
        /// 永続化キーを取得します。
        /// </summary>
        /// <returns>永続化キー</returns>
        private string GetPersistenceKey() => persistanceKey;

        /// <summary>
        /// LocalSaveモード時に選択状態を保存します。
        /// </summary>
        /// <param name="index">保存するインデックス</param>
        private void SaveLocalIfNeeded(int index)
        {
            if (syncMode != SwitchSelectorSyncMode.LocalSave)
            {
                return;
            }

            string key = GetPersistenceKey();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            PlayerData.SetInt(key, index);
        }

        #endregion

        #region スイッチ状態管理

        /// <summary>
        /// 最初にONになっているスイッチのインデックスを検索します。
        /// </summary>
        /// <returns>最初のONスイッチのインデックス。見つからない場合は-1。</returns>
        private int FindFirstOnIndex()
        {
            if (switches == null)
            {
                return -1;
            }

            for (int i = 0; i < switches.Length; i++)
            {
                var s = switches[i];
                if (s == null)
                {
                    continue;
                }

                if (s.Mode != SwitchMode.Toggle)
                {
                    continue;
                }

                if (s.ToggleIsOn)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// スイッチの状態が変更された際のコールバック処理。
        /// </summary>
        /// <param name="changedSwitch">状態が変更されたスイッチ</param>
        /// <param name="isOn">新しい状態（true: ON、false: OFF）</param>
        public void OnSwitchStateChanged(SwitchBase changedSwitch, bool isOn)
        {
            if (!_initialized)
            {
                return;
            }

            if (switches == null || switches.Length == 0)
            {
                return;
            }

            int index = IndexOf(changedSwitch);
            if (index < 0)
            {
                return;
            }

            if (isOn)
            {
                EnforceExclusive(index);
                _activeIndex = index;
                UpdateInteractableStates();

                if (syncMode == SwitchSelectorSyncMode.LocalSave)
                {
                    SaveLocalIfNeeded(index);
                }

                return;
            }

            // OFFになった場合の処理
            if (FindFirstOnIndex() < 0)
            {
                // allowAllOff が Enable の場合は全OFFを許可
                if (allowAllOff == SwitchSelectorAllowAllOff.Enable)
                {
                    _activeIndex = -1;
                    UpdateInteractableStates();

                    if (syncMode == SwitchSelectorSyncMode.LocalSave)
                    {
                        SaveLocalIfNeeded(-1);
                    }
                }
                else
                {
                    // allowAllOff が Disable の場合は全OFFを防ぐ
                    changedSwitch.ApplyToggleStateFromSelector(true);
                    EnforceExclusive(index);
                    _activeIndex = index;
                    UpdateInteractableStates();

                    if (syncMode == SwitchSelectorSyncMode.LocalSave)
                    {
                        SaveLocalIfNeeded(index);
                    }
                }
            }
        }

        /// <summary>
        /// プレイヤーの状態が復元された際の処理を行います。
        /// </summary>
        /// <param name="player">復元されたプレイヤー</param>
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (syncMode != SwitchSelectorSyncMode.LocalSave)
            {
                return;
            }

            if (player == null || !player.isLocal)
            {
                return;
            }

            string key = GetPersistenceKey();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!PlayerData.TryGetInt(player, key, out int savedIndex))
            {
                return;
            }

            _localSaveRestored = true;
            _localSaveRestoredIndex = savedIndex;

            // Start後に復元が来る場合にも反映する
            if (_initialized && switches != null && switches.Length > 0)
            {
                ApplySelection(savedIndex, save: false);
                UpdateInteractableStates();
                _initialSelectionApplied = true;
            }
        }

        #endregion

        #region 選択適用

        /// <summary>
        /// 指定されたインデックスのスイッチを選択します。
        /// </summary>
        /// <param name="index">選択するスイッチのインデックス</param>
        /// <param name="save">選択を保存するかどうか</param>
        private void ApplySelection(int index, bool save)
        {
            if (switches == null || switches.Length == 0)
            {
                return;
            }

            if (index < 0)
            {
                if (allowAllOff == SwitchSelectorAllowAllOff.Enable)
                {
                    ApplyAllOff();

                    if (save && syncMode == SwitchSelectorSyncMode.LocalSave)
                    {
                        SaveLocalIfNeeded(-1);
                    }

                    return;
                }

                index = 0;
            }

            int activeIndex = Mathf.Clamp(index, 0, switches.Length - 1);
            _activeIndex = activeIndex;

            var activeSwitch = switches[activeIndex];
            if (activeSwitch != null && activeSwitch.Mode == SwitchMode.Toggle)
            {
                activeSwitch.ApplyToggleStateFromSelector(true);
            }

            EnforceExclusive(activeIndex);
            UpdateInteractableStates();

            if (save && syncMode == SwitchSelectorSyncMode.LocalSave)
            {
                SaveLocalIfNeeded(activeIndex);
            }
        }

        #endregion

        #region 排他制御

        /// <summary>
        /// 排他制御を強制します。
        /// </summary>
        /// <param name="activeIndex">アクティブにするスイッチのインデックス</param>
        private void EnforceExclusive(int activeIndex)
        {
            if (switches == null)
            {
                return;
            }

            for (int i = 0; i < switches.Length; i++)
            {
                if (i == activeIndex)
                {
                    continue;
                }

                var s = switches[i];
                if (s == null)
                {
                    continue;
                }

                if (s.Mode != SwitchMode.Toggle)
                {
                    continue;
                }

                if (!s.ToggleIsOn)
                {
                    continue;
                }

                s.ApplyToggleStateFromSelector(false);
            }
        }

        #endregion

        #region インタラクト制御

        /// <summary>
        /// 各スイッチのインタラクト可能状態を更新します。
        /// </summary>
        private void UpdateInteractableStates()
        {
            if (switches == null)
            {
                return;
            }

            for (int i = 0; i < switches.Length; i++)
            {
                var s = switches[i];
                if (s == null)
                {
                    continue;
                }

                if (s.Mode != SwitchMode.Toggle)
                {
                    continue;
                }

                // allowAllOff が Enable の場合、全スイッチが押せる
                // allowAllOff が Disable の場合、ONのスイッチは押せない
                if (allowAllOff == SwitchSelectorAllowAllOff.Enable)
                {
                    s.SetInteractable(true);
                }
                else
                {
                    s.SetInteractable(i != _activeIndex);
                }
            }
        }

        /// <summary>
        /// 全スイッチをOFFにします。
        /// </summary>
        private void ApplyAllOff()
        {
            if (switches == null)
            {
                return;
            }

            _activeIndex = -1;

            for (int i = 0; i < switches.Length; i++)
            {
                var s = switches[i];
                if (s == null)
                {
                    continue;
                }

                if (s.Mode != SwitchMode.Toggle)
                {
                    continue;
                }

                s.ApplyToggleStateFromSelector(false);
            }

            UpdateInteractableStates();
        }

        #endregion

        #region エディタサポート

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        /// <summary>
        /// エディタ用：指定されたスイッチを参照しているかどうかを判定します。
        /// </summary>
        /// <param name="target">判定対象のスイッチ</param>
        /// <returns>参照している場合true</returns>
        public bool Editor_ReferencesSwitch(SwitchBase target)
        {
            if (target == null || switches == null)
            {
                return false;
            }

            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// エディタでの値の変更時に呼ばれる検証処理です。
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

            // allowAllOff が Disable の時に None が選ばれたら Switch_0 に強制する
            if (allowAllOff == SwitchSelectorAllowAllOff.Disable && (int)defaultActiveIndex == -1)
            {
                defaultActiveIndex = SwitchSelectorDefaultIndex.Switch_0;
            }

            ApplySelectorVisualObjectsInEditor();

            if (switches == null || switches.Length == 0)
            {
                return;
            }

            // SyncModeの強制適用（SwitchSelectorの設定を参照先へ反映）
            SwitchSyncMode forcedSwitchMode = syncMode == SwitchSelectorSyncMode.Global
                ? SwitchSyncMode.Global
                : SwitchSyncMode.Local;

            var forcedVisualMode = ConvertToSwitchBaseVisualMode(visualMode);

            // allowAllOff が Enable で defaultActiveIndex が None(-1) なら全OFF
            if ((int)defaultActiveIndex == -1 && allowAllOff == SwitchSelectorAllowAllOff.Enable)
            {
                for (int i = 0; i < switches.Length; i++)
                {
                    var s = switches[i];
                    if (s == null)
                    {
                        continue;
                    }

                    if (s.Mode != SwitchMode.Toggle)
                    {
                        continue;
                    }

                    s.Editor_SetSyncMode(forcedSwitchMode);
                    s.Editor_SetVisualMode(forcedVisualMode);
                    s.Editor_ApplyToggleVisual(false);
                }
                return;
            }

            // activeIndex が -1 の場合は 0 にフォールバック（後方互換）
            int activeIndex = (int)defaultActiveIndex < 0 ? 0 : (int)defaultActiveIndex;
            activeIndex = Mathf.Clamp(activeIndex, 0, switches.Length - 1);

            for (int i = 0; i < switches.Length; i++)
            {
                var s = switches[i];
                if (s == null)
                {
                    continue;
                }

                if (s.Mode != SwitchMode.Toggle)
                {
                    continue;
                }

                // LocalSaveの場合も SwitchBase側の保存は使わない（SwitchSelectorが担当）
                s.Editor_SetSyncMode(forcedSwitchMode);

                // 表示モードの強制適用
                s.Editor_SetVisualMode(forcedVisualMode);

                bool shouldBeOn = i == activeIndex;

                s.Editor_ApplyToggleVisual(shouldBeOn);
            }
        }

        /// <summary>
        /// エディタ用：セレクターの視覚オブジェクトを表示モードに応じて切り替えます。
        /// </summary>
        private void ApplySelectorVisualObjectsInEditor()
        {
            // Selector自身の表示切り替え（ユーザー補助）。未設定なら何もしない。
            SetActiveSafe(Switch3DObjects, visualMode == SwitchSelectorVisualMode.Mode3D);
            SetActiveSafe(Switch2DObjects, visualMode != SwitchSelectorVisualMode.Mode3D);
        }
#endif

        #endregion
    }
}
