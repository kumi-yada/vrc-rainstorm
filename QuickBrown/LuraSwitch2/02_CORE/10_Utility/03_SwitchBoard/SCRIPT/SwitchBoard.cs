
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// SwitchBoardのモードを定義します。
    /// </summary>
    public enum SwitchBoardMode
    {
        /// <summary>ユーザーに最も近いHolderに自動的に追従します。</summary>
        Static = 0,
        /// <summary>ユーザーが掴んで移動できます。</summary>
        Pickup = 1,
    }

    /// <summary>
    /// スイッチボードの配置と移動を管理します。
    /// StaticモードとPickupモードを切り替え可能です。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SwitchBoard : UdonSharpBehaviour
    {
        #region フィールド

        [Header("■ 基本設定／Basic Settings")]
        [Tooltip("SwitchBoardの動作モードです。Static: 自動追従、Pickup: 掴んで移動可能")]
        [SerializeField] private SwitchBoardMode mode = SwitchBoardMode.Static;

        [Header("■ Static Mode")]
        [Tooltip("Staticモード時、ローカルユーザーに最も近いHolderへ移動・回転する間隔（秒）。Updateは使わず SendCustomEventDelayedSeconds でループします。")]
        [SerializeField] private float staticRecenterIntervalSeconds = 6f;

        [Tooltip("Staticモードで参照するHolder一覧です。未設定/空の場合は何もしません（安全側）。")]
        [SerializeField] private SwitchBoard_Holder[] holders;

        [Header("----------System----------")]
        [Tooltip("Pickup可能にする VRC_Pickup を指定します（通常は同じGameObject）。未設定なら自動取得します")]
        [SerializeField] private VRC_Pickup pickup;

        [Tooltip("Pickup用Collider（Staticモード時はDisable）。必須（未設定だと切り替えできません）")]
        [SerializeField] private Collider pickupCollider;

        [Tooltip("掴んだ/離した/Respawn時のSE再生に使うAudioSource。未設定なら自動取得します")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Boardの見た目のMeshRenderer")]
        [SerializeField] private MeshRenderer boardMeshRenderer;

        [Tooltip("BoardのCanvasGroup")]
        [SerializeField] private CanvasGroup boardCanvasGroup;

        [Header("■ Fade")]
        [Tooltip("Boardが移動する直前に透明化し、移動後にこの秒数で0→1へ戻します")]
        [SerializeField] private float moveFadeInSeconds = 0.5f;

        [Tooltip("フェードインの更新間隔（秒）。Updateは使わず、SendCustomEventDelayedSecondsで刻みます")]
        [SerializeField] private float fadeStepSeconds = 0.03f;

        [Tooltip("MeshRendererのMaterialに設定する透明度プロパティ名")]
        [SerializeField] private string meshFadePropertyName = "_Value";

        [Tooltip("Mesh側のフェード値を反転します（シェーダーが 1=透明 / 0=不透明 の場合にON）")]
        [SerializeField] private bool invertMeshFadeValue;

        [Tooltip("スナップ先とほぼ同じ位置の場合は移動/フェードを行いません（メートル）。Staticループのチラつき防止用")]
        [SerializeField] private float snapSkipPositionThresholdMeters = 0.001f;

        [Header("■ Sound")]
        [Tooltip("Pickup時に再生するSEです。")]
        [SerializeField] private AudioClip pickupClip;

        [Tooltip("Drop時に再生するSEです。")]
        [SerializeField] private AudioClip dropClip;

        [Tooltip("Respawn時に再生するSEです。")]
        [SerializeField] private AudioClip respawnClip;


        public SwitchBoardMode Mode => mode;

        private Vector3 snapLocalPositionOffset = Vector3.zero;
        private Vector3 snapLocalEulerOffset = Vector3.zero;

        private VRCPlayerApi _localPlayer;
        private bool _staticLoopScheduled;

        private Material[] _boardMaterials;

        private float _currentAlpha = 1f;
        private float _fadeStartTime;
        private float _fadeDuration;
        private int _fadeToken;

        #endregion

        #region 初期化

        /// <summary>
        /// 初期化処理を行います。
        /// </summary>
        private void Start()
        {
            _localPlayer = Networking.LocalPlayer;

            if (pickup == null)
            {
                pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            }

            if (audioSource == null)
            {
                audioSource = (AudioSource)GetComponent(typeof(AudioSource));
            }

            PrepareFadeProperty();
            ApplyAlphaImmediate(1f);

            ApplyModeVisuals();

            RegisterToHolders();
            InitializeHolderAwayFlags();
            NotifyHoldersModeChanged();
            StartRecenterLoopIfNeeded(true);
        }

        #endregion

        #region Holder管理

        /// <summary>
        /// 全てのHolderにこのBoardを登録します。
        /// </summary>
        private void RegisterToHolders()
        {
            if (holders == null)
            {
                return;
            }

            for (int i = 0; i < holders.Length; i++)
            {
                var h = holders[i];
                if (h == null)
                {
                    continue;
                }

                h.RegisterBoard(this);
            }
        }

        /// <summary>
        /// 全てのHolderのAway（離れている）フラグを初期化します。
        /// </summary>
        private void InitializeHolderAwayFlags()
        {
            if (holders == null)
            {
                return;
            }

            for (int i = 0; i < holders.Length; i++)
            {
                var h = holders[i];
                if (h == null)
                {
                    continue;
                }

                bool atHome = h.IsBoardAtHomeByDistance();
                h.MarkBoardAway(!atHome);
            }
        }

        /// <summary>
        /// 全てのHolderにモード変更を通知します。
        /// </summary>
        private void NotifyHoldersModeChanged()
        {
            if (holders == null)
            {
                return;
            }

            for (int i = 0; i < holders.Length; i++)
            {
                var h = holders[i];
                if (h == null)
                {
                    continue;
                }

                h.NotifyBoardModeChanged();
            }
        }

        /// <summary>
        /// 全てのHolderのAwayフラグを一括設定します。
        /// </summary>
        /// <param name="away">離れているかどうか</param>
        private void MarkAllHoldersAway(bool away)
        {
            if (holders == null)
            {
                return;
            }

            for (int i = 0; i < holders.Length; i++)
            {
                var h = holders[i];
                if (h == null)
                {
                    continue;
                }

                h.MarkBoardAway(away);
            }
        }

        #endregion

        #region モード制御

        /// <summary>
        /// ランタイム時にモードを変更します。
        /// </summary>
        /// <param name="newMode">新しいモード</param>
        public void Runtime_SetMode(SwitchBoardMode newMode)
        {
            mode = newMode;
            ApplyModeVisuals();
            NotifyHoldersModeChanged();
            StartRecenterLoopIfNeeded(false);
        }

        /// <summary>
        /// モードに応じてビジュアル（Collider、Pickup可否）を適用します。
        /// </summary>
        private void ApplyModeVisuals()
        {
            bool pickupEnabled = mode == SwitchBoardMode.Pickup;

            if (pickupCollider != null)
            {
                pickupCollider.enabled = pickupEnabled;
            }

            if (pickup != null)
            {
                pickup.pickupable = pickupEnabled;
            }
        }

        #endregion

        #region 再配置ループ

        /// <summary>
        /// 必要に応じて再配置ループを開始します。
        /// </summary>
        /// <param name="immediate">即座に実行するかどうか</param>
        private void StartRecenterLoopIfNeeded(bool immediate)
        {
            if (mode != SwitchBoardMode.Static)
            {
                return;
            }

            // Holderが1つ以下なら最寄り判定の意味が無いのでループしない
            if (holders == null)
            {
                return;
            }

            if (holders.Length < 2)
            {
                return;
            }

            if (_staticLoopScheduled)
            {
                return;
            }
            _staticLoopScheduled = true;

            if (immediate)
            {
                StaticRecenterLoop();
            }
            else
            {
                float delay = Mathf.Max(0.01f, staticRecenterIntervalSeconds);
                SendCustomEventDelayedSeconds(nameof(StaticRecenterLoop), delay);
            }
        }

        /// <summary>
        /// Staticモード時の再配置ループです（SendCustomEventDelayedSecondsで定期実行）。
        /// </summary>
        public void StaticRecenterLoop()
        {
            if (mode != SwitchBoardMode.Static)
            {
                _staticLoopScheduled = false;
                return;
            }

            if (holders == null || holders.Length < 2)
            {
                _staticLoopScheduled = false;
                return;
            }

            float interval = Mathf.Max(0.01f, staticRecenterIntervalSeconds);
            SendCustomEventDelayedSeconds(nameof(StaticRecenterLoop), interval);

            if (mode != SwitchBoardMode.Static)
            {
                return;
            }

            if (holders == null || holders.Length == 0)
            {
                return;
            }

            if (_localPlayer == null)
            {
                _localPlayer = Networking.LocalPlayer;
            }
            if (_localPlayer == null)
            {
                return;
            }

            var nearest = FindNearestHolder(_localPlayer);
            if (nearest == null)
            {
                return;
            }

            SnapToHolder(nearest);
        }

        /// <summary>
        /// ローカルプレイヤーに最も近いHolderを検索します。
        /// </summary>
        /// <param name="localPlayer">ローカルプレイヤー</param>
        /// <returns>最も近いHolder（見つからない場合null）</returns>
        private SwitchBoard_Holder FindNearestHolder(VRCPlayerApi localPlayer)
        {
            Vector3 playerPos = localPlayer.GetPosition();

            SwitchBoard_Holder best = null;
            float bestSqr = 0f;

            for (int i = 0; i < holders.Length; i++)
            {
                var h = holders[i];
                if (h == null) continue;
                Transform t = h.SnapTransform;
                if (t == null) continue;

                float sqr = (t.position - playerPos).sqrMagnitude;
                if (best == null || sqr < bestSqr)
                {
                    best = h;
                    bestSqr = sqr;
                }
            }

            return best;
        }

        #endregion

        #region スナップ処理

        /// <summary>
        /// 指定したHolderの位置にスナップ（移動・回転）します。
        /// </summary>
        /// <param name="holder">スナップ先のHolder</param>
        public void SnapToHolder(SwitchBoard_Holder holder)
        {
            if (holder == null)
            {
                return;
            }

            Transform snap = holder.SnapTransform;
            if (snap == null)
            {
                return;
            }

            Vector3 targetPos = snap.TransformPoint(snapLocalPositionOffset);
            Quaternion targetRot = snap.rotation * Quaternion.Euler(snapLocalEulerOffset);

            float posThreshold = snapSkipPositionThresholdMeters;
            if (posThreshold < 0.00001f)
            {
                posThreshold = 0.00001f;
            }

            float posSqr = (transform.position - targetPos).sqrMagnitude;
            float thresholdSqr = posThreshold * posThreshold;
            if (posSqr <= thresholdSqr)
            {
                return;
            }

            // 移動が目に見えるのを避けるため、移動直前に透明化→移動→フェードイン
            ApplyAlphaImmediate(0f);

            // 掴まれている場合の強制移動は挙動が不安定になりやすいので、Pickupモード時は基本行わない。
            // Triggerからの呼び出しでは明示的に呼ぶ想定。
            transform.SetPositionAndRotation(targetPos, targetRot);

            StartFadeIn(moveFadeInSeconds);

            // スナップ後は距離でフラグを作り直す（ループは使わない）
            InitializeHolderAwayFlags();
        }

        /// <summary>
        /// トリガーから呼ばれるスナップ処理です（Pickup中でも実行可能）。
        /// </summary>
        /// <param name="holder">スナップ先のHolder</param>
        public void Trigger_SnapToHolder(SwitchBoard_Holder holder)
        {
            if (pickup != null)
            {

            }

            PlayOneShotSafe(respawnClip);

            SnapToHolder(holder);
        }

        #endregion

        #region Pickupイベント

        /// <summary>
        /// Pickupが掴まれた際に呼ばれます。
        /// </summary>
        public override void OnPickup()
        {
            if (mode != SwitchBoardMode.Pickup)
            {
                return;
            }

            // 掴まれたらHolderから離れた扱いにする（ループで追跡しない）
            MarkAllHoldersAway(true);

            PlayOneShotSafe(pickupClip);
        }

        /// <summary>
        /// Pickupが離された際に呼ばれます。
        /// </summary>
        public override void OnDrop()
        {
            if (mode != SwitchBoardMode.Pickup)
            {
                return;
            }

            PlayOneShotSafe(dropClip);
        }

        #endregion

        #region ユーティリティ

        /// <summary>
        /// オーディオクリップを安全に再生します。
        /// </summary>
        /// <param name="clip">再生するクリップ</param>
        private void PlayOneShotSafe(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (audioSource == null)
            {
                return;
            }

            audioSource.PlayOneShot(clip);
        }

        private void PrepareFadeProperty()
        {
            _boardMaterials = null;

            if (boardMeshRenderer == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(meshFadePropertyName))
            {
                return;
            }

            _boardMaterials = boardMeshRenderer.materials;
        }

        private void ApplyAlphaImmediate(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            _currentAlpha = alpha;

            if (boardCanvasGroup != null)
            {
                boardCanvasGroup.alpha = alpha;
            }

            if (_boardMaterials != null)
            {
                float meshValue = invertMeshFadeValue ? (1f - alpha) : alpha;
                meshValue = Mathf.Clamp01(meshValue);

                for (int i = 0; i < _boardMaterials.Length; i++)
                {
                    Material m = _boardMaterials[i];
                    if (m == null)
                    {
                        continue;
                    }

                    m.SetFloat(meshFadePropertyName, meshValue);
                }
            }
        }

        private void StartFadeIn(float durationSeconds)
        {
            _fadeToken++;
            _fadeStartTime = Time.time;
            _fadeDuration = Mathf.Max(0.01f, durationSeconds);

            float step = Mathf.Max(0.01f, fadeStepSeconds);
            SendCustomEventDelayedSeconds(nameof(FadeInStep), step);
        }

        public void FadeInStep()
        {
            int tokenAtStart = _fadeToken;

            float t = (Time.time - _fadeStartTime) / _fadeDuration;
            float a = Mathf.Clamp01(t);
            ApplyAlphaImmediate(a);

            if (tokenAtStart != _fadeToken)
            {
                return;
            }

            if (a >= 1f)
            {
                return;
            }

            float step = Mathf.Max(0.01f, fadeStepSeconds);
            SendCustomEventDelayedSeconds(nameof(FadeInStep), step);
        }

        #endregion
    }
}
