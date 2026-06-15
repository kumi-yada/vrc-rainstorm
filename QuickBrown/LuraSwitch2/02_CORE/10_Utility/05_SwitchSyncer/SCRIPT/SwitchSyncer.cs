
using UdonSharp;
using UnityEngine;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// 複数の SwitchBase、SliderSwitch、SwitchSelector を同じ状態に同期するユーティリティ
    /// </summary>
    public class SwitchSyncer : UdonSharpBehaviour
    {
        #region インスペクターフィールド

        [Header("Sync Targets")]
        [Tooltip("同じ状態に揃えたい Toggle モードの SwitchBase 群")]
        [SerializeField] private SwitchBase[] toggleSwitches;

        [Tooltip("同じ選択状態（ONになっているスイッチのIndex）に揃えたい SwitchSelector 群")]
        [SerializeField] private SwitchSelector[] switchSelectors;

        [Tooltip("同じ状態に揃えたい SliderSwitch 群")]
        [SerializeField] private SliderSwitch[] sliderSwitches;

        [Header("Settings")]
        [Tooltip("状態チェック間隔（秒）。軽量化のため毎フレームは見ません")]
        [SerializeField] private float syncIntervalSeconds = 0.1f;

        [Tooltip("SliderSwitch の外部同期時の補間時間（秒）")]
        [SerializeField] private float sliderSyncInterpolationTime = 0.3f;

        #endregion

        #region 定数

        private const bool SliderSnapOnSync = true;
        private const float SliderEpsilon = 0.001f;
        private const bool ToggleSyncNetworkWhenGlobal = true;

        #endregion

        #region ランタイムフィールド

        private bool _initialized;
        private bool _running;

        private bool _toggleStateInitialized;
        private bool _toggleIsOn;
        private bool _toggleRestoreApplied;

        private bool _sliderStateInitialized;
        private float _sliderValue;
        private int _sliderChangedIndex = -1;
        private bool _sliderRestoreApplied;

        private bool _selectorStateInitialized;
        private int _selectorActiveIndex;
        private int _selectorChangedIndex = -1;
        private bool _selectorRestoreApplied;

        private bool _pendingSliderSync;
        private float _pendingSliderValue;
        private int _pendingSliderChangedIndex = -1;
        private bool _pendingSliderSyncScheduled;

        #endregion

        #region Unityイベント

        /// <summary>
        /// コンポーネント有効化時の処理
        /// </summary>
        private void OnEnable()
        {
            _running = true;

            if (!_initialized)
            {
                return;
            }

            ScheduleNextTick(0.01f);
        }

        /// <summary>
        /// コンポーネント無効化時の処理
        /// </summary>
        private void OnDisable()
        {
            _running = false;

            if (sliderSwitches != null)
            {
                for (int i = 0; i < sliderSwitches.Length; i++)
                {
                    var s = sliderSwitches[i];
                    if (s == null)
                    {
                        continue;
                    }

                    s.SetIgnoreDeserializationByExternalControl(false);
                }
            }

            _sliderChangedIndex = -1;
            _pendingSliderSync = false;
            _pendingSliderSyncScheduled = false;
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        private void Start()
        {
            _initialized = true;
            _running = true;

            // 初期状態：LocalSave の復元値があればそれを最優先し、全体に揃える
            if (TryGetAnyToggleRestoredValue(out var restoredToggle))
            {
                _toggleStateInitialized = true;
                _toggleIsOn = restoredToggle;
                ApplyToggleStateToAll(_toggleIsOn);
                _toggleRestoreApplied = true;
            }
            else if (TryGetAnyToggleState(out var initialToggle))
            {
                _toggleStateInitialized = true;
                _toggleIsOn = initialToggle;
                ApplyToggleStateToAll(_toggleIsOn);
            }

            if (TryGetAnySliderRestoredValue(out var restoredSlider))
            {
                _sliderStateInitialized = true;
                _sliderValue = restoredSlider;
                _sliderChangedIndex = -1;
                QueueSliderSync(_sliderValue, -1);
                _sliderRestoreApplied = true;
            }
            else if (TryGetAnySliderValue(out var initialSlider))
            {
                _sliderStateInitialized = true;
                _sliderValue = initialSlider;
                QueueSliderSync(_sliderValue, -1);
            }

            // Selector：LocalSaveの復元値があれば優先して全体へ
            if (TryGetAnySelectorRestoredIndex(out var restoredSelectorIndex))
            {
                _selectorStateInitialized = true;
                _selectorActiveIndex = restoredSelectorIndex;
                ApplySelectorIndexToAll(_selectorActiveIndex);
                _selectorRestoreApplied = true;
            }
            else if (TryGetAnySelectorIndex(out var initialSelectorIndex))
            {
                _selectorStateInitialized = true;
                _selectorActiveIndex = initialSelectorIndex;
                ApplySelectorIndexToAll(_selectorActiveIndex);
            }

            ScheduleNextTick(Mathf.Max(0.01f, syncIntervalSeconds));
        }

        #endregion

        #region 同期ループ

        /// <summary>
        /// 次回の同期チェックをスケジュール
        /// </summary>
        /// <param name="delaySeconds">遅延時間（秒）</param>
        private void ScheduleNextTick(float delaySeconds)
        {
            if (!_running)
            {
                return;
            }

            SendCustomEventDelayedSeconds("SyncTick", Mathf.Max(0.01f, delaySeconds));
        }

        /// <summary>
        /// 定期的な同期チェック処理
        /// </summary>
        public void SyncTick()
        {
            if (!_running)
            {
                return;
            }

            // Late Restore 対応：Start後に OnPlayerRestored が来た場合でも復元値を優先して全体へ反映
            if (!_toggleRestoreApplied && TryGetAnyToggleRestoredValue(out var restoredToggle))
            {
                _toggleStateInitialized = true;
                _toggleIsOn = restoredToggle;
                ApplyToggleStateToAll(_toggleIsOn);
                _toggleRestoreApplied = true;
            }

            if (!_sliderRestoreApplied && TryGetAnySliderRestoredValue(out var restoredSlider))
            {
                _sliderStateInitialized = true;
                _sliderValue = restoredSlider;
                _sliderChangedIndex = -1;
                QueueSliderSync(_sliderValue, -1);
                _sliderRestoreApplied = true;
            }

            if (!_selectorRestoreApplied && TryGetAnySelectorRestoredIndex(out var restoredSelectorIndex))
            {
                _selectorStateInitialized = true;
                _selectorActiveIndex = restoredSelectorIndex;
                _selectorChangedIndex = -1;
                ApplySelectorIndexToAll(_selectorActiveIndex);
                _selectorRestoreApplied = true;
            }

            // ToggleSwitch
            if (TryDetectToggleChange(out var newToggle))
            {
                _toggleIsOn = newToggle;
                ApplyToggleStateToAll(_toggleIsOn);
            }

            // SliderSwitch：どれかが変更されたら全体に同期
            if (TryDetectSliderChange(out var newSlider, out var changedIndex))
            {
                _sliderValue = newSlider;
                _sliderChangedIndex = changedIndex;
                // スライダー同期は最後に反映（反映側からはネット送信しない）
                QueueSliderSync(_sliderValue, _sliderChangedIndex);
            }

            // SwitchSelector：どれかが変更されたら全体に同期
            if (TryDetectSelectorChange(out var newSelectorIndex, out var selectorChangedIndex))
            {
                _selectorActiveIndex = newSelectorIndex;
                _selectorChangedIndex = selectorChangedIndex;
                ApplySelectorIndexToAll(_selectorActiveIndex);
            }

            ScheduleNextTick(syncIntervalSeconds);
        }

        #endregion

        #region Selector同期

        /// <summary>
        /// いずれかの SwitchSelector から現在の選択インデックスを取得
        /// </summary>
        /// <param name="index">取得したインデックス</param>
        /// <returns>取得成功時は true</returns>
        private bool TryGetAnySelectorIndex(out int index)
        {
            index = 0;

            if (switchSelectors == null)
            {
                return false;
            }

            for (int i = 0; i < switchSelectors.Length; i++)
            {
                var s = switchSelectors[i];
                if (s == null)
                {
                    continue;
                }

                int current = s.GetCurrentOnIndex();
                index = current >= 0 ? current : s.DefaultIndex;
                return true;
            }

            return false;
        }

        /// <summary>
        /// いずれかの SwitchSelector から LocalSave 復元値を取得
        /// </summary>
        /// <param name="index">復元されたインデックス</param>
        /// <returns>復元値が存在する場合は true</returns>
        private bool TryGetAnySelectorRestoredIndex(out int index)
        {
            index = 0;

            if (switchSelectors == null)
            {
                return false;
            }

            for (int i = 0; i < switchSelectors.Length; i++)
            {
                var s = switchSelectors[i];
                if (s == null)
                {
                    continue;
                }

                if (!s.HasLocalSaveRestored)
                {
                    continue;
                }

                index = s.LocalSaveRestoredIndex;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Selector の状態変化を検出
        /// </summary>
        /// <param name="newIndex">新しいインデックス</param>
        /// <param name="changedIndex">変更されたスイッチのインデックス</param>
        /// <returns>変化を検出した場合は true</returns>
        private bool TryDetectSelectorChange(out int newIndex, out int changedIndex)
        {
            newIndex = 0;
            changedIndex = -1;

            if (switchSelectors == null || switchSelectors.Length == 0)
            {
                return false;
            }

            if (!_selectorStateInitialized)
            {
                if (TryGetAnySelectorIndex(out var initial))
                {
                    _selectorStateInitialized = true;
                    _selectorActiveIndex = initial;
                }

                return false;
            }

            for (int i = 0; i < switchSelectors.Length; i++)
            {
                var s = switchSelectors[i];
                if (s == null)
                {
                    continue;
                }

                int current = s.GetCurrentOnIndex();
                int normalized = current >= 0 ? current : s.DefaultIndex;

                if (normalized != _selectorActiveIndex)
                {
                    newIndex = normalized;
                    changedIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// すべての SwitchSelector に選択インデックスを適用
        /// </summary>
        /// <param name="index">適用するインデックス</param>
        private void ApplySelectorIndexToAll(int index)
        {
            if (switchSelectors == null)
            {
                return;
            }

            for (int i = 0; i < switchSelectors.Length; i++)
            {
                var s = switchSelectors[i];
                if (s == null)
                {
                    continue;
                }

                // 変更元をスキップ（無限ループ対策＋無駄な再適用を避ける）
                if (_selectorChangedIndex >= 0 && i == _selectorChangedIndex)
                {
                    continue;
                }

                s.ApplySelectionFromExternal(index);
            }
        }

        #endregion

        #region Toggle同期

        /// <summary>
        /// いずれかの Toggle スイッチから現在の状態を取得
        /// </summary>
        /// <param name="isOn">取得した状態</param>
        /// <returns>取得成功時は true</returns>
        private bool TryGetAnyToggleState(out bool isOn)
        {
            isOn = false;
            if (toggleSwitches == null)
            {
                return false;
            }

            for (int i = 0; i < toggleSwitches.Length; i++)
            {
                var t = toggleSwitches[i];
                if (t == null)
                {
                    continue;
                }

                isOn = t.ToggleIsOn;
                return true;
            }

            return false;
        }

        /// <summary>
        /// いずれかの Toggle スイッチから LocalSave 復元値を取得
        /// </summary>
        /// <param name="isOn">復元された状態</param>
        /// <returns>復元値が存在する場合は true</returns>
        private bool TryGetAnyToggleRestoredValue(out bool isOn)
        {
            isOn = false;

            if (toggleSwitches == null)
            {
                return false;
            }

            for (int i = 0; i < toggleSwitches.Length; i++)
            {
                var t = toggleSwitches[i];
                if (t == null)
                {
                    continue;
                }

                if (!t.HasLocalSaveRestored)
                {
                    continue;
                }

                isOn = t.LocalSaveRestoredInt != 0;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Toggle の状態変化を検出
        /// </summary>
        /// <param name="newIsOn">新しい状態</param>
        /// <returns>変化を検出した場合は true</returns>
        private bool TryDetectToggleChange(out bool newIsOn)
        {
            newIsOn = false;

            if (toggleSwitches == null || toggleSwitches.Length == 0)
            {
                return false;
            }

            if (!_toggleStateInitialized)
            {
                if (TryGetAnyToggleState(out var initial))
                {
                    _toggleStateInitialized = true;
                    _toggleIsOn = initial;
                }

                return false;
            }

            for (int i = 0; i < toggleSwitches.Length; i++)
            {
                var t = toggleSwitches[i];
                if (t == null)
                {
                    continue;
                }

                bool current = t.ToggleIsOn;
                if (current != _toggleIsOn)
                {
                    newIsOn = current;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// すべての Toggle スイッチに状態を適用
        /// </summary>
        /// <param name="isOn">適用する状態</param>
        private void ApplyToggleStateToAll(bool isOn)
        {
            if (toggleSwitches == null)
            {
                return;
            }

            for (int i = 0; i < toggleSwitches.Length; i++)
            {
                var t = toggleSwitches[i];
                if (t == null)
                {
                    continue;
                }

                t.ApplyToggleStateFromExternal(isOn, ToggleSyncNetworkWhenGlobal);
            }
        }

        #endregion

        #region Slider同期

        /// <summary>
        /// いずれかの Slider から現在の値を取得
        /// </summary>
        /// <param name="value01">取得した値 (0-1)</param>
        /// <returns>取得成功時は true</returns>
        private bool TryGetAnySliderValue(out float value01)
        {
            value01 = 0f;
            if (sliderSwitches == null)
            {
                return false;
            }

            for (int i = 0; i < sliderSwitches.Length; i++)
            {
                var s = sliderSwitches[i];
                if (s == null)
                {
                    continue;
                }

                value01 = Mathf.Clamp01(s.sliderValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// いずれかの Slider から LocalSave 復元値を取得
        /// </summary>
        /// <param name="value01">復元された値 (0-1)</param>
        /// <returns>復元値が存在する場合は true</returns>
        private bool TryGetAnySliderRestoredValue(out float value01)
        {
            value01 = 0f;

            if (sliderSwitches == null)
            {
                return false;
            }

            for (int i = 0; i < sliderSwitches.Length; i++)
            {
                var s = sliderSwitches[i];
                if (s == null)
                {
                    continue;
                }

                if (!s.HasLocalSaveRestored)
                {
                    continue;
                }

                value01 = Mathf.Clamp01(s.LocalSaveRestoredValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Slider の状態変化を検出
        /// </summary>
        /// <param name="newValue01">新しい値 (0-1)</param>
        /// <param name="changedIndex">変更されたスライダーのインデックス</param>
        /// <returns>変化を検出した場合は true</returns>
        private bool TryDetectSliderChange(out float newValue01, out int changedIndex)
        {
            newValue01 = 0f;
            changedIndex = -1;

            if (sliderSwitches == null || sliderSwitches.Length == 0)
            {
                return false;
            }

            if (!_sliderStateInitialized)
            {
                if (TryGetAnySliderValue(out var initial))
                {
                    _sliderStateInitialized = true;
                    _sliderValue = initial;
                }

                return false;
            }

            for (int i = 0; i < sliderSwitches.Length; i++)
            {
                var s = sliderSwitches[i];
                if (s == null)
                {
                    continue;
                }

                // 補間中の値変化は原則「外部同期の途中経過」なので変更検出から除外。
                // ただし Global同期のデシリアライズ等で「出力も伴う補間」は、
                // マスターの値変化としてフォロワーへ追従させたいので除外しない。
                if (s.IsInterpolating && !s.IsInterpolationApplyingOutput)
                {
                    continue;
                }

                float current = Mathf.Clamp01(s.sliderValue);
                if (Mathf.Abs(current - _sliderValue) > Mathf.Max(0.000001f, SliderEpsilon))
                {
                    newValue01 = current;
                    changedIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Slider 同期をキューに追加（次フレームで適用）
        /// </summary>
        /// <param name="value01">同期する値 (0-1)</param>
        /// <param name="changedIndex">変更元のインデックス</param>
        private void QueueSliderSync(float value01, int changedIndex)
        {
            _pendingSliderSync = true;
            _pendingSliderValue = Mathf.Clamp01(value01);
            _pendingSliderChangedIndex = changedIndex;

            if (_pendingSliderSyncScheduled)
            {
                return;
            }

            _pendingSliderSyncScheduled = true;
            SendCustomEventDelayedFrames(nameof(ApplyPendingSliderSync), 1);
        }

        /// <summary>
        /// キューに追加された Slider 同期を適用
        /// </summary>
        public void ApplyPendingSliderSync()
        {
            _pendingSliderSyncScheduled = false;

            if (!_running)
            {
                return;
            }

            if (!_pendingSliderSync)
            {
                return;
            }

            _pendingSliderSync = false;

            _sliderChangedIndex = _pendingSliderChangedIndex;
            ApplySliderValueToAll(_pendingSliderValue);
        }

        /// <summary>
        /// すべての Slider に値を適用（ビジュアルのみ、出力は変更元のみ）
        /// </summary>
        /// <param name="value01">適用する値 (0-1)</param>
        private void ApplySliderValueToAll(float value01)
        {
            if (sliderSwitches == null)
            {
                return;
            }

            float v = Mathf.Clamp01(value01);
            for (int i = 0; i < sliderSwitches.Length; i++)
            {
                var s = sliderSwitches[i];
                if (s == null)
                {
                    continue;
                }

                // 変更元のスライダーはスキップ
                if (_sliderChangedIndex >= 0 && i == _sliderChangedIndex)
                {
                    continue;
                }

                // SwitchSyncerで繋がれたスライダーは「マスターだけが出力を更新」し、
                // フォロワーは見た目のみ追従させる。
                // これにより、確定時に同じターゲットへ2回出力されてカクつく問題を防ぐ。
                s.ApplyValueFromExternalWithTimeVisualOnly(v, SliderSnapOnSync, sliderSyncInterpolationTime);

                // 各スライダーに設定されているControllerのOnValueChangedも呼び出す
                // これにより、Controller_MirrorOpacityなどの外部コントローラーにも変更が反映される
                UdonSharpBehaviour controller = (UdonSharpBehaviour)s.GetProgramVariable("controller");
                if (controller != null)
                {
                    controller.SetProgramVariable("_value", v);
                    controller.SendCustomEvent("OnValueChanged");
                }
            }
        }

        #endregion
    }
}
