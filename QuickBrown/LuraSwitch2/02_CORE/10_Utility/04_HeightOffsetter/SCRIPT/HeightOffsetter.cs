
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEditor;
using VRC.SDKBase.Editor.Attributes;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// プレイヤーの身長に応じて、ターゲットオブジェクトの高さを自動調整します。
    /// ReferenceBoxで定義された範囲内で、プレイヤーの視点高さに追従します。
    /// </summary>
    public class HeightOffsetter : UdonSharpBehaviour
    {
        #region インスペクターフィールド

        [Tooltip("高さの範囲を定義するBoxColliderです。")]
        [HelpBox("JP:\nReferenceBoxを操作することで高さを調整できます。\n\nEN:\nHeight can be adjusted by manipulating the ReferenceBox.", HelpBoxAttribute.MessageType.Info)]
        [SerializeField] private BoxCollider ReferenceBox;

        [Tooltip("高さを調整する対象のGameObjectリストです。")]
        [SerializeField] private GameObject[] OffsetTargets;

        [Header("Height Source")]
        [Tooltip("プレイヤーのRoot（足元）からView（視点）までの高さの割合です（0.0～1.0）。VRユーザーのみ有効。")]
        [SerializeField, Range(0f, 1f)]
        [HideInInspector]
        private float rootToViewHeightFactor = 0.9f;

        [Header("Editor Preview")]
        [Tooltip("エディタプレビュー用の高さ位置です（0.0～1.0）。")]
        [SerializeField, Range(0f, 1f)]
        private float HeightPreview = 0.5f;

        [Header("Sampling")]
        [Tooltip("プレイヤーの身長をサンプリングする間隔（秒）です。")]
        [SerializeField] private float sampleIntervalSeconds = 5f;

        [Tooltip("高さ変化の検出閾値です。この値未満の変化は無視されます。")]
        [SerializeField] private float heightChangeEpsilon = 0.01f;

        [Header("Smoothing")]
        [Tooltip("スムージング更新の間隔（フレーム数）です。")]
        [SerializeField, Range(1, 10)] private int smoothingTickFrames = 1;

        [Tooltip("高さ移動のアニメーション時間（秒）です。")]
        [SerializeField, Range(0.05f, 2f)] private float moveDurationSeconds = 0.3f;

        #endregion

        #region ランタイムフィールド

        private Vector3[] _initialTargetWorldPositions;
        private bool _initialized;
        private float _lastSampledPlayerHeight;
        private float _currentHeightY;
        private float _targetHeightY;

        private float _tweenStartHeightY;
        private float _tweenTargetHeightY;
        private float _tweenStartTime;
        private bool _tweenActive;

        private bool _sampleLoopStarted;
        private bool _smoothLoopStarted;

        private bool _runtimeInitialized;

        #endregion

        #region Unityイベント

        /// <summary>
        /// コンポーネントが有効化された際に呼ばれます。
        /// </summary>
        public void OnEnable()
        {
            EnsureRuntimeInitialized();
            StartLoops();
        }

        /// <summary>
        /// コンポーネントが無効化された際に呼ばれます。
        /// </summary>
        public void OnDisable()
        {
            _sampleLoopStarted = false;
            _smoothLoopStarted = false;
        }

        /// <summary>
        /// 初期化処理を行います。
        /// </summary>
        void Start()
        {
            EnsureRuntimeInitialized();
            StartLoops();
        }

        #endregion

        #region 初期化

        /// <summary>
        /// ランタイム状態が初期化されているか確認し、必要に応じて初期化します。
        /// </summary>
        private void EnsureRuntimeInitialized()
        {
            if (_runtimeInitialized) return;
            ResetRuntimeState();
            _runtimeInitialized = true;
        }

        /// <summary>
        /// ランタイム状態をリセットします。
        /// </summary>
        private void ResetRuntimeState()
        {
            _initialized = false;
            _lastSampledPlayerHeight = 0f;
            _currentHeightY = 0f;
            _targetHeightY = 0f;

            _tweenStartHeightY = 0f;
            _tweenTargetHeightY = 0f;
            _tweenStartTime = Time.time;
            _tweenActive = false;

            _sampleLoopStarted = false;
            _smoothLoopStarted = false;
        }

        /// <summary>
        /// サンプリングとスムージングのループを開始します。
        /// </summary>
        private void StartLoops()
        {
            // 初回は即時、以降は遅延イベントでループ
            if (!_sampleLoopStarted)
            {
                _sampleLoopStarted = true;
                SampleHeightLoop();
            }

            if (!_smoothLoopStarted)
            {
                _smoothLoopStarted = true;
                SmoothMoveLoop();
            }
        }

        #endregion

        #region サンプリングループ

        /// <summary>
        /// プレイヤーの高さをサンプリングするループです（SendCustomEventDelayedSecondsで定期実行）。
        /// </summary>
        public void SampleHeightLoop()
        {
            // 無効化/非アクティブ時は何もしない（以降の再スケジュールも止める）
            if (!IsRuntimeActive())
            {
                _sampleLoopStarted = false;
                return;
            }

            // 次回予約（Updateは使わず、遅延イベントで回す）
            float interval = sampleIntervalSeconds > 0.01f ? sampleIntervalSeconds : 5f;
            SendCustomEventDelayedSeconds(nameof(SampleHeightLoop), interval);

            if (ReferenceBox == null) return;
            var localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            float playerHeightY = GetLocalPlayerTargetHeightY(localPlayer, rootToViewHeightFactor);

            // 変化が小さい場合は何もしない（負荷対策）
            if (_initialized && Mathf.Abs(playerHeightY - _lastSampledPlayerHeight) < heightChangeEpsilon)
            {
                return;
            }
            _lastSampledPlayerHeight = playerHeightY;

            if (!TryGetReferenceBoxWorldMinMaxY(ReferenceBox, out float minY, out float maxY))
            {
                return;
            }

            // Min/Maxは割合ではなく「Clampした値」
            float clampedHeightY = Mathf.Clamp(playerHeightY, minY, maxY);

            if (!_initialized)
            {
                _initialized = true;
                int count = OffsetTargets != null ? OffsetTargets.Length : 0;
                _initialTargetWorldPositions = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    var go = OffsetTargets[i];
                    _initialTargetWorldPositions[i] = go != null ? go.transform.position : Vector3.zero;
                }

                _currentHeightY = clampedHeightY;
                SetTargetHeightY(clampedHeightY, true);
            }

            SetTargetHeightY(clampedHeightY, false);
        }

        #endregion

        #region スムージングループ

        /// <summary>
        /// スムーズな移動を行うループです（SendCustomEventDelayedFramesで定期実行）。
        /// </summary>
        public void SmoothMoveLoop()
        {
            // 無効化/非アクティブ時は何もしない（以降の再スケジュールも止める）
            if (!IsRuntimeActive())
            {
                _smoothLoopStarted = false;
                return;
            }

            int frames = smoothingTickFrames > 0 ? smoothingTickFrames : 1;
            SendCustomEventDelayedFrames(nameof(SmoothMoveLoop), frames);

            if (!_initialized) return;

            if (ReferenceBox == null) return;
            if (!TryGetReferenceBoxWorldMinMaxY(ReferenceBox, out float minY, out float maxY)) return;

            // Inspectorで配列が変更された場合は再初期化
            if (OffsetTargets == null || _initialTargetWorldPositions == null || _initialTargetWorldPositions.Length != OffsetTargets.Length)
            {
                _initialized = false;
                return;
            }

            float now = Time.time;
            if (_tweenActive)
            {
                float duration = Mathf.Max(0.01f, moveDurationSeconds);
                float t01 = Mathf.Clamp01((now - _tweenStartTime) / duration);
                float eased = t01 * t01 * (3f - 2f * t01); // SmoothStep
                _currentHeightY = Mathf.Lerp(_tweenStartHeightY, _tweenTargetHeightY, eased);

                if (t01 >= 1f)
                {
                    _currentHeightY = _tweenTargetHeightY;
                    _tweenActive = false;
                }
            }
            else
            {
                _currentHeightY = _targetHeightY;
            }

            ApplyHeightY(_currentHeightY, minY, maxY);
        }

        #endregion

        #region 高さ設定

        /// <summary>
        /// ターゲット高さを設定します。
        /// </summary>
        /// <param name="targetHeightY">設定する高さ</param>
        /// <param name="immediate">即座に適用するかどうか</param>
        private void SetTargetHeightY(float targetHeightY, bool immediate)
        {
            if (immediate)
            {
                _targetHeightY = targetHeightY;
                _currentHeightY = targetHeightY;
                _tweenActive = false;
                return;
            }

            // ほぼ同値なら更新しない
            if (Mathf.Abs(targetHeightY - _targetHeightY) < heightChangeEpsilon)
            {
                _targetHeightY = targetHeightY;
                return;
            }

            _targetHeightY = targetHeightY;

            // 変化があったら「現在値から0.3秒で新ターゲットへ」Tweenを張り直す
            _tweenStartHeightY = _currentHeightY;
            _tweenTargetHeightY = _targetHeightY;
            _tweenStartTime = Time.time;
            _tweenActive = true;
        }

        #endregion

        #region ユーティリティ

        /// <summary>
        /// ランタイムがアクティブかどうかを判定します。
        /// </summary>
        /// <returns>アクティブな場合true</returns>
        private bool IsRuntimeActive()
        {
            // UdonSharp では isActiveAndEnabled が露出していないため手動で判定
            return enabled && gameObject != null && gameObject.activeInHierarchy;
        }

        /// <summary>
        /// ローカルプレイヤーのターゲット高さ（Y座標）を取得します。
        /// </summary>
        /// <param name="player">対象プレイヤー</param>
        /// <param name="factor01">Root（足元）からView（視点）までの割合（0.0～1.0）</param>
        /// <returns>ターゲット高さ（ワールドY座標）</returns>
        private static float GetLocalPlayerTargetHeightY(VRCPlayerApi player, float factor01)
        {
            float rootY = player.GetPosition().y;

            // VR/デスクトップ両対応：Head の TrackingData を視点高さとして扱う
            var head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            float viewY = head.position.y;

            // 取得できないケースの保険
            if (float.IsNaN(viewY))
            {
                viewY = rootY;
            }

            // rootToViewHeightFactor は VR の時のみ有効。
            // 非VR(デスクトップ等)では常に 1.0 扱いにする。
            float t = player != null && player.IsUserInVR() ? Mathf.Clamp01(factor01) : 1f;
            return Mathf.Lerp(rootY, viewY, t);
        }

        /// <summary>
        /// BoxColliderのワールド座標でのY軸の最小値と最大値を取得します。
        /// </summary>
        /// <param name="box">対象のBoxCollider</param>
        /// <param name="minY">最小Y座標</param>
        /// <param name="maxY">最大Y座標</param>
        /// <returns>取得に成功した場合true</returns>
        public static bool TryGetReferenceBoxWorldMinMaxY(BoxCollider box, out float minY, out float maxY)
        {
            minY = 0f;
            maxY = 0f;

            if (box == null) return false;
            var t = box.transform;
            if (t == null) return false;

            Vector3 c = box.center;
            Vector3 half = box.size * 0.5f;

            // BoxCollider のローカル8頂点をワールドに変換して、ワールドYのmin/maxを取る
            bool initialized = false;
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iy = -1; iy <= 1; iy += 2)
                    for (int iz = -1; iz <= 1; iz += 2)
                    {
                        Vector3 local = c + new Vector3(half.x * ix, half.y * iy, half.z * iz);
                        Vector3 world = t.TransformPoint(local);
                        if (!initialized)
                        {
                            minY = world.y;
                            maxY = world.y;
                            initialized = true;
                        }
                        else
                        {
                            if (world.y < minY) minY = world.y;
                            if (world.y > maxY) maxY = world.y;
                        }
                    }

            return initialized && (maxY - minY) > 0.00001f;
        }

        /// <summary>
        /// 高さをターゲットオブジェクトに適用します。
        /// </summary>
        /// <param name="heightY">適用する高さ</param>
        /// <param name="minY">最小Y座標</param>
        /// <param name="maxY">最大Y座標</param>
        private void ApplyHeightY(float heightY, float minY, float maxY)
        {
            if (OffsetTargets == null || _initialTargetWorldPositions == null) return;

            int count = OffsetTargets.Length;
            if (_initialTargetWorldPositions.Length != count) return;

            for (int i = 0; i < count; i++)
            {
                var go = OffsetTargets[i];
                if (go == null) continue;

                // 移動はワールドYのみ。
                // 範囲外に出ないように Clamp し、範囲内は視点の高さと一致する。
                Vector3 basePos = _initialTargetWorldPositions[i];
                float y = Mathf.Clamp(heightY, minY, maxY);
                go.transform.position = new Vector3(basePos.x, y, basePos.z);
            }
        }

        #endregion

        #region エディタサポート

        [System.NonSerialized]
        private bool _isApplyingEditorPreview;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        /// <summary>
        /// エディタでの値の変更時に呼ばれる検証処理です。
        /// </summary>
        private void OnValidate()
        {
            // エディタ非再生時のみ SampleValue を反映
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (_isApplyingEditorPreview) return;

            // 無効化/非アクティブ時は変更しない
            if (!enabled) return;
            if (gameObject != null && !gameObject.activeInHierarchy) return;

            if (ReferenceBox == null) return;
            if (OffsetTargets == null) return;
            if (OffsetTargets.Length == 0) return;

            if (!TryGetReferenceBoxWorldMinMaxY(ReferenceBox, out float minY, out float maxY)) return;

            float t01 = Mathf.Clamp01(HeightPreview);
            float assumedViewY = Mathf.Lerp(minY, maxY, t01);
            float clampedY = Mathf.Clamp(assumedViewY, minY, maxY);

            _isApplyingEditorPreview = true;
            for (int i = 0; i < OffsetTargets.Length; i++)
            {
                var go = OffsetTargets[i];
                if (go == null) continue;

                Vector3 p = go.transform.position;
                go.transform.position = new Vector3(p.x, clampedY, p.z);
            }
            _isApplyingEditorPreview = false;
        }
#endif

        #endregion
    }
}
