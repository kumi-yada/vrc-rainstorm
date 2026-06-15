
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;
using UnityEditor;

namespace QuickBrown.LuraSwitch
{
    public enum MirrorType
    {
        SetupMirror = -1,
        Off = 0,
        LQ = 1,
        HQ = 2
    }

    public enum GlassObjectType
    {
        Glass = 0,
        noGlass = 1,
    }

    public enum GlassTiling
    {
        Tiling1 = 1,
        Tiling2 = 2,
        Tiling3 = 3,
        Tiling4 = 4,
        Tiling5 = 5,
        Tiling6 = 6,
        Tiling7 = 7,
        Tiling8 = 8,
        Tiling9 = 9,
        Tiling10 = 10,
        Tiling11 = 11,
        Tiling12 = 12,
        Tiling13 = 13,
        Tiling14 = 14,
        Tiling15 = 15,
    }

    public enum TriggerVisualizeMode
    {
        Off = 0,
        On = 1,
    }

    public class MirrorController : UdonSharpBehaviour
    {
        #region インスペクターフィールド

        [Header("■ エディタプレビュー")]
        [HelpBox("■■■■■■■■■■■■ Mirror Controller ■■■■■■■■■■■■\n\nJP:\nエディタ非再生時のプレビュー設定です。実行時には影響しません。\n\nEN:\nPreview settings for non-playing editor mode. Does not affect runtime.", HelpBoxAttribute.MessageType.Info)]
        [SerializeField] private MirrorType previewMirrorType = MirrorType.SetupMirror;

        [Tooltip("エディタ限定：ミラートリガーエリアの可視化設定")]
        [SerializeField] private TriggerVisualizeMode triggerVisualize = TriggerVisualizeMode.On;

        [Header("--------------------------------------------------")]
        [Space(10)]
        [Header("■ ミラーエリア範囲設定")]
        [HelpBox("JP:\nミラーが完全に表示されるFullAreaと、フェード開始のStartAreaを設定します。\nStartAreaの範囲内はミラーが点灯する領域なので、負荷に注意して設定してください。\n\nmirrorRange_FullArea:完全表示エリア\nmirrorSideMargin_FullArea:完全表示エリアの左右余白\nmirrorRange_StartArea:フェード開始エリア\nmirrorSideMargin_StartArea:フェード開始エリアの左右余白\n\nEN:\nSet the FullArea where the mirror is fully displayed and StartArea where fading begins.\nWithin the StartArea, the mirror is active, so please set it carefully considering performance.\n\nmirrorRange_FullArea: Full display area\nmirrorSideMargin_FullArea: Left/right margin for full display area\nmirrorRange_StartArea: Fade start area\nmirrorSideMargin_StartArea: Left/right margin for fade start area", HelpBoxAttribute.MessageType.Info)]
        [Tooltip("完全表示エリアの奥行き")]
        [SerializeField] private float mirrorRange_FullArea = 4.0f;

        [Tooltip("完全表示エリアの左右余白（両側に追加される幅）")]
        [SerializeField][Range(0f, 3f)] private float mirrorSideMargin_FullArea = 1f;

        [Tooltip("フェード開始エリアの奥行き（ミラー後面からの距離）")]
        [SerializeField] private float mirrorRange_StartArea = 2.0f;

        [Tooltip("フェード開始エリアの左右余白（両側に追加される幅）")]
        [SerializeField][Range(0f, 6f)] private float mirrorSideMargin_StartArea = 2f;

        [Header("--------------------------------------------------")]
        [Space(10)]
        [Header("■ 表示設定")]
        [HelpBox("JP:\nミラーの最大不透明度と前面にガラスオブジェクトを設定する場合の設定です。\n\nEN:\nSettings for mirror maximum opacity and front glass object configuration.", HelpBoxAttribute.MessageType.Info)]
        [Tooltip("ミラーの最大不透明度")]
        [SerializeField][Range(0.0f, 1.0f)] private float maxOpacity = 0.75f;

        [Tooltip("ガラスオブジェクトの表示タイプ")]
        [SerializeField] private GlassObjectType glassObjectType = GlassObjectType.Glass;

        [Tooltip("GlassObjectのタイリング数（1〜15）\nGlassShaderの'_Value'プロパティに反映されます")]
        [SerializeField] private GlassTiling glassTiling = GlassTiling.Tiling1;

        [Space(100)]
        [Header("----------System（変更不要）----------")]
        [HelpBox("JP:\n以下の設定は通常変更する必要はありません。システム内部で使用されます。\n\nEN:\nThe following settings do not usually need to be changed. They are used internally by the system.")]

        [Header("■ ミラーオブジェクト")]
        [Tooltip("セットアップ用の基準ミラー（位置・回転・スケールの基準）")]
        [SerializeField] private GameObject SetupMirror;

        [Space(10)]
        [Tooltip("OFF状態のミラーオブジェクト")]
        [SerializeField] private GameObject OffMirror;

        [Tooltip("LQミラーオブジェクト")]
        [SerializeField] private GameObject LQMirror;

        [Tooltip("HQミラーオブジェクト")]
        [SerializeField] private GameObject HQMirror;

        [Header("--------------------------------------------------")]
        [Space(10)]
        [Tooltip("LQミラー用の背面オブジェクト")]
        [SerializeField] private GameObject LQ_BackObject;

        [Tooltip("フェード開始エリアのトリガーオブジェクト")]
        [SerializeField] private GameObject MirrorStartArea;

        [Tooltip("完全表示エリアのトリガーオブジェクト")]
        [SerializeField] private GameObject MirrorFullArea;

        [Tooltip("エディタ用：エリアプレビューオブジェクト")]
        [SerializeField] private GameObject AreaPreviewObject;

        [Tooltip("ミラー表面のガラスオブジェクト")]
        [SerializeField] private GameObject GlassObject;

        [Header("--------------------------------------------------")]
        [Space(10)]
        [Tooltip("ミラー切り替えアニメーションの所要時間（秒）")]
        [SerializeField][Range(0.05f, 1.0f)] private float mirrorSwitchDurationSeconds = 0.5f;

        [Header("--------------------------------------------------")]
        [Space(10)]
        [Header("■ サウンド")]
        [Tooltip("ミラーON/OFF音を再生する AudioSource")]
        [SerializeField] private AudioSource mirrorAudioSource;

        [Tooltip("ミラーがONになる時に再生する OneShot クリップ")]
        [SerializeField] private AudioClip mirror_On;

        [Tooltip("ミラーがOFFになる時に再生する OneShot クリップ")]
        [SerializeField] private AudioClip mirror_Off;

        #endregion

        #region 定数

        private const float GlassFrontOffset = 0.005f;
        private const float MaxOpacityDisableEpsilon = 0.00001f;
        private const string ShaderAlpha = "_Alpha";
        private const string ShaderAlphaSecond = "_AlphaSecond";
        private const string MirrorCoverValuePropertyName = "_Value";

        #endregion

        #region ランタイムフィールド

        // 状態管理
        private MirrorType _activeMirrorType = MirrorType.Off;
        private bool isPlayerInMirrorArea = false;
        private bool _temporarilyHiddenByExternalControl;
        [SerializeField][HideInInspector] private float mirrorPower = 1.0f;

        // マテリアルキャッシュ
        private Material _setupMirrorMaterialInstance;
        private Material _offMirrorMaterialInstance;
        private Material _lqMirrorMaterialInstance;
        private Material _hqMirrorMaterialInstance;
        private MaterialPropertyBlock _glassPropertyBlock;

        // VRCMirrorReflectionキャッシュ
        private VRCMirrorReflection _lqMirrorReflection;
        private VRCMirrorReflection _hqMirrorReflection;

        // セットアップミラー法線キャッシュ
        private Vector3 _setupMirrorAverageNormalLocal = Vector3.forward;
        private bool _setupMirrorAverageNormalLocalValid = false;

        // セットアップミラートランスフォームキャッシュ（エディタ用）
        private Vector3 lastSetupMirrorPosition;
        private Quaternion lastSetupMirrorRotation;
        private Vector3 lastSetupMirrorScale;

        // アニメーション状態
        private bool isSwitchAnimating = false;
        private float switchTimer = 0f;
        private MirrorType pendingMirrorType = MirrorType.SetupMirror;
        private GameObject switchOldMirror;
        private GameObject switchNewMirror;
        private MirrorType switchTargetType = MirrorType.SetupMirror;
        private bool switchNewIsLQ = false;
        private bool switchLQBackSetupDone = false;

        // 遅延リクエスト
        private bool _deferredMirrorTypeRequestScheduled;
        private MirrorType _deferredRequestedMirrorType = MirrorType.Off;

        #endregion

        #region 初期化

        /// <summary>
        /// 初期化処理を行います。
        /// </summary>
        void Start()
        {
            EnsureMirrorRangeConsistency();
            CacheMirrorReflections();

            // SetupMirror の表面法線キャッシュ（未取得の場合は後で遅延取得されるが、初回ズレを防ぐため先に作る）
            CacheSetupMirrorAverageNormalLocal();

            // 各ミラーが sharedMaterial を共有している場合、ここで実行時用にインスタンス化して分離する
            EnsureMirrorMaterialInstances();

            // 実行時の初期状態は常に非アクティブ（Off）
            // 実際の切り替えは MirrorActivator / トリガーで制御する
            _activeMirrorType = MirrorType.Off;
            ApplyMirrorSettings(MirrorType.Off);

            // Range補正が入った場合に、実行時のトリガースケールも追従させる
            UpdateMirrorTrigger();

            UpdateGlassObjectVisibilityAndTransform(_activeMirrorType);

#if UNITY_EDITOR
            // SetupMirrorのTransform情報を初期化（エディタのみ）
            InitializeSetupMirrorTransform();
#endif
        }

        /// <summary>
        /// ミラーレンジの整合性を確保します（StartAreaはFullArea以上）。
        /// </summary>
        private void EnsureMirrorRangeConsistency()
        {
            if (mirrorRange_StartArea < mirrorRange_FullArea)
            {
                mirrorRange_StartArea = mirrorRange_FullArea;
            }
        }

        /// <summary>
        /// VRCMirrorReflectionコンポーネントをキャッシュします。
        /// </summary>
        private void CacheMirrorReflections()
        {
            _lqMirrorReflection = LQMirror != null ? LQMirror.GetComponent<VRCMirrorReflection>() : null;
            _hqMirrorReflection = HQMirror != null ? HQMirror.GetComponent<VRCMirrorReflection>() : null;
        }

        /// <summary>
        /// 全てのVRCMirrorReflectionを無効化します。
        /// </summary>
        private void DisableAllMirrorReflections()
        {
            CacheMirrorReflections();

            if (_lqMirrorReflection != null) _lqMirrorReflection.enabled = false;
            if (_hqMirrorReflection != null) _hqMirrorReflection.enabled = false;
        }

        /// <summary>
        /// 指定したミラータイプのVRCMirrorReflectionを有効化/無効化します。
        /// </summary>
        /// <param name="mirrorType">対象のミラータイプ</param>
        /// <param name="isEnabled">有効化するかどうか</param>
        private void SetMirrorReflectionEnabledForType(MirrorType mirrorType, bool isEnabled)
        {
            CacheMirrorReflections();

            if (mirrorType == MirrorType.LQ)
            {
                if (_lqMirrorReflection != null) _lqMirrorReflection.enabled = isEnabled;
                return;
            }

            if (mirrorType == MirrorType.HQ)
            {
                if (_hqMirrorReflection != null) _hqMirrorReflection.enabled = isEnabled;
                return;
            }
        }

        /// <summary>
        /// エディタプレビュー用：指定したミラーのVRCMirrorReflectionを有効化します。
        /// </summary>
        /// <param name="mirrorType">プレビューするミラータイプ</param>
        private void ApplyPreviewMirrorReflectionEnabled(MirrorType mirrorType)
        {
#if UNITY_EDITOR
            DisableAllMirrorReflections();
            SetMirrorReflectionEnabledForType(mirrorType, true);
#endif
        }

        /// <summary>
        /// 各ミラーのマテリアルインスタンスを確保します。
        /// </summary>
        private void EnsureMirrorMaterialInstances()
        {
            _setupMirrorMaterialInstance = EnsureMaterialInstanceForMirrorObject(SetupMirror, _setupMirrorMaterialInstance);
            _offMirrorMaterialInstance = EnsureMaterialInstanceForMirrorObject(OffMirror, _offMirrorMaterialInstance);
            _lqMirrorMaterialInstance = EnsureMaterialInstanceForMirrorObject(LQMirror, _lqMirrorMaterialInstance);
            _hqMirrorMaterialInstance = EnsureMaterialInstanceForMirrorObject(HQMirror, _hqMirrorMaterialInstance);
        }

        /// <summary>
        /// ミラーオブジェクトのマテリアルインスタンスを確保します。
        /// </summary>
        /// <param name="mirrorObject">対象のミラーオブジェクト</param>
        /// <param name="cached">キャッシュ済みマテリアル</param>
        /// <returns>マテリアルインスタンス</returns>
        private Material EnsureMaterialInstanceForMirrorObject(GameObject mirrorObject, Material cached)
        {
            if (mirrorObject == null)
            {
                return null;
            }

            var meshRenderer = mirrorObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                return null;
            }

            if (cached != null)
            {
                return cached;
            }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!EditorApplication.isPlaying)
            {
                return meshRenderer.sharedMaterial;
            }
#endif

            return meshRenderer.material;
        }

        /// <summary>
        /// ミラーオブジェクトの書き込み用マテリアルを取得します。
        /// </summary>
        /// <param name="mirrorObject">対象のミラーオブジェクト</param>
        /// <returns>書き込み用マテリアル</returns>
        private Material GetMirrorMaterialForWrite(GameObject mirrorObject)
        {
            if (mirrorObject == null)
            {
                return null;
            }

            // キャッシュ済みインスタンスを優先（無ければ生成）
            if (mirrorObject == SetupMirror)
            {
                _setupMirrorMaterialInstance = EnsureMaterialInstanceForMirrorObject(SetupMirror, _setupMirrorMaterialInstance);
                return _setupMirrorMaterialInstance;
            }
            if (mirrorObject == OffMirror)
            {
                _offMirrorMaterialInstance = EnsureMaterialInstanceForMirrorObject(OffMirror, _offMirrorMaterialInstance);
                return _offMirrorMaterialInstance;
            }
            if (mirrorObject == LQMirror)
            {
                _lqMirrorMaterialInstance = EnsureMaterialInstanceForMirrorObject(LQMirror, _lqMirrorMaterialInstance);
                return _lqMirrorMaterialInstance;
            }
            if (mirrorObject == HQMirror)
            {
                _hqMirrorMaterialInstance = EnsureMaterialInstanceForMirrorObject(HQMirror, _hqMirrorMaterialInstance);
                return _hqMirrorMaterialInstance;
            }

            // 想定外のオブジェクトが来た場合も、再生時はRenderer単位でインスタンスを作って操作
            var unknownRenderer = mirrorObject.GetComponent<MeshRenderer>();
            if (unknownRenderer == null)
            {
                return null;
            }
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!EditorApplication.isPlaying)
            {
                return unknownRenderer.sharedMaterial;
            }
#endif

            return unknownRenderer.material;
        }

        #endregion

        #region Unity イベント

        /// <summary>
        /// 毎フレームの更新処理を行います。
        /// </summary>
        void Update()
        {
            UpdateMirrorSwitchAnimation();
        }

#if UNITY_EDITOR
        /// <summary>
        /// エディタ用：フレーム末の更新処理を行います。
        /// </summary>
        void LateUpdate()
        {
            CheckSetupMirrorTransformChange();
        }
#endif

        #endregion

        #region 公開API

        /// <summary>
        /// ミラータイプに応じてミラーの表示を切り替える
        /// </summary>
        /// <param name="mirrorType">切り替え先のミラータイプ</param>
        public void SetMirror(MirrorType mirrorType)
        {
            ApplyMirrorSettings(mirrorType);
        }

        /// <summary>
        /// 外部からミラータイプを指定して切り替える（呼び出し側で使いやすい別名）
        /// </summary>
        /// <param name="mirrorType">切り替え先のミラータイプ</param>
        public void SetMirrorType(MirrorType mirrorType)
        {
            SetMirror(mirrorType);
        }

        /// <summary>
        /// MirrorType の切り替え要求を「次フレームにまとめて」反映する。
        /// 同フレーム内に複数回呼ばれた場合、最後の要求のみを採用する。
        /// </summary>
        /// <param name="mirrorType">切り替え先のミラータイプ</param>
        public void RequestMirrorType(MirrorType mirrorType)
        {
            _deferredRequestedMirrorType = mirrorType;

            if (_deferredMirrorTypeRequestScheduled)
            {
                return;
            }

            _deferredMirrorTypeRequestScheduled = true;
            SendCustomEventDelayedFrames("ApplyDeferredRequestedMirrorType", 1);
        }

        /// <summary>
        /// 遅延されたミラータイプ変更要求を適用します。
        /// </summary>
        public void ApplyDeferredRequestedMirrorType()
        {
            _deferredMirrorTypeRequestScheduled = false;
            SetMirrorType(_deferredRequestedMirrorType);
        }

        #endregion

        #region ミラー切り替え処理

        /// <summary>
        /// 実際のミラー切り替え処理を実行します。
        /// </summary>
        /// <param name="mirrorType">設定するミラータイプ</param>
        private void ApplyMirrorSettings(MirrorType mirrorType)
        {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            // エディタの非再生時は「確認用」として preview を即時反映する。
            // - トリガー内判定（isPlayerInMirrorArea）で抑制しない
            // - Update が回らないため切り替えアニメも使わない
            bool forcePreviewInEditor = !EditorApplication.isPlaying;
#else
        bool forcePreviewInEditor = false;
#endif

            bool isInAreaForActivation = isPlayerInMirrorArea || forcePreviewInEditor;

            // maxOpacity が 0 の場合は、トリガー内でもミラーをアクティブ化しない。
            // （_activeMirrorType は保持しておき、maxOpacity が戻ったら再表示できるようにする）
            if (mirrorType != MirrorType.SetupMirror && maxOpacity <= MaxOpacityDisableEpsilon)
            {
                CancelMirrorSwitchAnimation();
                HideAllMirrors();
                HideSetupMirror();
                ResetAlphaSecondForAllMirrors(1f);

                _activeMirrorType = mirrorType;
                UpdateGlassObjectVisibilityAndTransform(mirrorType);
                return;
            }

            // 「トリガー内にいる時だけ」実ミラーをアクティブにする。
            // エリア外で HQ/LQ に設定されても、負荷の高いミラーを有効化しない（要求だけ保持）。
            if (!isInAreaForActivation && mirrorType != MirrorType.SetupMirror)
            {
                CancelMirrorSwitchAnimation();

                // エリア外では全て非表示
                HideAllMirrors();
                HideSetupMirror();

                // 次回表示に備えて _AlphaSecond を戻す
                ResetAlphaSecondForAllMirrors(1f);

                // 要求されたタイプ自体は保持（エリア内に入った時に反映するため）
                _activeMirrorType = mirrorType;

                // GlassObject の表示/Transform は従来通り更新
                UpdateGlassObjectVisibilityAndTransform(mirrorType);
                return;
            }

            // トリガー内で「別タイプへ切り替える」場合のみ、透明度アニメを使う
            if (!forcePreviewInEditor && isPlayerInMirrorArea && mirrorType != MirrorType.SetupMirror && mirrorType != _activeMirrorType)
            {
                StartMirrorSwitchAnimation(mirrorType);
                return;
            }

            CancelMirrorSwitchAnimation();

            // まず全てのミラーを非表示にする
            HideAllMirrors();

            if (mirrorType == MirrorType.SetupMirror)
            {
                // セットアップモード：参照用ミラーを表示
                ShowSetupMirror();
            }
            else
            {
                // 通常モード：指定されたミラーを表示
                ShowSpecificMirror(mirrorType);
                HideSetupMirror();
            }

            // エディタの非再生時プレビューでは、VRC Mirror Reflection を有効化して反映させる
            if (forcePreviewInEditor)
            {
                CacheMirrorReflections();
                ApplyPreviewMirrorReflectionEnabled(mirrorType);
            }

            UpdateGlassObjectVisibilityAndTransform(mirrorType);

            // アニメを使わない切り替えでは _AlphaSecond を常に 1 に戻す
            ResetAlphaSecondForAllMirrors(1f);

            _activeMirrorType = mirrorType;
        }

        #endregion

        #region アニメーション処理

        /// <summary>
        /// ミラー切り替えアニメーションを開始します。
        /// </summary>
        /// <param name="mirrorType">切り替え先のミラータイプ</param>
        private void StartMirrorSwitchAnimation(MirrorType mirrorType)
        {
            // OFF <-> ON の切り替え時のみサウンドを再生
            PlayMirrorToggleOneShotIfNeeded(_activeMirrorType, mirrorType);

            // 連続入力：アニメ中は最後の要求だけを保持し、終了後に反映
            if (isSwitchAnimating)
            {
                pendingMirrorType = mirrorType;
                return;
            }

            // 念のため
            if (mirrorType == _activeMirrorType)
            {
                return;
            }

            isSwitchAnimating = true;
            switchTimer = 0f;
            switchTargetType = mirrorType;
            switchNewIsLQ = (mirrorType == MirrorType.LQ);
            switchLQBackSetupDone = false;

            switchOldMirror = FindMirrorObject(_activeMirrorType);
            switchNewMirror = FindMirrorObject(mirrorType);

            // 切り替え開始時点では旧ミラーの反射を維持し、新ミラーはアクティブ化タイミングまで無効のまま
            SetMirrorReflectionEnabledForType(_activeMirrorType, true);
            SetMirrorReflectionEnabledForType(mirrorType, false);

            // SetupMirrorは通常表示しない
            HideSetupMirror();

            // GlassObject は SetupMirror に追従（位置・回転・スケール + 前面オフセット）
            UpdateGlassObjectVisibilityAndTransform(mirrorType);

            // 旧ミラー：フェードアウト開始のため 1
            if (switchOldMirror != null)
            {
                SetMirrorAlphaSecondForObject(switchOldMirror, 1f);
            }

            // 新ミラー：フェードイン開始のため 0（この時点ではアクティブ化しない）
            if (switchNewMirror != null)
            {
                SetMirrorAlphaSecondForObject(switchNewMirror, 0f);
            }

            // ベースの _Alpha は現在の mirrorPower に合わせて更新
            UpdateMirrorAlpha();
        }

        /// <summary>
        /// ミラーのON/OFF切り替えに応じて OneShot を再生します。
        /// </summary>
        /// <param name="from">切り替え元のミラータイプ</param>
        /// <param name="to">切り替え先のミラータイプ</param>
        private void PlayMirrorToggleOneShotIfNeeded(MirrorType from, MirrorType to)
        {
            // 実行時のみ（エディタプレビューなどは鳴らさない）
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!EditorApplication.isPlaying)
            {
                return;
            }
#endif

            // そもそもミラーエリア外での要求保持などでは鳴らさない
            if (!isPlayerInMirrorArea)
            {
                return;
            }

            if (mirrorAudioSource == null)
            {
                return;
            }

            if (from == MirrorType.SetupMirror || to == MirrorType.SetupMirror)
            {
                return;
            }

            bool wasOn = from != MirrorType.Off;
            bool willBeOn = to != MirrorType.Off;

            // ON/OFFが変わらない（LQ<->HQ 等）場合は鳴らさない
            if (wasOn == willBeOn)
            {
                return;
            }

            AudioClip clip = willBeOn ? mirror_On : mirror_Off;
            if (clip == null)
            {
                return;
            }

            mirrorAudioSource.PlayOneShot(clip);
        }

        /// <summary>
        /// ミラー切り替えアニメーションをキャンセルします。
        /// </summary>
        private void CancelMirrorSwitchAnimation()
        {
            isSwitchAnimating = false;
            switchTimer = 0f;
            pendingMirrorType = MirrorType.SetupMirror;
            switchOldMirror = null;
            switchNewMirror = null;
            switchTargetType = MirrorType.SetupMirror;
            switchNewIsLQ = false;
            switchLQBackSetupDone = false;
        }

        /// <summary>
        /// ミラー切り替えアニメーションを更新します。
        /// </summary>
        private void UpdateMirrorSwitchAnimation()
        {
            if (!isSwitchAnimating) return;

            switchTimer += Time.deltaTime;

            float totalSeconds = Mathf.Max(0.01f, mirrorSwitchDurationSeconds);
            float fadeOutSeconds = totalSeconds * 0.5f;
            float fadeInSeconds = totalSeconds * 0.5f;

            // 1) 旧ミラーを 1 -> 0 へ（0.25秒）
            if (switchTimer <= fadeOutSeconds)
            {
                float tOut = fadeOutSeconds <= 0.0001f ? 1f : Mathf.Clamp01(switchTimer / fadeOutSeconds);
                if (switchOldMirror != null) SetMirrorAlphaSecondForObject(switchOldMirror, Mathf.Lerp(1f, 0f, tOut));
                return;
            }

            // 旧ミラーを非アクティブにする（フェードアウト完了タイミング）
            if (switchOldMirror != null)
            {
                switchOldMirror.SetActive(false);

                // 旧ミラーの反射も無効化
                SetMirrorReflectionEnabledForType(_activeMirrorType, false);

                // LQから切り替える場合はバックオブジェクトも消す
                if (_activeMirrorType == MirrorType.LQ)
                {
                    SetMirrorActive(LQ_BackObject, false);
                }

                switchOldMirror = null;
            }

            // 2) 新ミラーをアクティブ化して 0 -> 1 へ（0.25秒）
            if (switchNewMirror != null && !switchNewMirror.activeSelf)
            {
                switchNewMirror.SetActive(true);
                SyncTransformFromSetupMirror(switchNewMirror);

                // 新ミラーの反射を有効化
                SetMirrorReflectionEnabledForType(switchTargetType, true);
            }

            // GlassObjectはミラー切替中も追従させる
            UpdateGlassObjectVisibilityAndTransform(switchTargetType);

            // LQへ切り替える場合は、フェードイン開始時にバックオブジェクトをセットアップ
            if (switchNewIsLQ && !switchLQBackSetupDone)
            {
                SetupLQBackObject();
                switchLQBackSetupDone = true;
            }

            float fadeInTime = switchTimer - fadeOutSeconds;
            float tIn = fadeInSeconds <= 0.0001f ? 1f : Mathf.Clamp01(fadeInTime / fadeInSeconds);
            if (switchNewMirror != null) SetMirrorAlphaSecondForObject(switchNewMirror, Mathf.Lerp(0f, 1f, tIn));

            // 3) 完了（合計時間）
            if (switchTimer >= totalSeconds)
            {
                _activeMirrorType = switchTargetType;
                isSwitchAnimating = false;

                if (switchNewMirror != null) SetMirrorAlphaSecondForObject(switchNewMirror, 1f);

                // 保留があれば続けて実行
                if (pendingMirrorType != MirrorType.SetupMirror && pendingMirrorType != _activeMirrorType)
                {
                    MirrorType next = pendingMirrorType;
                    pendingMirrorType = MirrorType.SetupMirror;
                    StartMirrorSwitchAnimation(next);
                }
                else
                {
                    pendingMirrorType = MirrorType.SetupMirror;
                }
            }
        }

        /// <summary>
        /// ミラーオブジェクトのAlphaSecondプロパティを設定します。
        /// </summary>
        /// <param name="mirrorObject">対象のミラーオブジェクト</param>
        /// <param name="value">設定する値（0.0～1.0）</param>
        private void SetMirrorAlphaSecondForObject(GameObject mirrorObject, float value)
        {
            if (mirrorObject == null) return;

            var meshRenderer = mirrorObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null) return;

            var material = GetMirrorMaterialForWrite(mirrorObject);
            if (material == null) return;

            if (material.shader != null && material.shader.name.Contains("CustomMirror"))
            {
                material.SetFloat(ShaderAlphaSecond, Mathf.Clamp01(value));
            }
        }

        /// <summary>
        /// 全てのミラーのAlphaSecondプロパティをリセットします。
        /// </summary>
        /// <param name="value">設定する値</param>
        private void ResetAlphaSecondForAllMirrors(float value)
        {
            SetMirrorAlphaSecondForObject(OffMirror, value);
            SetMirrorAlphaSecondForObject(LQMirror, value);
            SetMirrorAlphaSecondForObject(HQMirror, value);
        }

        #endregion

        #region ミラー表示制御

        /// <summary>
        /// セットアップ用ミラーを表示する
        /// </summary>
        private void ShowSetupMirror()
        {
            if (SetupMirror != null)
            {
                var meshRenderer = SetupMirror.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = true;
                }
            }
        }

        /// <summary>
        /// セットアップ用ミラーを非表示にする
        /// </summary>
        private void HideSetupMirror()
        {
            if (SetupMirror != null)
            {
                var meshRenderer = SetupMirror.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = false;
                }
            }
        }

        /// <summary>
        /// 指定されたミラーを表示し、位置をセットアップミラーに合わせる
        /// </summary>
        /// <param name="mirrorType">表示するミラータイプ</param>
        private void ShowSpecificMirror(MirrorType mirrorType)
        {
            // 切り替え後に残らないよう、まず反射を全て無効化
            DisableAllMirrorReflections();

            GameObject targetMirror = FindMirrorObject(mirrorType);
            if (targetMirror != null)
            {
                targetMirror.SetActive(true);
                SyncTransformFromSetupMirror(targetMirror);
            }

            // 表示するミラータイプの反射を有効化
            SetMirrorReflectionEnabledForType(mirrorType, true);

            // LQミラーの場合、LQ_BackObjectも処理する
            if (mirrorType == MirrorType.LQ)
            {
                SetupLQBackObject();
            }
        }

        /// <summary>
        /// 全てのミラーオブジェクトを非表示にする
        /// </summary>
        private void HideAllMirrors()
        {
            SetMirrorActive(OffMirror, false);
            SetMirrorActive(LQMirror, false);
            SetMirrorActive(HQMirror, false);
            SetMirrorActive(LQ_BackObject, false);

            // 非表示時は反射も落とす（実行時の負荷/意図しないプレビュー反射を防ぐ）
            DisableAllMirrorReflections();
        }

        /// <summary>
        /// ミラータイプに対応するミラーオブジェクトを取得
        /// </summary>
        /// <param name="mirrorType">検索するミラータイプ</param>
        /// <returns>対応するミラーオブジェクト（見つからない場合はnull）</returns>
        private GameObject FindMirrorObject(MirrorType mirrorType)
        {
            switch (mirrorType)
            {
                case MirrorType.SetupMirror:
                    return SetupMirror;
                case MirrorType.Off:
                    return OffMirror;
                case MirrorType.LQ:
                    return LQMirror;
                case MirrorType.HQ:
                    return HQMirror;
                default:
                    return null;
            }
        }

        /// <summary>
        /// ミラーオブジェクトのアクティブ状態を安全に設定
        /// </summary>
        /// <param name="mirrorObject">対象のミラーオブジェクト</param>
        /// <param name="isActive">設定するアクティブ状態</param>
        private void SetMirrorActive(GameObject mirrorObject, bool isActive)
        {
            if (mirrorObject != null)
            {
                mirrorObject.SetActive(isActive);
            }
        }

        /// <summary>
        /// セットアップミラーの位置・回転・スケールを対象オブジェクトにコピー
        /// </summary>
        /// <param name="targetObject">コピー先のオブジェクト</param>
        private void SyncTransformFromSetupMirror(GameObject targetObject)
        {
            if (SetupMirror == null || targetObject == null)
                return;

            var setupTransform = SetupMirror.transform;
            var targetTransform = targetObject.transform;

            targetTransform.position = setupTransform.position;
            targetTransform.rotation = setupTransform.rotation;
            targetTransform.localScale = setupTransform.localScale;
        }

        #endregion

        #region Glass オブジェクト制御

        /// <summary>
        /// Glassオブジェクトの表示状態とTransformを更新します。
        /// </summary>
        /// <param name="mirrorType">現在のミラータイプ</param>
        private void UpdateGlassObjectVisibilityAndTransform(MirrorType mirrorType)
        {
            if (GlassObject == null)
            {
                return;
            }

            // noGlass の場合は常に非表示
            if (glassObjectType == GlassObjectType.noGlass)
            {
                GlassObject.SetActive(false);
                return;
            }

            // Glass の場合は常に表示
            GlassObject.SetActive(true);
            ApplyGlassMaterialOverridesToGlassObject();
            SyncGlassTransformFromSetupMirror(GlassObject);
        }

        /// <summary>
        /// Glassオブジェクトのマテリアルプロパティを適用します。
        /// </summary>
        private void ApplyGlassMaterialOverridesToGlassObject()
        {
            if (GlassObject == null)
            {
                return;
            }

            var renderer = GlassObject.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            var shared = renderer.sharedMaterial;
            if (shared == null)
            {
                return;
            }

            // 他のマテリアルを変更しないため、MaterialPropertyBlock で tiling を上書きする
            if (_glassPropertyBlock == null)
            {
                _glassPropertyBlock = new MaterialPropertyBlock();
            }

            renderer.GetPropertyBlock(_glassPropertyBlock);

            // GlassShader は '_Value' (Int) をタイリング数として使用
            if (shared.HasProperty(MirrorCoverValuePropertyName))
            {
                int tiling = Mathf.Clamp((int)glassTiling, 1, 15);
                _glassPropertyBlock.SetInt(MirrorCoverValuePropertyName, tiling);
            }

            renderer.SetPropertyBlock(_glassPropertyBlock);
        }

        /// <summary>
        /// MirrorCover(=GlassObject) のマテリアルパラメータ "_Value"（タイリング数）を更新します（MaterialPropertyBlockで安全に更新）。
        /// </summary>
        /// <param name="value">1..15 を想定（floatで渡されても丸めます）</param>
        public void SetMirrorCoverValue(float value)
        {
            int tiling = Mathf.Clamp(Mathf.RoundToInt(value), 1, 15);
            glassTiling = (GlassTiling)tiling;
            ApplyGlassMaterialOverridesToGlassObject();
        }

        /// <summary>
        /// Glassのタイリング数を設定します。
        /// </summary>
        /// <param name="tiling">タイリング数（1～15）</param>
        public void SetGlassTiling(GlassTiling tiling)
        {
            glassTiling = tiling;
            ApplyGlassMaterialOverridesToGlassObject();
        }

        #endregion

        #region SetupMirror 法線処理

        /// <summary>
        /// SetupMirrorのメッシュ法線の平均をキャッシュします。
        /// </summary>
        private void CacheSetupMirrorAverageNormalLocal()
        {
            _setupMirrorAverageNormalLocalValid = false;
            _setupMirrorAverageNormalLocal = Vector3.forward;

            if (SetupMirror == null)
            {
                return;
            }

            var meshFilter = SetupMirror.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                return;
            }

            var mesh = meshFilter.sharedMesh;
            if (mesh == null)
            {
                return;
            }

            var normals = mesh.normals;
            if (normals == null || normals.Length == 0)
            {
                return;
            }

            int maxSamples = 64;
            int step = 1;
            if (normals.Length > maxSamples)
            {
                step = normals.Length / maxSamples;
                if (step < 1) step = 1;
            }

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < normals.Length; i += step)
            {
                sum += normals[i];
            }

            if (sum.sqrMagnitude < 0.000001f)
            {
                return;
            }

            _setupMirrorAverageNormalLocal = sum.normalized;
            _setupMirrorAverageNormalLocalValid = true;
        }

        /// <summary>
        /// SetupMirrorの前方法線をワールド座標で取得します。
        /// </summary>
        /// <returns>ワールド座標での前方法線</returns>
        private Vector3 GetSetupMirrorFrontNormalWorld()
        {
            if (!_setupMirrorAverageNormalLocalValid)
            {
                CacheSetupMirrorAverageNormalLocal();
            }

            if (SetupMirror == null)
            {
                return Vector3.forward;
            }

            Vector3 localNormal = _setupMirrorAverageNormalLocalValid ? _setupMirrorAverageNormalLocal : Vector3.forward;
            Vector3 worldNormal = SetupMirror.transform.TransformDirection(localNormal);

            if (worldNormal.sqrMagnitude < 0.000001f)
            {
                worldNormal = SetupMirror.transform.forward;
            }

            return worldNormal.normalized;
        }

        /// <summary>
        /// GlassオブジェクトのTransformをSetupMirrorに同期させます。
        /// </summary>
        /// <param name="targetObject">同期先のオブジェクト</param>
        private void SyncGlassTransformFromSetupMirror(GameObject targetObject)
        {
            if (SetupMirror == null || targetObject == null)
            {
                return;
            }

            var setupTransform = SetupMirror.transform;
            var targetTransform = targetObject.transform;

            // SetupMirror の表面法線方向へオフセットし、常に前面（ZFight回避）に配置する
            Vector3 frontNormalWorld = GetSetupMirrorFrontNormalWorld();
            targetTransform.position = setupTransform.position + (frontNormalWorld * GlassFrontOffset);
            targetTransform.rotation = setupTransform.rotation;
            targetTransform.localScale = setupTransform.localScale;
        }

        #endregion

        #region LQミラー補助処理

        /// <summary>
        /// LQ_BackObjectをセットアップする（LQミラー専用処理）
        /// </summary>
        private void SetupLQBackObject()
        {
            if (LQ_BackObject == null || SetupMirror == null || this == null)
                return;

            // LQ_BackObjectをアクティブにする
            LQ_BackObject.SetActive(true);

            // SetupMirrorの中心位置に移動
            LQ_BackObject.transform.position = SetupMirror.transform.position;

            // ワールド空間のスケールを100倍に設定（1:1:1の比率）
            // 半径1の球を半径100の球にする
            Vector3 targetWorldScale = new Vector3(100f, 100f, 100f);
            Vector3 parentScale = LQ_BackObject.transform.parent != null
                ? LQ_BackObject.transform.parent.lossyScale
                : Vector3.one;

            LQ_BackObject.transform.localScale = new Vector3(
                targetWorldScale.x / parentScale.x,
                targetWorldScale.y / parentScale.y,
                targetWorldScale.z / parentScale.z
            );
        }

        #endregion

        #region アルファ制御

        /// <summary>
        /// 各ミラーのシェーダーの_Alphaプロパティを更新する
        /// </summary>
        private void UpdateMirrorAlpha()
        {
            UpdateMirrorAlphaForObject(LQMirror);
            UpdateMirrorAlphaForObject(HQMirror);
        }

        /// <summary>
        /// 指定されたミラーオブジェクトのシェーダーの_Alphaプロパティを更新する
        /// </summary>
        /// <param name="mirrorObject">対象のミラーオブジェクト</param>
        private void UpdateMirrorAlphaForObject(GameObject mirrorObject)
        {
            if (mirrorObject == null) return;

            var meshRenderer = mirrorObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null) return;

            var material = GetMirrorMaterialForWrite(mirrorObject);
            if (material == null) return;

            // CustomMirrorシェーダーかどうかチェック
            if (material.shader != null && material.shader.name.Contains("CustomMirror"))
            {
                // ミラータイプに応じて適切な最大不透明度を取得
                float maxOpacity = GetMaxOpacityForMirror(mirrorObject);

                // mirrorPowerの値を最大不透明度で調整して_Alphaプロパティに設定
                float targetAlpha = mirrorPower * maxOpacity;
                material.SetFloat(ShaderAlpha, targetAlpha);

                // 通常時は _AlphaSecond を常に 1 に保つ（アニメ中は別途制御）
                if (!isSwitchAnimating)
                {
                    material.SetFloat(ShaderAlphaSecond, 1f);
                }
            }
        }

        /// <summary>
        /// ミラーオブジェクトに応じた最大不透明度を取得する
        /// </summary>
        /// <param name="mirrorObject">対象のミラーオブジェクト</param>
        /// <returns>対応する最大不透明度</returns>
        private float GetMaxOpacityForMirror(GameObject mirrorObject)
        {
            if (mirrorObject == LQMirror)
                return maxOpacity;
            else if (mirrorObject == HQMirror)
                return maxOpacity;
            else
                return 1.0f; // デフォルト値
        }

        #endregion

        #region エディタサポート

#if UNITY_EDITOR
        /// <summary>
        /// エディタ専用：sharedMaterialを直接更新する（OnValidateから呼ばれる）
        /// </summary>
        private void UpdateMirrorAlphaForEditor()
        {
            UpdateMirrorAlphaForObjectEditor(LQMirror);
            UpdateMirrorAlphaForObjectEditor(HQMirror);
        }

        /// <summary>
        /// エディタ専用：指定されたミラーオブジェクトのsharedMaterialを更新する
        /// </summary>
        private void UpdateMirrorAlphaForObjectEditor(GameObject mirrorObject)
        {
            if (mirrorObject == null) return;

            var meshRenderer = mirrorObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null) return;

            var material = meshRenderer.sharedMaterial;
            if (material == null) return;

            if (material.shader != null && material.shader.name.Contains("CustomMirror"))
            {
                float maxOpacity = GetMaxOpacityForMirror(mirrorObject);
                float targetAlpha = mirrorPower * maxOpacity;
                material.SetFloat(ShaderAlpha, targetAlpha);
                material.SetFloat(ShaderAlphaSecond, 1f);
            }
        }
#endif

        /// <summary>
        /// Inspector上で値が変更されたときに自動実行される処理
        /// </summary>
        private void OnValidate()
        {
            // オブジェクトが破棄されている場合は処理をスキップ
            if (this == null) return;

            maxOpacity = Mathf.Clamp01(maxOpacity);

            EnsureMirrorRangeConsistency();

            CacheMirrorReflections();

            // SetupMirror が差し替わった/メッシュが変わった場合に備えて法線キャッシュを更新
            CacheSetupMirrorAverageNormalLocal();

#if UNITY_EDITOR
            // エディタ上でミラータイプが変更されたら即座に反映
            ApplyMirrorSettings(previewMirrorType);

            // mirrorPower または maxOpacity が変更されたら各ミラーのアルファ値を更新（エディタではsharedMaterialを使用）
            UpdateMirrorAlphaForEditor();
#endif

            // MirrorTriggerの位置・回転・スケールを更新
            UpdateMirrorTrigger();

            // GlassObject の表示/Transform を更新
            UpdateGlassObjectVisibilityAndTransform(_activeMirrorType);

            // GlassObject(=MirrorCover) のマテリアル上書きを更新（_Value / tiling）
            ApplyGlassMaterialOverridesToGlassObject();

            // MirrorTriggerの可視化を更新（エディタでのみ）
            UpdateMirrorTriggerVisibility(triggerVisualize);

#if UNITY_EDITOR
            // SetupMirrorのTransform情報を更新（エディタのみ）
            InitializeSetupMirrorTransform();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// SetupMirrorのTransform情報を初期化/更新する（エディタ専用）
        /// </summary>
        private void InitializeSetupMirrorTransform()
        {
            if (SetupMirror != null)
            {
                lastSetupMirrorPosition = SetupMirror.transform.position;
                lastSetupMirrorRotation = SetupMirror.transform.rotation;
                lastSetupMirrorScale = SetupMirror.transform.localScale;
            }
        }

        /// <summary>
        /// SetupMirrorのTransformが変更されたかをチェックし、変更されていれば関連オブジェクトを更新する（エディタ専用）
        /// </summary>
        private void CheckSetupMirrorTransformChange()
        {
            if (SetupMirror == null) return;

            var currentTransform = SetupMirror.transform;
            bool hasChanged = false;

            // 位置の変更をチェック（小さな変化も検知）
            if (Vector3.Distance(currentTransform.position, lastSetupMirrorPosition) > 0.001f)
            {
                lastSetupMirrorPosition = currentTransform.position;
                hasChanged = true;
            }

            // 回転の変更をチェック（小さな変化も検知）
            if (Quaternion.Angle(currentTransform.rotation, lastSetupMirrorRotation) > 0.001f)
            {
                lastSetupMirrorRotation = currentTransform.rotation;
                hasChanged = true;
            }

            // スケールの変更をチェック（小さな変化も検知）
            if (Vector3.Distance(currentTransform.localScale, lastSetupMirrorScale) > 0.001f)
            {
                lastSetupMirrorScale = currentTransform.localScale;
                hasChanged = true;
            }

            // 変更があった場合は関連オブジェクトを更新
            if (hasChanged)
            {
                UpdateMirrorTrigger();
            }
        }
#endif

        #endregion

        #region トリガーエリア制御

        /// <summary>
        /// MirrorStartAreaとMirrorFullAreaの位置・回転・スケールをSetupMirrorに基づいて更新する
        /// </summary>
        private void UpdateMirrorTrigger()
        {
            UpdateMirrorStartArea();
            UpdateMirrorFullArea();

            // SetupMirror 基準で動くものはここでまとめて更新
            UpdateGlassObjectVisibilityAndTransform(_activeMirrorType);
        }

        /// <summary>
        /// MirrorStartAreaの位置・回転・スケールをSetupMirrorに基づいて更新する
        /// </summary>
        private void UpdateMirrorStartArea()
        {
            if (MirrorStartArea == null || SetupMirror == null)
                return;

            var setupTransform = SetupMirror.transform;
            var startAreaTransform = MirrorStartArea.transform;

            // 回転をSetupMirrorと同じにする
            startAreaTransform.rotation = setupTransform.rotation;

            // スケールを設定（横はSetupMirror+mirrorSideMargin_StartArea、縦は98%、奥行きはmirrorRange_StartArea）
            startAreaTransform.localScale = new Vector3(
                setupTransform.localScale.x + mirrorSideMargin_StartArea * 2f, // 両側に余白を追加
                setupTransform.localScale.y * 0.98f, // ZFighting回避のため98%に設定
                mirrorRange_StartArea
            );

            // 位置を設定（SetupMirrorの後面側からmirrorRange_StartAreaの長さ分伸びるように）
            // トリガーの中心がミラーの後面側からmirrorRange_StartArea/2の位置になるように配置
            // ZFighting回避のため、StartAreaを少し手前にオフセット
            Vector3 offsetPosition = -setupTransform.forward * (mirrorRange_StartArea * 0.5f + 0.005f);
            startAreaTransform.position = setupTransform.position + offsetPosition;
        }

        /// <summary>
        /// MirrorFullAreaの位置・回転・スケールをSetupMirrorに基づいて更新する
        /// </summary>
        private void UpdateMirrorFullArea()
        {
            if (MirrorFullArea == null || SetupMirror == null)
                return;

            var setupTransform = SetupMirror.transform;
            var fullAreaTransform = MirrorFullArea.transform;

            // 回転をSetupMirrorと同じにする
            fullAreaTransform.rotation = setupTransform.rotation;

            // スケールを設定（横はSetupMirror+mirrorSideMargin_FullArea、縦は99%、奥行きはmirrorRange_FullArea）
            fullAreaTransform.localScale = new Vector3(
                setupTransform.localScale.x + mirrorSideMargin_FullArea * 2f, // 両側に余白を追加
                setupTransform.localScale.y * 0.99f, // ZFighting回避のため99%に設定
                mirrorRange_FullArea
            );

            // 位置を設定（SetupMirrorの後面側からmirrorRange_FullAreaの長さ分伸びるように）
            // トリガーの中心がミラーの後面側からmirrorRange_FullArea/2の位置になるように配置
            // ZFighting回避のため、FullAreaを少し奥にオフセット
            Vector3 offsetPosition = -setupTransform.forward * (mirrorRange_FullArea * 0.5f + 0.015f);
            fullAreaTransform.position = setupTransform.position + offsetPosition;
        }

        /// <summary>
        /// トリガーの表示/非表示を設定します（エディタ用）。
        /// </summary>
        /// <param name="mode">表示モード</param>
        private void UpdateMirrorTriggerVisibility(TriggerVisualizeMode mode)
        {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (AreaPreviewObject != null)
            {
                AreaPreviewObject.SetActive(mode == TriggerVisualizeMode.On);
            }
#endif
        }

        #endregion

        #region 公開取得メソッド

        /// <summary>
        /// 現在アクティブなミラーオブジェクトを取得します。
        /// </summary>
        /// <returns>現在アクティブなミラーオブジェクト</returns>
        public GameObject GetActiveMirrorObject()
        {
            return FindMirrorObject(_activeMirrorType);
        }

        /// <summary>
        /// SetupMirrorのTransformを取得する（外部アクセス用）
        /// </summary>
        public Transform GetSetupMirrorTransform()
        {
            return SetupMirror != null ? SetupMirror.transform : null;
        }

        /// <summary>
        /// 現在のミラータイプを取得する（外部アクセス用）
        /// </summary>
        public MirrorType GetActiveMirrorType()
        {
            return _activeMirrorType;
        }

        #endregion

        #region プレイヤーイベント処理

        /// <summary>
        /// プレイヤーがミラーエリアに入ったときの処理
        /// </summary>
        public void OnPlayerEnterMirrorArea()
        {
            isPlayerInMirrorArea = true;

            // maxOpacity==0 のときは MirrorTrigger 経由でもミラーを出さない
            if (maxOpacity <= MaxOpacityDisableEpsilon)
            {
                CancelMirrorSwitchAnimation();
                HideAllMirrors();
                HideSetupMirror();
                ResetAlphaSecondForAllMirrors(1f);
                return;
            }

            if (_activeMirrorType == MirrorType.SetupMirror)
            {
                SetMirror(MirrorType.LQ);
            }
            else
            {
                SetMirror(_activeMirrorType);
            }
        }

        /// <summary>
        /// プレイヤーがミラーエリアから出たときの処理。
        /// </summary>
        public void OnPlayerExitMirrorArea()
        {
            isPlayerInMirrorArea = false;

            CancelMirrorSwitchAnimation();

            SetMirrorPower(0.0f);
            HideAllMirrors();

            ResetAlphaSecondForAllMirrors(1f);
        }

        #endregion

        #region 距離計算・パワー制御

        /// <summary>
        /// 指定された位置からミラーエリア境界までの距離を計算します。
        /// </summary>
        /// <param name="worldPosition">ワールド座標での位置</param>
        /// <returns>ミラーエリア境界からの距離</returns>
        public float CalculateDistanceFromMirrorArea(Vector3 worldPosition)
        {
            if (SetupMirror == null || MirrorStartArea == null || MirrorFullArea == null) return 0f;

            // StartAreaとFullAreaの距離を計算
            float startAreaDistance = CalculateDistanceToBox(worldPosition, MirrorStartArea);
            float fullAreaDistance = CalculateDistanceToBox(worldPosition, MirrorFullArea);

            float distance;
            if (IsInsideBox(worldPosition, MirrorFullArea))
            {
                distance = fullAreaDistance;
            }
            else if (IsInsideBox(worldPosition, MirrorStartArea))
            {
                distance = fullAreaDistance;
            }
            else
            {
                distance = startAreaDistance;
            }

            return distance;
        }

        /// <summary>
        /// 指定された位置がボックス内にあるかどうかを判定する
        /// </summary>
        private bool IsInsideBox(Vector3 worldPosition, GameObject boxObject)
        {
            if (boxObject == null) return false;

            var boxTransform = boxObject.transform;
            var boxCollider = boxObject.GetComponent<BoxCollider>();

            // BoxCollider がある場合は center/size を使用（Triggerの実体に一致）
            Vector3 localCenter = boxCollider != null ? boxCollider.center : Vector3.zero;
            Vector3 halfSize = boxCollider != null ? (boxCollider.size * 0.5f) : (boxTransform.localScale * 0.5f);

            // ワールド座標をボックスローカル座標（center基準）に変換
            Vector3 localPosition = boxTransform.InverseTransformPoint(worldPosition) - localCenter;

            return Mathf.Abs(localPosition.x) <= halfSize.x &&
                Mathf.Abs(localPosition.y) <= halfSize.y &&
                Mathf.Abs(localPosition.z) <= halfSize.z;
        }

        /// <summary>
        /// 指定された位置から指定されたボックスオブジェクトの境界までの距離を計算する
        /// 側面（X軸）と後面（Z軸の負方向）のみで計測。床面・天面・前面（ミラー側）は除外。
        /// </summary>
        /// <param name="worldPosition">ワールド座標での位置</param>
        /// <param name="boxObject">ボックスオブジェクト</param>
        /// <returns>ボックス境界からの距離（内側の場合は境界までの最短距離、外側の場合は最近点までの距離）</returns>
        private float CalculateDistanceToBox(Vector3 worldPosition, GameObject boxObject)
        {
            if (boxObject == null) return float.MaxValue;

            var boxTransform = boxObject.transform;
            var boxCollider = boxObject.GetComponent<BoxCollider>();

            // BoxCollider がある場合は center/size を使用（Triggerの実体に一致）
            Vector3 localCenter = boxCollider != null ? boxCollider.center : Vector3.zero;
            Vector3 halfSize = boxCollider != null ? (boxCollider.size * 0.5f) : (boxTransform.localScale * 0.5f);

            // ワールド座標をボックスローカル座標（center基準）に変換
            Vector3 localPosition = boxTransform.InverseTransformPoint(worldPosition) - localCenter;

            // 対象面上の候補点（左側面・右側面・後面）を作り、最短距離を採用
            Vector3 leftFacePoint = new Vector3(
                -halfSize.x,
                Mathf.Clamp(localPosition.y, -halfSize.y, halfSize.y),
                Mathf.Clamp(localPosition.z, -halfSize.z, halfSize.z)
            );

            Vector3 rightFacePoint = new Vector3(
                halfSize.x,
                Mathf.Clamp(localPosition.y, -halfSize.y, halfSize.y),
                Mathf.Clamp(localPosition.z, -halfSize.z, halfSize.z)
            );

            Vector3 backFacePoint = new Vector3(
                Mathf.Clamp(localPosition.x, -halfSize.x, halfSize.x),
                Mathf.Clamp(localPosition.y, -halfSize.y, halfSize.y),
                -halfSize.z
            );

            float dLeft = Vector3.Distance(localPosition, leftFacePoint);
            float dRight = Vector3.Distance(localPosition, rightFacePoint);
            float dBack = Vector3.Distance(localPosition, backFacePoint);

            return Mathf.Min(dLeft, Mathf.Min(dRight, dBack));
        }

        /// <summary>
        /// 指定された位置から指定されたボックスオブジェクトの最接近点をワールド座標で計算する
        /// 側面（X軸）と後面（Z軸の負方向）のみで計算。床面・天面・前面（ミラー側）は除外。
        /// </summary>
        /// <param name="worldPosition">ワールド座標での位置</param>
        /// <param name="boxObject">ボックスオブジェクト</param>
        /// <returns>ボックスの最接近点のワールド座標</returns>
        private Vector3 CalculateClosestPointOnBox(Vector3 worldPosition, GameObject boxObject)
        {
            if (boxObject == null) return worldPosition;

            var boxTransform = boxObject.transform;
            var boxCollider = boxObject.GetComponent<BoxCollider>();

            // BoxCollider がある場合は center/size を使用（Triggerの実体に一致）
            Vector3 localCenter = boxCollider != null ? boxCollider.center : Vector3.zero;
            Vector3 halfSize = boxCollider != null ? (boxCollider.size * 0.5f) : (boxTransform.localScale * 0.5f);

            // ワールド座標をボックスローカル座標（center基準）に変換
            Vector3 localPosition = boxTransform.InverseTransformPoint(worldPosition) - localCenter;

            // 側面（X=±half）と後面（Z=-half）のみの候補点を作り、最短のものを採用
            Vector3 leftFacePoint = new Vector3(
                -halfSize.x,
                Mathf.Clamp(localPosition.y, -halfSize.y, halfSize.y),
                Mathf.Clamp(localPosition.z, -halfSize.z, halfSize.z)
            );

            Vector3 rightFacePoint = new Vector3(
                halfSize.x,
                Mathf.Clamp(localPosition.y, -halfSize.y, halfSize.y),
                Mathf.Clamp(localPosition.z, -halfSize.z, halfSize.z)
            );

            Vector3 backFacePoint = new Vector3(
                Mathf.Clamp(localPosition.x, -halfSize.x, halfSize.x),
                Mathf.Clamp(localPosition.y, -halfSize.y, halfSize.y),
                -halfSize.z
            );

            float dLeft = Vector3.Distance(localPosition, leftFacePoint);
            float dRight = Vector3.Distance(localPosition, rightFacePoint);
            float dBack = Vector3.Distance(localPosition, backFacePoint);

            Vector3 closestLocalPoint;
            if (dLeft <= dRight && dLeft <= dBack)
                closestLocalPoint = leftFacePoint;
            else if (dRight <= dBack)
                closestLocalPoint = rightFacePoint;
            else
                closestLocalPoint = backFacePoint;

            // centerを戻してワールド座標へ
            return boxTransform.TransformPoint(closestLocalPoint + localCenter);
        }

        /// <summary>
        /// ミラーエリアからの距離に基づいてMirrorPowerを計算する
        /// </summary>
        /// <param name="distanceFromMirror">ミラーエリア境界からの距離</param>
        /// <returns>計算されたMirrorPower（0.0f～1.0f）</returns>
        public float CalculateMirrorPowerFromDistance(float distanceFromMirror)
        {
            // 固定関係：FullArea（内側、最大パワー）、StartArea（外側、最小パワー）
            float fullAreaHalfDepth = mirrorRange_FullArea * 0.5f;
            float startAreaHalfDepth = mirrorRange_StartArea * 0.5f;

            // FullArea内（距離がfullAreaHalfDepth以下）：最大パワー
            if (distanceFromMirror <= fullAreaHalfDepth)
            {
                return 1.0f;
            }

            // StartArea外（距離がstartAreaHalfDepthを超える）：最小パワー
            if (distanceFromMirror >= startAreaHalfDepth)
            {
                return 0.0f;
            }

            // FullAreaとStartAreaの間：線形補間
            float normalizedDistance = (distanceFromMirror - fullAreaHalfDepth) / (startAreaHalfDepth - fullAreaHalfDepth);
            float calculatedPower = Mathf.Lerp(1.0f, 0.0f, normalizedDistance);

            return Mathf.Clamp01(calculatedPower);
        }

        /// <summary>
        /// プレイヤー位置から StartArea/FullArea への距離を使って MirrorPower を計算する。
        /// - StartArea（外側）表面に近いほど 0
        /// - FullArea（内側）表面に近いほど 1
        /// 距離は「側面 + 後面のみ」を対象とした最近点距離を使用する。
        /// </summary>
        /// <param name="worldPosition">ワールド座標での位置</param>
        /// <returns>MirrorPower（0.0f～1.0f）</returns>
        public float CalculateMirrorPowerFromPosition(Vector3 worldPosition)
        {
            if (MirrorStartArea == null || MirrorFullArea == null)
            {
                return 0f;
            }

            // FullArea（内側）に入っているなら最大
            if (IsInsideBox(worldPosition, MirrorFullArea))
            {
                return 1f;
            }

            // StartArea（外側）より外なら最小
            if (!IsInsideBox(worldPosition, MirrorStartArea))
            {
                return 0f;
            }

            // StartArea内かつFullArea外：両エリア表面への距離比で補間
            float startDist = CalculateDistanceToBox(worldPosition, MirrorStartArea);
            float fullDist = CalculateDistanceToBox(worldPosition, MirrorFullArea);
            float denom = startDist + fullDist;

            if (denom <= 0.0001f)
            {
                // ほぼ同一点（理論上は起きにくい）
                return 1f;
            }

            // Start表面: startDist=0 => 0
            // Full表面: fullDist=0 => 1
            return Mathf.Clamp01(startDist / denom);
        }

        /// <summary>
        /// MirrorPowerを設定し、シェーダーのアルファ値を更新する
        /// </summary>
        /// <param name="newPower">新しいMirrorPower値（0.0f～1.0f）</param>
        public void SetMirrorPower(float newPower)
        {
            mirrorPower = Mathf.Clamp01(newPower);
            UpdateMirrorAlpha();
        }

        /// <summary>
        /// LQ/HQミラーの最大不透明度（maxOpacity）を設定し、シェーダーのアルファ値を更新する
        /// </summary>
        public void SetMaxOpacity(float newMaxOpacity)
        {
            maxOpacity = Mathf.Clamp01(newMaxOpacity);

            // maxOpacity が 0 になったら、SetTemporarilyHiddenByExternalControl(true) と同等の処理を行う
            // （Controller_MirrorOpacity から両方呼ばれる想定だが、SetMaxOpacity単体でも動作するように）
            if (maxOpacity <= MaxOpacityDisableEpsilon)
            {
                CancelMirrorSwitchAnimation();
                SetMirrorPower(0.0f);
                HideAllMirrors();
                HideSetupMirror();
                ResetAlphaSecondForAllMirrors(1f);
            }
            else
            {
                UpdateMirrorAlpha();
            }
        }

        /// <summary>
        /// 外部（例: SliderSwitch）から、現在アクティブなミラーを一時的に非表示/復帰します。
        /// </summary>
        /// <param name="isHidden">trueで非表示、falseで復帰</param>
        public void SetTemporarilyHiddenByExternalControl(bool isHidden)
        {
            if (_temporarilyHiddenByExternalControl == isHidden)
            {
                return;
            }

            _temporarilyHiddenByExternalControl = isHidden;

            if (isHidden)
            {
                // アニメ中でも確実に止めて、現在表示中のミラーを全て落とす
                CancelMirrorSwitchAnimation();
                SetMirrorPower(0.0f);
                HideAllMirrors();
                HideSetupMirror();
                ResetAlphaSecondForAllMirrors(1f);
                return;
            }

            // 復帰：そもそも表示条件を満たさなければ何もしない
            if (!isPlayerInMirrorArea) return;
            if (maxOpacity <= MaxOpacityDisableEpsilon) return;
            if (_activeMirrorType == MirrorType.Off) return;

            // SetupMirrorは実運用で表示しない設計のため、復帰時はLQへ
            if (_activeMirrorType == MirrorType.SetupMirror)
            {
                SetMirror(MirrorType.LQ);
            }
            else
            {
                SetMirror(_activeMirrorType);
            }
        }

        #endregion
    }
}
