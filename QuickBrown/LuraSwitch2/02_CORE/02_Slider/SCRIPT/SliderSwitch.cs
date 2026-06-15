using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDKBase.Editor.Attributes;
using VRC.SDK3.Components;
using VRC.SDK3.Persistence;
using UnityEngine.Rendering.PostProcessing;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
#endif

namespace QuickBrown.LuraSwitch
{
    public enum SliderSwitchSyncMode
    {
        Local = 0,
        Global = 1,
        LocalSave = 2,
    }

    public enum SliderSwitchVisualMode
    {
        [InspectorName("2D_UI")]
        Mode2DUI = 1,

        [InspectorName("2D_Pickup")]
        Mode2DPickup = 2,

        [InspectorName("3D")]
        Mode3D = 0,
    }

    public enum SliderSwitchOrientationMode
    {
        Vertical = 0,
        Horizontal = 1,
    }

    public enum UseText
    {
        Off = 0,
        On = 1,
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SliderSwitch : UdonSharpBehaviour
    {
        #region フィールド

        [HelpBox("JP:\nこのUdonは見た目や同期、セーブ設定のみを管理します。\n実際のターゲットの参照は\n=====TargetSetting=====\nで行ってください。\n\nEN:\nThis Udon only manages appearance, synchronization, and save settings.\nActual target references should be done in\n=====TargetSetting=====", HelpBoxAttribute.MessageType.Info)]
        [Header("■ SliderMode")]
        [SerializeField] private SliderSwitchSyncMode syncMode = SliderSwitchSyncMode.Local;
        [HelpBox("JP:\nLocal: ローカルのみ同期\nGlobal: 全体同期\nLocalSave: 各プレイヤーの状態を保存/復元\n\nEN:\nLocal: Local sync only\nGlobal: Global sync\nLocalSave: Save/restore each player's state", HelpBoxAttribute.MessageType.Info)]
        [SerializeField] private SliderSwitchVisualMode sliderVisualMode = SliderSwitchVisualMode.Mode3D;
        [SerializeField] private SliderSwitchOrientationMode orientationMode = SliderSwitchOrientationMode.Vertical;

        [Space(10)]
        [SerializeField, Range(0f, 1f)] private float sliderDefaultValue = 0f;
        [HelpBox("JP:\nスライダーの初期値を設定します（0.0～1.0）\nエディタ上では即座に結果が反映されます。\nセーブデータがある場合はそちらが優先されます。\n\nEN:\nSet the initial value of the slider (0.0 to 1.0)\nResults are reflected immediately in the editor.\nSave data takes priority if available.")]


        [Tooltip("スライダーの刻み幅。0.1の場合10%ずつステップします。")]
        [SerializeField] private float sliderStep = 0.1f;

        [Header("--------------------------------------------------")]

        [Space(10)]
        [Header("■ Slider Text")]
        [HelpBox("JP:\nスライダーに表示するテキストの設定です。UseTextをOnにすると、2D/3Dテキストが表示されます。\n\nEN:\nSettings for text displayed on the slider. When UseText is On, 2D/3D text will be displayed.")]
        [SerializeField] private UseText useText = UseText.On;

        [Tooltip("有効なとき、Start/OnValidate で Text の表示/文字を自動更新します。")]
        [SerializeField] private bool TextAutoUpdate = true;

        [Tooltip("2D/3D の表示テキストに反映する文字列です（OnValidateで即時反映）。")]
        [SerializeField, TextArea] private string sliderText;

        [Header("■ Interaction Text")]
        [HelpBox("JP:\nVRChatでUseしようとするときに浮かび上がるテキストです。\nTextAutoUpdateが有効な場合、自動更新されます。\n\nEN:\nText that appears when trying to Use in VRChat.\nAutomatically updated when TextAutoUpdate is enabled.")]
        [SerializeField] private string slider_InteractionText = "Text";
        [Header("--------------------------------------------------")]

        [Space(10)]
        [Header("■ LocalSave")]
        [HelpBox("JP:\nLocalSaveモード使用時にのみ機能します。\n各プレイヤーの状態を保存/復元するためのキーを設定します。\n他のものと重複しない名前を付けてください。\n\nEN:\nFunctions only when using LocalSave mode.\nSet the key for saving/restoring each player's state.\nPlease use a unique name that does not conflict with others.")]
        [SerializeField] private string persistanceKey = "SliderSwitch_Value";

        [Space(100)]
        [Header("--------------------System（以下変更不要）--------------------")]

        [Header("■ Controller")]
        [SerializeField] private UdonSharpBehaviour controller;

        [Header("■ Visual Objects")]
        [Tooltip("3Dモードで有効にするオブジェクト（未設定なら何もしません）")]
        [SerializeField] private GameObject _3DModeObject;

        [Tooltip("2Dモードで有効にするオブジェクト（未設定なら何もしません）")]
        [SerializeField] private GameObject _2DModeObject;

        [Space(10)]
        [Header("■ 3D Pickup")]
        [Tooltip("3Dピックアップモード用のオブジェクト")]
        [SerializeField] private GameObject _3DPickup;

        [Space(10)]
        [Header("■ 3D Slider")]
        [Tooltip("3Dスライダーで動かす対象のTransform")]
        [SerializeField] private Transform sliderTarget;

        [Tooltip("3Dピックアップ用のコライダー")]
        [SerializeField] private Collider pickupCollider;

        [Tooltip("スライダーの最小位置（ローカル座標）")]
        [SerializeField] private float minPosition = -0.5f;

        [Tooltip("スライダーの最大位置（ローカル座標）")]
        [SerializeField] private float maxPosition = 0.5f;

        [Space(10)]
        [Header("■ Gauge")]
        [Tooltip("スライダーのゲージランプ用のMeshRenderer")]
        [SerializeField] private MeshRenderer sliderLamp;

        [Space(10)]
        [Header("■ Sound")]
        [Tooltip("スライダー操作時の効果音を再生するAudioSource")]
        [SerializeField] private AudioSource slideAudioSource;

        [Tooltip("スライダー移動中の効果音")]
        [SerializeField] private AudioClip slideAudioClip_clip;

        [Tooltip("スライダーに触れた時の効果音")]
        [SerializeField] private AudioClip slideAudioClip_touch;

        [Tooltip("スライダーを離した時の効果音")]
        [SerializeField] private AudioClip slideAudioClip_fix;

        [Space(10)]
        [Header("■ Haptics")]
        [Tooltip("スナップ時にハプティクスを有効にするか")]
        [SerializeField] private bool enableHapticsOnSnap = true;

        [SerializeField] private float hapticsDurationSeconds = 0.03f;
        [SerializeField, Range(0f, 1f)] private float hapticsAmplitude = 0.12f;
        [SerializeField] private float hapticsFrequency = 120f;

        [Space(10)]
        [Header("■ 3D Feel")]
        [Tooltip("3Dスライダーの追従スムーズ時間")]
        [SerializeField] private float followSmoothTime = 0.04f;

        [Tooltip("3Dスライダーの追従最大速度")]
        [SerializeField] private float followMaxSpeed = 25f;

        [Space(10)]
        [Header("■ 2D UI")]
        [Tooltip("2D UIモード用のスライダーコンポーネント")]
        [SerializeField] private UnityEngine.UI.Slider uiSlider;

        [Tooltip("2D UIスライダーのハンドル")]
        [SerializeField] private UnityEngine.RectTransform uiHandle;

        [Tooltip("2D UIスライダーの塗りつぶし画像")]
        [SerializeField] private UnityEngine.UI.Image uiFillImage;

        [Tooltip("2D UIスライダーの当たり判定用コライダー")]
        [SerializeField] private BoxCollider[] sliderColliders;

        [Space(10)]
        [Header("■ 2D UI Hit Thickness")]
        [Tooltip("VRCUIShape 等により自動生成される sliderColliders が薄すぎる場合に、実行時に Z 厚みを倍率で増やします。")]
        [SerializeField] private bool applyUiColliderZThicknessMultiplier = true;

        [Tooltip("sliderColliders の size.z に掛ける倍率です。薄い当たり判定の救済用（例: 10）。")]
        [SerializeField] private float uiColliderZThicknessMultiplier = 10f;

        [Space(10)]
        [Header("■ Text Components")]
        [Tooltip("2D(UGUI)のテキスト表示対象です。\nSlider Text の内容を反映します（OnValidateで即時反映）。")]
        [SerializeField] private TextMeshProUGUI[] _2DSlider_Text;

        [Tooltip("3D(UGUI)のテキスト表示対象です。\nSlider Text の内容を反映します（OnValidateで即時反映）。")]
        [SerializeField] private TextMeshProUGUI[] _3DSlider_Text;

        [Header("■ InteractionText Settings")]
        [Tooltip("InteractionText設定用のVRCPickup配列です。\nTextAutoUpdateが有効な場合、エディタ/実行時に slider_InteractionText を反映します。")]
        [SerializeField] private VRCPickup[] targetPickups;

        [Space(10)]
        [Header("■ Orientation")]
        [Tooltip("Horizontal時に+90度回転させるオブジェクト")]
        [SerializeField, InspectorName("RotateObjects_90")] private Transform[] rotateObjectsPlus90;

        [Tooltip("Horizontal時に-90度回転させるオブジェクト")]
        [SerializeField, InspectorName("RotateObjects_-90")] private Transform[] rotateObjectsMinus90;

        [Tooltip("Horizontal時に回転させるテキストオブジェクト")]
        [SerializeField, InspectorName("rotateTextObjects")] private Transform[] rotateTextObjects;

        [Tooltip("UIハンドルの最小Y位置")]
        [SerializeField] private float uiHandleMinY = -50f;

        [Tooltip("UIハンドルの最大Y位置")]
        [SerializeField] private float uiHandleMaxY = 50f;

        [Tooltip("UIのスムーズ時間")]
        [SerializeField] private float uiSmoothTime = 0.05f;

        [Tooltip("UIのソフトディテント強度")]
        [SerializeField, Range(0f, 1f)] private float uiSoftDetentStrength = 0.25f;

        [Space(10)]

        [HideInInspector] public float sliderValue;

        // Global同期時の補間時間（秒）
        private const float GlobalSyncInterpolationTimeSeconds = 1f;

        // Global同期用：ネットワーク経由で同期される値
        [UdonSynced] private float _syncedSliderValue;

        // Global同期用：セッション中の初回だけ sliderDefaultValue を反映するためのフラグ
        [UdonSynced] private bool _globalInitialized;



        private Vector3 _fixedLocalXZ;
        private VRC_Pickup _pickup;

        private int _lastSnapIndex = -1;
        private float _valueVelocity;
        private bool _isPickedUp;

        // Global同期：補間用
        private bool _isInterpolating;
        private bool _interpolationApplyOutput = true;
        private float _interpolationStartValue;
        private float _interpolationTargetValue;
        private float _interpolationElapsed;
        private float _interpolationDuration; // 動的に設定される補間時間
        private bool _isFirstJoin = true;

        // LocalSave: Restore検出用
        private bool _localSaveRestored;
        private float _localSaveRestoredValue;

        private const float MinStep = 0.0001f;
        private const float HapticsAmplitudeScale = 1f;

        // Tickループ
        private bool _tickScheduled;
        private float _lastTickTime;

        // 2D(UI)ドラッグ中
        private bool _isUiDragging;
        private float _uiTargetValue;
        private float _uiValueVelocity;
        private bool _uiSuppressSliderCallback;
        private Vector2 _uiHandleBaseAnchoredPos;
        private bool _uiHandleBasePosCached;

        private const int LocalSaveScale = 100;

        // SliderLamp（ゲージ）用：共有マテリアルを壊さないため、実行時にインスタンス化してから値を書き込む
        private Material[] _sliderLampInstancedMaterials;
        private bool _sliderLampMaterialReady;
        private float _lastLampValue = -1f;

        // 初回のStart/OnEnableかどうかを判定するフラグ
        private bool _initialized = false;

        // XZ座標のキャッシュが完了したかどうか
        private bool _fixedLocalXZCached = false;


        // SoftDetentは常時有効、デフォルト値で固定
        private const float SoftDetentStrength = 0.25f;
        private const float SoftDetentRangeRatio = 0.2f;

        // SliderLamp の値プロパティ名は固定
        private const string SliderLampValuePropertyName = "_Value";

        // 2DUI操作時のTrackArea拡大用：初期状態保存
        private bool _pickupColliderInitialState;
        private bool _trackAreaStatesCached;
        private Vector3[] _sliderCollidersInitialSize;
        private Vector3[] _sliderCollidersInitialCenter;

        // VRCUIShape が自動生成する薄い BoxCollider を救済するための一度きり適用フラグ
        private bool _uiColliderZThicknessApplied;

        private void ApplySliderText()
        {
            bool active = useText == UseText.On;

            if (_2DSlider_Text != null)
            {
                for (int i = 0; i < _2DSlider_Text.Length; i++)
                {
                    var t = _2DSlider_Text[i];
                    if (t == null)
                    {
                        continue;
                    }

                    t.gameObject.SetActive(active);
                    if (active)
                    {
                        t.text = sliderText;
                    }
                }
            }

            if (_3DSlider_Text != null)
            {
                for (int i = 0; i < _3DSlider_Text.Length; i++)
                {
                    var t = _3DSlider_Text[i];
                    if (t == null)
                    {
                        continue;
                    }

                    t.gameObject.SetActive(active);
                    if (active)
                    {
                        t.text = sliderText;
                    }
                }
            }
        }

        /// <summary>
        /// slider_InteractionText を targetPickups の InteractionText に適用します。
        /// </summary>
        private void ApplyInteractionText()
        {
            if (targetPickups == null || targetPickups.Length == 0)
            {
                return;
            }

            var nextText = BuildInteractionTextWithSyncMode();
            for (int i = 0; i < targetPickups.Length; i++)
            {
                var pickup = targetPickups[i];
                if (pickup == null)
                {
                    continue;
                }

                pickup.InteractionText = nextText;
            }
        }

        private string BuildInteractionTextWithSyncMode()
        {
            var raw = (slider_InteractionText ?? string.Empty).Trim();

            // 既存の "(Local)" 等が末尾以外に残っているケース（ユーザーがサフィックスの後ろに追記した等）でも
            // 重複せず「常に末尾に1つだけ」になるよう、既知サフィックスを全て除去してから付け直す。
            // enum名と数値の両方を除去（実行時は ToString() が数値になるため）
            raw = raw
                .Replace(" (Local)", string.Empty)
                .Replace("(Local)", string.Empty)
                .Replace(" (0)", string.Empty)
                .Replace("(0)", string.Empty)
                .Replace(" (Global)", string.Empty)
                .Replace("(Global)", string.Empty)
                .Replace(" (1)", string.Empty)
                .Replace("(1)", string.Empty)
                .Replace(" (LocalSave)", string.Empty)
                .Replace("(LocalSave)", string.Empty)
                .Replace(" (2)", string.Empty)
                .Replace("(2)", string.Empty)
                .Trim();

            while (raw.Contains("  "))
            {
                raw = raw.Replace("  ", " ");
            }

            // UdonSharpではenum.ToString()が数値を返すため、明示的に文字列化
            string syncModeText;
            if (syncMode == SliderSwitchSyncMode.Local)
            {
                syncModeText = "Local";
            }
            else if (syncMode == SliderSwitchSyncMode.Global)
            {
                syncModeText = "Global";
            }
            else if (syncMode == SliderSwitchSyncMode.LocalSave)
            {
                syncModeText = "LocalSave";
            }
            else
            {
                syncModeText = syncMode.ToString();
            }

            var suffix = " (" + syncModeText + ")";
            if (raw.Length == 0)
            {
                return suffix.TrimStart();
            }

            return raw + suffix;
        }

        #endregion

        /// <summary>
        /// 外部同期やGlobal同期などで補間中か。
        /// SwitchSyncer の変更検出で、補間による値変化を誤検出しないために使用します。
        /// </summary>
        public bool IsInterpolating
        {
            get { return _isInterpolating; }
        }

        /// <summary>
        /// 補間中の値変化が「出力も伴う」補間かどうか。
        /// </summary>
        public bool IsInterpolationApplyingOutput
        {
            get { return _isInterpolating && _interpolationApplyOutput; }
        }

        /// <summary>
        /// LocalSaveの復元がこのセッションで完了しているか（SwitchSyncerが優先適用するために参照）。
        /// </summary>
        public bool HasLocalSaveRestored
        {
            get { return _localSaveRestored; }
        }

        /// <summary>
        /// LocalSaveで復元された値（HasLocalSaveRestored==trueの時のみ有効）。
        /// </summary>
        public float LocalSaveRestoredValue
        {
            get { return _localSaveRestoredValue; }
        }

        // SwitchSyncer 等から「このスライダーはフォロワー（見た目のみ）」として扱いたい場合、
        // Global の OnDeserialization を無視してネットワーク値との喧嘩（ちらつき）を防ぐ。
        private bool _ignoreDeserializationByExternalControl;

        /// <summary>
        /// 外部（例: SwitchSyncer）により、このスライダーの Global デシリアライズ適用を無視するか。
        /// </summary>
        public void SetIgnoreDeserializationByExternalControl(bool ignore)
        {
            _ignoreDeserializationByExternalControl = ignore;
        }

        #region 2DUI TrackArea Control

        /// <summary>
        /// 2DUI操作開始時：TrackAreaを拡大し、PickupColliderを無効化します。
        /// </summary>
        public void UI_ExpandTrackAreaAndDisablePickup()
        {
            if (!_trackAreaStatesCached)
            {
                CacheTrackAreaInitialStates();
            }

            // PickupColliderを無効化
            if (pickupCollider != null)
            {
                pickupCollider.enabled = false;
            }

            // sliderCanvasのサイズは変更しない（Unity UI Sliderの座標計算を正常に保つため）
            // 代わりに、sliderCollidersのサイズだけを拡大して操作範囲を広げる

            // sliderCollidersのサイズとセンターを拡大（Width×3、Height×2）
            if (sliderColliders != null)
            {
                for (int i = 0; i < sliderColliders.Length; i++)
                {
                    if (sliderColliders[i] != null)
                    {
                        Vector3 originalSize = _sliderCollidersInitialSize[i];
                        Vector3 originalCenter = _sliderCollidersInitialCenter[i];
                        sliderColliders[i].size = new Vector3(originalSize.x * 3f, originalSize.y * 2f, originalSize.z);
                        sliderColliders[i].center = new Vector3(originalCenter.x * 3f, originalCenter.y * 2f, originalCenter.z);
                    }
                }
            }
        }

        /// <summary>
        /// 2DUI操作終了時：TrackAreaとPickupColliderを初期状態に戻します。
        /// </summary>
        public void UI_RestoreTrackAreaAndPickup()
        {
            if (!_trackAreaStatesCached)
            {
                return;
            }

            // PickupColliderを初期状態に戻す
            if (pickupCollider != null)
            {
                pickupCollider.enabled = _pickupColliderInitialState;
            }

            // sliderCanvasは変更していないので復元不要

            // sliderCollidersを初期サイズ・センターに戻す
            if (sliderColliders != null)
            {
                for (int i = 0; i < sliderColliders.Length; i++)
                {
                    if (sliderColliders[i] != null)
                    {
                        sliderColliders[i].size = _sliderCollidersInitialSize[i];
                        sliderColliders[i].center = _sliderCollidersInitialCenter[i];
                    }
                }
            }
        }

        /// <summary>
        /// TrackAreaとPickupColliderの初期状態をキャッシュします。
        /// </summary>
        private void CacheTrackAreaInitialStates()
        {
            if (_trackAreaStatesCached)
            {
                return;
            }

            // VRCUIShape が生成する collider が薄い場合があるため、キャッシュ前に厚み補正を適用する
            EnsureUiColliderZThicknessIfNeeded();

            // PickupColliderの初期状態を保存
            if (pickupCollider != null)
            {
                _pickupColliderInitialState = pickupCollider.enabled;
            }

            // sliderCanvasは変更しないのでキャッシュ不要

            // sliderCollidersの初期サイズ・センターを保存
            if (sliderColliders != null)
            {
                _sliderCollidersInitialSize = new Vector3[sliderColliders.Length];
                _sliderCollidersInitialCenter = new Vector3[sliderColliders.Length];
                for (int i = 0; i < sliderColliders.Length; i++)
                {
                    if (sliderColliders[i] != null)
                    {
                        _sliderCollidersInitialSize[i] = sliderColliders[i].size;
                        _sliderCollidersInitialCenter[i] = sliderColliders[i].center;
                    }
                }
            }

            _trackAreaStatesCached = true;
        }

        /// <summary>
        /// sliderColliders の Z 厚みが薄すぎる場合に、実行時に倍率で厚くします。
        /// VRCUIShape の自動生成が Start 順で遅れることがあるため、初期化直後に遅延でも再実行します。
        /// </summary>
        private void EnsureUiColliderZThicknessIfNeeded()
        {
            if (!applyUiColliderZThicknessMultiplier)
            {
                return;
            }

            // まだ生成されていない/参照が空の場合に備え、適用フラグは「実際に適用できた時」だけ立てる
            if (_uiColliderZThicknessApplied)
            {
                return;
            }

            if (sliderColliders == null || sliderColliders.Length == 0)
            {
                return;
            }

            float mult = Mathf.Max(1f, uiColliderZThicknessMultiplier);
            if (mult <= 1.0001f)
            {
                // 1倍指定なら何もしないが、今後も処理不要
                _uiColliderZThicknessApplied = true;
                return;
            }

            bool applied = false;
            for (int i = 0; i < sliderColliders.Length; i++)
            {
                var col = sliderColliders[i];
                if (col == null)
                {
                    continue;
                }

                Vector3 size = col.size;
                size.z = size.z * mult;
                col.size = size;
                applied = true;
            }

            if (applied)
            {
                _uiColliderZThicknessApplied = true;
            }
        }

        /// <summary>
        /// 遅延実行用：VRCUIShape が collider を生成した後に厚み補正を適用します。
        /// </summary>
        public void UI_EnsureColliderZThicknessDeferred()
        {
            EnsureUiColliderZThicknessIfNeeded();
        }

        #endregion

        #region External Sync API

        /// <summary>
        /// 外部から、値(0-1)と補間時間を指定して反映します。
        /// </summary>
        public void ApplyValueFromExternalWithTime(float value01, bool snap, float interpolationTime)
        {
            // 自分が動かしている最中は外部同期を受け取らない
            if (_isPickedUp)
            {
                return;
            }

            float targetValue = Mathf.Clamp01(value01);

            // スナップが必要な場合は目標値をスナップ
            if (snap)
            {
                targetValue = SnapValue01(targetValue);
            }

            // 補間開始（ローカルの見た目・出力）
            _interpolationApplyOutput = true;
            _interpolationStartValue = sliderValue;
            _interpolationTargetValue = targetValue;
            _interpolationElapsed = 0f;
            _interpolationDuration = Mathf.Max(0.01f, interpolationTime);
            _isInterpolating = true;

            // Global同期：必要なら Synced 値も更新して送る
            if (IsGlobal)
            {
                EnsureGlobalOwnership();
                _syncedSliderValue = targetValue;
                RequestSerialization();
            }

            // 補間中だけTickを回す
            ScheduleTick(0.01f);
        }

        /// <summary>
        /// 外部から、値(0-1)と補間時間を指定して反映します（見た目のみ）。
        /// </summary>
        public void ApplyValueFromExternalWithTimeVisualOnly(float value01, bool snap, float interpolationTime)
        {
            if (_isPickedUp)
            {
                return;
            }

            float targetValue = Mathf.Clamp01(value01);
            if (snap)
            {
                targetValue = SnapValue01(targetValue);
            }

            _interpolationApplyOutput = false;
            _interpolationStartValue = sliderValue;
            _interpolationTargetValue = targetValue;
            _interpolationElapsed = 0f;
            _interpolationDuration = Mathf.Max(0.01f, interpolationTime);
            _isInterpolating = true;

            // 補間中だけTickを回す
            ScheduleTick(0.01f);
        }

        #endregion

        private bool IsGlobal
        {
            get { return syncMode == SliderSwitchSyncMode.Global; }
        }

        private bool IsLocalSave
        {
            get { return syncMode == SliderSwitchSyncMode.LocalSave; }
        }

        #region 外部からの初期化補助

        /// <summary>
        /// ローカルモード(isGlobal=false)用。
        /// SliderSwitch_Pickup 側（つまみオブジェクト）の初期Transformが参照されてしまう構成向けに、
        /// Start時に「sliderDefaultValue の位置」へつまみ（Pickup）を揃えます。
        /// </summary>
        public void InitializePickupToDefaultOnStart(VRC_Pickup pickup)
        {
            if (!IsPickupEnabledVisualMode)
            {
                return;
            }

            // 参照補完
            if (sliderTarget == null)
            {
                sliderTarget = transform;
            }

            // pickupColliderが未設定なら、Pickupに付いているColliderを拾っておく（可能なら）
            if (pickupCollider == null && pickup != null)
            {
                pickupCollider = (Collider)pickup.GetComponent(typeof(Collider));
            }

            // 初期値を反映（sliderTargetの見た目・内部値を確定）
            CacheFixedLocalXZ();
            sliderValue = Mathf.Clamp01(sliderDefaultValue);
            ApplySliderValue(sliderValue, snap: true);

            // つまみ側（SliderSwitch_Pickupのオブジェクト）も、見た目の位置へ合わせる
            if (pickup != null)
            {
                pickup.transform.position = sliderTarget.position;
            }

            if (pickupCollider != null)
            {
                pickupCollider.transform.position = sliderTarget.position;
            }
        }

        #endregion

        #region Unity ライフサイクル

        /// <summary>
        /// 有効化されたタイミングで状態を復元します。
        /// </summary>
        private void OnEnable()
        {
            // Start前の初回OnEnableでは何もしない
            if (!_initialized)
            {
                return;
            }

            // 非アクティブから復帰した際に状態を復元
            RestoreStateOnReenable();
        }

        /// <summary>
        /// 起動時の初期化。
        /// </summary>
        private void Start()
        {
            // ワールド起動時の初期化
            InitializeOnceOrReenable();
            _initialized = true;
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        /// <summary>
        /// Inspector上で sliderDefaultValue を変更した時に、エディタ上の見た目（位置/ゲージ/Canvas描画）へ即反映します。
        /// 再生中はランタイム処理があるため、ここでは何もしません。
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

            Editor_EnsureInteractionTextFieldShowsSyncMode();

            ApplyVisualModeObjectsInEditor();

            // 向き（Horizontal/Vertical）に応じた回転をEditor上でも反映
            ApplyOrientationModeInEditor();

            if (sliderTarget == null)
            {
                sliderTarget = transform;
            }

            float v = Mathf.Clamp01(sliderDefaultValue);
            v = SnapValue01(v);
            sliderValue = v;

            // 位置反映（Editorでは pickupCollider は動かさない）
            CacheFixedLocalXZ();
            float y = Mathf.Lerp(minPosition, maxPosition, sliderValue);
            if (sliderTarget != null)
            {
                sliderTarget.localPosition = new Vector3(_fixedLocalXZ.x, y, _fixedLocalXZ.z);
            }

            // ゲージ反映（PropertyBlockで見た目だけ更新：共有マテリアルを汚さない）
            if (sliderLamp != null)
            {
                var propertyBlock = new MaterialPropertyBlock();
                sliderLamp.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(SliderLampValuePropertyName, sliderValue);
                sliderLamp.SetPropertyBlock(propertyBlock);

                _lastLampValue = sliderValue;
            }

            // 2D(UI)見た目反映（任意）
            UpdateUGUIVisualsFromValue(sliderValue, forceWriteSliderValue: true);

            if (TextAutoUpdate)
            {
                ApplySliderText();
                Editor_ApplyInteractionText();
            }

            // Controller への値反映（エディタ上でも Transform 状態を更新）
            if (controller != null)
            {
                controller.SetProgramVariable("_value", sliderValue);
                controller.SendCustomEvent("OnValueChanged");
            }

            // Canvas描画は廃止
        }

        private void Editor_ApplyInteractionText()
        {
            if (targetPickups == null || targetPickups.Length == 0)
            {
                return;
            }

            var nextText = BuildInteractionTextWithSyncMode();

            int count = 0;
            for (int i = 0; i < targetPickups.Length; i++)
            {
                var pickup = targetPickups[i];
                if (pickup == null)
                {
                    continue;
                }

                if (pickup.InteractionText == nextText)
                {
                    continue;
                }

                count++;
            }

            if (count == 0)
            {
                return;
            }

            var toRecord = new Object[count];
            int writeIndex = 0;
            for (int i = 0; i < targetPickups.Length; i++)
            {
                var pickup = targetPickups[i];
                if (pickup == null)
                {
                    continue;
                }

                if (pickup.InteractionText == nextText)
                {
                    continue;
                }

                toRecord[writeIndex] = pickup;
                writeIndex++;
            }

            Undo.RecordObjects(toRecord, "Update Pickup InteractionText");
            for (int i = 0; i < toRecord.Length; i++)
            {
                var pickup = (VRCPickup)toRecord[i];
                pickup.InteractionText = nextText;
                EditorUtility.SetDirty(pickup);
                PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
            }
        }

        private void Editor_EnsureInteractionTextFieldShowsSyncMode()
        {
            var desired = BuildInteractionTextWithSyncMode();
            if (slider_InteractionText == desired)
            {
                return;
            }

            Undo.RecordObject(this, "Update Slider InteractionText");
            slider_InteractionText = desired;
            EditorUtility.SetDirty(this);
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif

        /// <summary>
        /// Updateの代替処理。Pickup中/補間中/UIドラッグ中のみ遅延イベントで実行されます。
        /// </summary>
        public void TickLoop()
        {
            _tickScheduled = false;

            float now = Time.time;
            float dt = Mathf.Max(0f, now - _lastTickTime);
            _lastTickTime = now;

            if (_isPickedUp)
            {
                UpdatePickupMode(dt);
            }
            else if (_isUiDragging)
            {
                UpdateUiDragging(dt);
            }
            else if (_isInterpolating)
            {
                UpdateInterpolation(dt);
            }

            if (_isPickedUp || _isUiDragging || _isInterpolating)
            {
                // 可能な限り滑らかに：Updateは使わず、毎フレームTick
                ScheduleTick(0f);
            }
        }

        /// <summary>
        /// 次のTickLoopをスケジュールします。
        /// </summary>
        /// <param name="delaySeconds">遅延時間（秒）</param>
        private void ScheduleTick(float delaySeconds)
        {
            if (_tickScheduled)
            {
                return;
            }

            _tickScheduled = true;
            _lastTickTime = Time.time;
            SendCustomEventDelayedFrames("TickLoop", 1);
        }

        private bool Is2DUIVisualMode
        {
            get { return sliderVisualMode == SliderSwitchVisualMode.Mode2DUI; }
        }

        private bool IsPickupEnabledVisualMode
        {
            get { return sliderVisualMode != SliderSwitchVisualMode.Mode2DUI; }
        }

        /// <summary>
        /// ビジュアルモードに応じて2D/3Dオブジェクトの表示を切り替えます（ランタイム用）。
        /// </summary>
        private void ApplyVisualModeObjectsRuntime()
        {
            bool show3D = sliderVisualMode == SliderSwitchVisualMode.Mode3D;
            // 3Dモードは3D専用UIを用意する想定のため、2D側オブジェクトは非表示でOK
            bool show2D = sliderVisualMode != SliderSwitchVisualMode.Mode3D;

            if (_3DModeObject != null)
            {
                _3DModeObject.SetActive(show3D);
            }

            if (_2DModeObject != null)
            {
                _2DModeObject.SetActive(show2D);
            }

            // 2D_UI だけは Pickup 無し
            if (_3DPickup != null)
            {
                _3DPickup.SetActive(IsPickupEnabledVisualMode);
            }

            // pickupCollider も 2D_UI では無効化（3DPickup未設定でも誤操作しないように）
            if (pickupCollider != null)
            {
                pickupCollider.enabled = IsPickupEnabledVisualMode;
            }
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        /// <summary>
        /// ビジュアルモードに応じて2D/3Dオブジェクトの表示を切り替えます（エディタ用）。
        /// </summary>
        private void ApplyVisualModeObjectsInEditor()
        {
            bool show3D = sliderVisualMode == SliderSwitchVisualMode.Mode3D;
            bool show2D = sliderVisualMode != SliderSwitchVisualMode.Mode3D;

            if (_3DModeObject != null)
            {
                _3DModeObject.SetActive(show3D);
            }

            if (_2DModeObject != null)
            {
                _2DModeObject.SetActive(show2D);
            }

            if (_3DPickup != null)
            {
                _3DPickup.SetActive(IsPickupEnabledVisualMode);
            }

            if (pickupCollider != null)
            {
                pickupCollider.enabled = IsPickupEnabledVisualMode;
            }
        }
#endif

        #endregion

        #region Pickup イベント

        /// <summary>
        /// Pickup側（別コンポーネント）から呼び出すための「掴み開始」通知。
        /// SliderSwitch自身にVRC_Pickupが無い構成でも動くよう、pickup参照を受け取れます。
        /// </summary>
        public void NotifyPickup(VRC_Pickup pickup)
        {
            if (!IsPickupEnabledVisualMode)
            {
                return;
            }

            if (pickup != null)
            {
                _pickup = pickup;
            }

            // 掴んでいる間だけUpdateで追従させるため、フラグを立てる
            _isPickedUp = true;

            // Global同期モード：オーナー権を取得
            if (IsGlobal)
            {
                EnsureGlobalOwnership();
            }

            // 補間を停止
            _isInterpolating = false;

            // 直前までの慣性（SmoothDampの速度）をリセットして、掴んだ瞬間のジャンプ感を減らす
            _valueVelocity = 0f;

            // 掴んだ瞬間の効果音
            PlayTouchSound();

            // Pickup中だけTickを回す
            ScheduleTick(0.01f);
        }

        /// <summary>
        /// Pickup側（別コンポーネント）から呼び出すための「掴み終了」通知。
        /// このタイミングで最終値をスナップ（段に丸め）して固定します。
        /// </summary>
        public void NotifyDrop(VRC_Pickup pickup)
        {
            if (!IsPickupEnabledVisualMode)
            {
                return;
            }

            if (pickup != null)
            {
                _pickup = pickup;
            }

            // 掴み終了
            _isPickedUp = false;

            // ドロップ時にスナップで段が変わったかどうかを判定するため、直前の段番号を退避
            int prevSnapIndex = _lastSnapIndex;

            // 最終値をスナップして固定（段に丸めてピタッと止める）
            ApplySliderValue(sliderValue, snap: true);

            // スナップ後の段番号
            int afterSnapIndex = GetSnapIndex(SnapValue01(sliderValue));
            if (afterSnapIndex != prevSnapIndex)
            {
                // もし段が変わっていたら、カチカチ音を鳴らす
                _lastSnapIndex = afterSnapIndex;
                PlayOneShotSafe(slideAudioClip_clip);
            }

            // 最終決定（ドロップ）時のハプティクスは、段が変わらなくても必ず鳴らす
            PlayConfirmHapticsOnCurrentHand();

            // 離した瞬間の固定音
            PlayOneShotSafe(slideAudioClip_fix);

            // Global同期モード：確定した値を送信
            if (IsGlobal)
            {
                _syncedSliderValue = sliderValue;
                RequestSerialization();
            }

            // SliderTextは廃止

            // LocalSave: 確定値を保存
            SaveLocalIfNeeded(sliderValue);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!IsLocalSave)
            {
                return;
            }

            if (player == null || !player.isLocal)
            {
                return;
            }

            if (string.IsNullOrEmpty(persistanceKey))
            {
                return;
            }

            if (PlayerData.TryGetInt(player, persistanceKey, out int savedInt))
            {
                float restored = Mathf.Clamp01(savedInt / (float)LocalSaveScale);
                _localSaveRestored = true;
                _localSaveRestoredValue = restored;

                // 掴み中は復元しない（不自然なジャンプを防ぐ）
                if (_isPickedUp)
                {
                    return;
                }

                sliderValue = restored;
                ApplySliderValue(sliderValue, snap: true);
                _lastSnapIndex = GetSnapIndex(SnapValue01(sliderValue));
                _valueVelocity = 0f;
                // SliderTextは廃止
            }
        }

        public override void OnDeserialization()
        {
            if (!IsGlobal)
            {
                return;
            }

            // フォロワーUIとして外部制御されている場合は、ネットワーク値を適用しない
            // （SwitchSyncer が別のマスター値へ合わせるため、ここで戻されるとちらつく）
            if (_ignoreDeserializationByExternalControl)
            {
                return;
            }

            // LateJoiner（初回Join）の場合は補間なしで即座に値を適用
            if (_isFirstJoin)
            {
                _isFirstJoin = false;
                sliderValue = _syncedSliderValue;
                ApplySliderValue(sliderValue, snap: true);
                return;
            }

            // 自分が動かしている最中は同期を受け取らない
            if (_isPickedUp)
            {
                return;
            }

            // 補間開始
            _interpolationApplyOutput = true;
            _interpolationStartValue = sliderValue;
            _interpolationTargetValue = _syncedSliderValue;
            _interpolationElapsed = 0f;
            _interpolationDuration = Mathf.Max(0.01f, GlobalSyncInterpolationTimeSeconds);
            _isInterpolating = true;

            // 補間中だけTickを回す
            ScheduleTick(0.01f);
        }

        #endregion

        #region 初期化

        /// <summary>
        /// Start/OnEnable から呼ばれる共通初期化。
        /// インスペクタ未設定でも破綻しないよう、参照の補完や初期位置の反映を行います。
        /// </summary>
        private void InitializeOnceOrReenable()
        {
            // 2D/3D表示切替（有効化されるたびに整える）
            ApplyVisualModeObjectsRuntime();

            // UI当たり判定が薄すぎる問題の救済（即時＋遅延で適用）
            EnsureUiColliderZThicknessIfNeeded();
            SendCustomEventDelayedFrames("UI_EnsureColliderZThicknessDeferred", 1);
            SendCustomEventDelayedFrames("UI_EnsureColliderZThicknessDeferred", 10);

            // 向き（Horizontal/Vertical）に応じた回転を反映
            ApplyOrientationModeRuntime();

            // VRC_Pickup（掴んでいる手など）参照をキャッシュ
            CachePickup();

            // sliderTarget未指定なら自分自身を操作対象にする
            if (sliderTarget == null)
            {
                sliderTarget = transform;
            }

            // pickupColliderの有効/無効は VisualMode で制御する

            // ローカルX/Zを固定するためのキャッシュ
            CacheFixedLocalXZ();

            // SliderLamp（ゲージ）用マテリアルの準備（共有マテリアルを壊さないためインスタンス化）
            EnsureSliderLampMaterialInstance();

            // 2D(UI)参照の初期キャッシュ
            CacheUGUIHandleBaseAnchoredPosIfNeeded();

            if (IsGlobal)
            {
                // Global同期：セッション初回だけ sliderDefaultValue を適用
                TryInitializeGlobalDefaultIfNeeded();
            }
            else
            {
                // ローカル（Local/LocalSave）モードは初期値を反映（位置・表示・段番号を同期）
                // LocalSaveの実際の復元は OnPlayerRestored で行います。
                InitializeSliderPositionFromDefault();
            }

            // 2D(UI)側の見た目も初期同期
            UpdateUGUIVisualsFromValue(sliderValue, forceWriteSliderValue: true);

            if (TextAutoUpdate)
            {
                ApplySliderText();
                ApplyInteractionText();
            }
        }

        /// <summary>
        /// 向きモード（Horizontal/Vertical）に応じた回転を適用します（ランタイム用）。
        /// </summary>
        private void ApplyOrientationModeRuntime()
        {
            bool isHorizontal = orientationMode == SliderSwitchOrientationMode.Horizontal;

            // RotateObjects_90: Vertical=0 / Horizontal=+90
            ApplyOrientationZToTransforms(rotateObjectsPlus90, isHorizontal ? 90f : 0f);

            // RotateObjects_-90: Vertical=0 / Horizontal=-90
            ApplyOrientationZToTransforms(rotateObjectsMinus90, isHorizontal ? -90f : 0f);

            // rotateTextObjects: ローカル回転の絶対値として設定（Horizontal:+90 / Vertical:-90）
            ApplyAbsoluteLocalZRotationToTransforms(rotateTextObjects, isHorizontal ? 90f : -90f);
        }

        /// <summary>
        /// 指定Transform配列に、ローカル回転を絶対値として適用します（X/Yは0固定）。
        /// Inspector上の期待値（例: Z=+90 / Z=-90）をそのまま入力したい用途向け。
        /// </summary>
        private void ApplyAbsoluteLocalZRotationToTransforms(Transform[] targets, float targetZ)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                Transform t = targets[i];
                if (t == null)
                {
                    continue;
                }

                t.localRotation = Quaternion.Euler(0f, 0f, targetZ);
            }
        }

        /// <summary>
        /// 指定されたTransform配列にZ軸回転を適用します。
        /// </summary>
        private void ApplyOrientationZToTransforms(Transform[] targets, float targetZ)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                Transform t = targets[i];
                if (t == null)
                {
                    continue;
                }

                Vector3 e = t.localEulerAngles;
                e.z = targetZ;
                t.localEulerAngles = e;
            }
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        /// <summary>
        /// 向きモード（Horizontal/Vertical）に応じた回転を適用します（エディタ用）。
        /// </summary>
        private void ApplyOrientationModeInEditor()
        {
            ApplyOrientationModeRuntime();
        }
#endif

        /// <summary>
        /// LocalSaveモード時にスライダー値を保存します。
        /// </summary>
        private void SaveLocalIfNeeded(float value01)
        {
            if (!IsLocalSave)
            {
                return;
            }

            if (string.IsNullOrEmpty(persistanceKey))
            {
                return;
            }

            int v = Mathf.RoundToInt(Mathf.Clamp01(value01) * LocalSaveScale);
            PlayerData.SetInt(persistanceKey, v);
        }

        /// <summary>
        /// このオブジェクトの VRC_Pickup をキャッシュします。
        /// 掴んでいる手（Left/Right）判定に使います。
        /// </summary>
        private void CachePickup()
        {
            // VRC_Pickup が無い場合もある（その場合はハプティクスで手が特定できない）
            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        }

        /// <summary>
        /// スライダーターゲットのローカルX/Z座標を固定します（初回のみ実行）
        /// </summary>
        private void CacheFixedLocalXZ()
        {
            if (_fixedLocalXZCached)
            {
                return;
            }

            if (sliderTarget == null)
            {
                return;
            }

            // スライダーは「Yだけ動く」想定なので、X/Zを保存しておき毎回その値を使う
            _fixedLocalXZ = sliderTarget.localPosition;
            _fixedLocalXZ.y = 0f;
            _fixedLocalXZCached = true;
        }

        /// <summary>
        /// デフォルト値を適用し、スライダー位置と表示を初期化します。
        /// </summary>
        private void InitializeSliderPositionFromDefault()
        {
            // 初期値を0..1に丸め
            sliderValue = Mathf.Clamp01(sliderDefaultValue);

            // 初期値も段にスナップさせて、起動直後から段と表示が一致するようにする
            ApplySliderValue(sliderValue, snap: true);

            // クリック判定用に、現在の段番号を初期化
            _lastSnapIndex = GetSnapIndex(SnapValue01(sliderValue));

            // SmoothDampの速度も初期化
            _valueVelocity = 0f;

            // SliderTextは廃止
        }

        /// <summary>
        /// 非アクティブから復帰した際に現在の状態を復元します。
        /// </summary>
        private void RestoreStateOnReenable()
        {
            // 2D/3D表示切替（有効化されるたびに整える）
            ApplyVisualModeObjectsRuntime();

            // UI当たり判定が薄すぎる問題の救済（即時＋遅延で適用）
            EnsureUiColliderZThicknessIfNeeded();
            SendCustomEventDelayedFrames("UI_EnsureColliderZThicknessDeferred", 1);

            // 向き（Horizontal/Vertical）に応じた回転を反映
            ApplyOrientationModeRuntime();

            // 現在の値（sliderValue）を保持したまま、ビジュアルのみ再適用
            float currentValue = Mathf.Clamp01(sliderValue);
            ApplySliderValue(currentValue, snap: false);

            // 2D(UI)側の見た目も同期
            UpdateUGUIVisualsFromValue(currentValue, forceWriteSliderValue: true);

            if (TextAutoUpdate)
            {
                ApplySliderText();
                ApplyInteractionText();
            }
        }



        #endregion

        #region スライダー計算・反映

        /// <summary>
        /// 値をスライダー位置へ反映します。
        /// </summary>
        /// <param name="value01">スライダー値（0.0～1.0）</param>
        /// <param name="snap">スナップするか</param>
        private void ApplySliderValue(float value01, bool snap)
        {
            ApplySliderValueInternal(value01, snap, applyOutput: true);
        }

        /// <summary>
        /// 値をスライダー位置へ反映します（内部処理）。
        /// </summary>
        private void ApplySliderValueInternal(float value01, bool snap, bool applyOutput)
        {
            if (sliderTarget == null)
            {
                return;
            }

            // CacheFixedLocalXZ()は初期化時のみ実行し、ここでは呼ばない
            // （非アクティブ復帰時にsliderTargetの位置が不正な状態で呼ばれる可能性があるため）

            float v = Mathf.Clamp01(value01);
            if (snap)
            {
                v = SnapValue01(v);
            }

            sliderValue = v;

            // ゲージ（SliderLamp）へ反映（出力モードとは独立）
            ApplyLampValueFromSliderValue(sliderValue);

            if (applyOutput)
            {
                ApplyOutputFromValue(sliderValue);
            }

            UpdateSliderTransformsFromValue(syncPickupColliderPosition: ShouldWritePickupColliderPosition());

            // 2D(UI)見た目も同期
            UpdateUGUIVisualsFromValue(sliderValue, forceWriteSliderValue: false);
        }

        /// <summary>
        /// ピックアップ中の追従処理を更新します。
        /// </summary>
        private void UpdatePickupMode(float dt)
        {
            if (pickupCollider == null)
            {
                return;
            }

            // pickupColliderの位置から「いまどれくらい引っ張られているか（0-1）」を計算
            float rawValue = ComputeValue01FromPickupCollider();

            // スナップ位置の近くで少しだけ引っ掛かるように、入力値に抵抗をかける
            rawValue = ApplySoftDetent(rawValue);

            // 追従の滑らかさ（0に近いと不安定になりやすいので下限を設ける）
            float smoothTime = Mathf.Max(0.0001f, followSmoothTime);

            // 追従速度の上限（0以下なら無制限扱い）
            float maxSpeed;
            if (followMaxSpeed > 0f)
            {
                maxSpeed = followMaxSpeed;
            }
            else
            {
                maxSpeed = Mathf.Infinity;
            }

            // 手の動きに対して、スライダー値を滑らかに追従させる
            sliderValue = Mathf.SmoothDamp(sliderValue, rawValue, ref _valueVelocity, smoothTime, maxSpeed, dt);
            sliderValue = Mathf.Clamp01(sliderValue);

            // ゲージ（SliderLamp）へ反映
            ApplyLampValueFromSliderValue(sliderValue);

            // 値をターゲットへ反映
            ApplyOutputFromValue(sliderValue);

            // 現在値がどの段にいるかを計算し、段が変わった瞬間だけ反応する
            int snapIndex = GetSnapIndex(SnapValue01(sliderValue));
            if (snapIndex != _lastSnapIndex)
            {
                // 段が変わった：クリック音＋ハプティクス
                _lastSnapIndex = snapIndex;
                PlayOneShotSafe(slideAudioClip_clip);
                PlaySnapHapticsOnCurrentHand();
            }

            // 値→位置に反映（つまみの見た目も追従）
            UpdateSliderTransformsFromValue(syncPickupColliderPosition: ShouldWritePickupColliderPosition());

            // 2D(UI)見た目も追従
            UpdateUGUIVisualsFromValue(sliderValue, forceWriteSliderValue: false);
        }

        /// <summary>
        /// 補間処理を更新します（Global同期や外部同期用）。
        /// </summary>
        /// <param name="dt">デルタタイム</param>
        private void UpdateInterpolation(float dt)
        {
            if (!_isInterpolating)
            {
                return;
            }

            _interpolationElapsed += dt;

            float t = Mathf.Clamp01(_interpolationElapsed / _interpolationDuration);

            // 補間カーブ（easeInOutを使用）
            t = t * t * (3f - 2f * t);

            sliderValue = Mathf.Lerp(_interpolationStartValue, _interpolationTargetValue, t);
            sliderValue = Mathf.Clamp01(sliderValue);

            // ゲージ（SliderLamp）へ反映
            ApplyLampValueFromSliderValue(sliderValue);

            // 値を反映
            if (_interpolationApplyOutput)
            {
                ApplyOutputFromValue(sliderValue);
            }
            UpdateSliderTransformsFromValue(syncPickupColliderPosition: true);

            // 2D(UI)見た目も追従
            UpdateUGUIVisualsFromValue(sliderValue, forceWriteSliderValue: false);

            // 補間完了
            if (t >= 1f)
            {
                _isInterpolating = false;
                // 最終的にスナップ
                ApplySliderValueInternal(sliderValue, snap: true, applyOutput: _interpolationApplyOutput);

                // 段番号を更新（次回Pickup時のクリック判定がズレないように）
                _lastSnapIndex = GetSnapIndex(SnapValue01(sliderValue));
            }
        }

        #region 2D(UI) Input

        /// <summary>
        /// UIスライダーのポインターダウンイベント処理。
        /// </summary>
        public void UI_OnPointerDown()
        {
            // 3Dで掴んでいる間はUI操作を無視
            if (_isPickedUp)
            {
                return;
            }

            // 2D(UI)でPointerDownが多重発火する構成があり得るため、既にドラッグ中なら二重開始・二重サウンドを防ぐ。
            if (_isUiDragging)
            {
                return;
            }

            _isUiDragging = true;
            _uiValueVelocity = 0f;

            // 入力がまだ来ていない場合の保険（PointerDown直後の1フレームでジャンプしないように）
            _uiTargetValue = sliderValue;

            if (IsGlobal)
            {
                EnsureGlobalOwnership();
            }

            PlayTouchSound();

            // ドラッグ中だけTickを回す
            ScheduleTick(0.01f);
        }

        /// <summary>
        /// UIスライダーのドラッグイベント処理。
        /// </summary>
        public void UI_OnDrag()
        {
            if (!_isUiDragging)
            {
                return;
            }
        }

        /// <summary>
        /// UIスライダーのポインターアップイベント処理。最終値をスナップして確定します。
        /// </summary>
        public void UI_OnPointerUp()
        {
            if (!_isUiDragging)
            {
                return;
            }

            _isUiDragging = false;

            // 最終値をスナップして固定
            ApplySliderValue(sliderValue, snap: true);

            // 段が変わっていたらクリック音
            int snapIndex = GetSnapIndex(SnapValue01(sliderValue));
            if (snapIndex != _lastSnapIndex)
            {
                _lastSnapIndex = snapIndex;
                PlayOneShotSafe(slideAudioClip_clip);
            }

            // 最終決定ハプティクス
            PlayConfirmHapticsOnCurrentHand();
            PlayOneShotSafe(slideAudioClip_fix);

            // Global同期モード：確定した値を送信
            if (IsGlobal)
            {
                _syncedSliderValue = sliderValue;
                RequestSerialization();
            }

            // LocalSave: 確定値を保存
            SaveLocalIfNeeded(sliderValue);

            // UIのつまみ/Fill/Slider値も最終位置へ
            UpdateUGUIVisualsFromValue(sliderValue, forceWriteSliderValue: true);
        }

        /// <summary>
        /// Unity UI SliderのOnValueChangedイベントから呼び出されます
        /// </summary>
        public void UI_OnSliderValueChanged(float value01)
        {
            if (_uiSuppressSliderCallback)
            {
                return;
            }

            _uiTargetValue = Mathf.Clamp01(value01);
        }

        /// <summary>
        /// UIドラッグ中の更新処理。ソフトディテントと慣性を適用します。
        /// </summary>
        private void UpdateUiDragging(float dt)
        {
            // 目標値に対して吸着を適用（UIは強さをInspectorで調整）
            float detentTarget = ApplySoftDetentWithStrength(_uiTargetValue, uiSoftDetentStrength);

            // 慣性（SmoothDamp）
            float smoothTime = Mathf.Max(0.0001f, uiSmoothTime);
            float v = Mathf.SmoothDamp(sliderValue, detentTarget, ref _uiValueVelocity, smoothTime, Mathf.Infinity, dt);
            v = Mathf.Clamp01(v);

            // ドラッグ中はネットワーク送信せず、ローカル反映だけ（出力は更新する）
            ApplySliderValueInternal(v, snap: false, applyOutput: true);

            // 段が変わった瞬間だけフィードバック（クリック音＋ハプティクス）
            int snapIndex = GetSnapIndex(SnapValue01(sliderValue));
            if (snapIndex != _lastSnapIndex)
            {
                _lastSnapIndex = snapIndex;
                PlayOneShotSafe(slideAudioClip_clip);
                PlaySnapHapticsOnCurrentHand();
            }
        }

        /// <summary>
        /// 指定された強度でソフトディテント（段への吸着）を適用します。
        /// </summary>
        private float ApplySoftDetentWithStrength(float value01, float strength01)
        {
            float step = Mathf.Abs(sliderStep);
            if (step < MinStep)
            {
                return Mathf.Clamp01(value01);
            }

            float range = step * SoftDetentRangeRatio;
            if (range <= 0f)
            {
                return Mathf.Clamp01(value01);
            }

            float v = Mathf.Clamp01(value01);
            float snapped = SnapValue01(v);
            float distance = Mathf.Abs(v - snapped);
            if (distance >= range)
            {
                return v;
            }

            float t = 1f - (distance / range);
            t = Mathf.Clamp01(t);
            float smooth = t * t * (3f - (2f * t));
            float weight = smooth * Mathf.Clamp01(strength01);
            return Mathf.Lerp(v, snapped, weight);
        }

        /// <summary>
        /// UIハンドルの基準位置をキャッシュします（初回のみ実行）。
        /// </summary>
        private void CacheUGUIHandleBaseAnchoredPosIfNeeded()
        {
            if (_uiHandleBasePosCached)
            {
                return;
            }

            if (uiHandle == null)
            {
                return;
            }

            _uiHandleBaseAnchoredPos = uiHandle.anchoredPosition;
            _uiHandleBasePosCached = true;
        }

        /// <summary>
        /// スライダー値からUIの見た目（ハンドル位置、Fill量、Slider値）を更新します。
        /// </summary>
        private void UpdateUGUIVisualsFromValue(float value01, bool forceWriteSliderValue)
        {
            float v = Mathf.Clamp01(value01);

            CacheUGUIHandleBaseAnchoredPosIfNeeded();

            // Slider値の同期（非ドラッグ中のみ）
            if (uiSlider != null && (!_isUiDragging || forceWriteSliderValue))
            {
                if (forceWriteSliderValue || Mathf.Abs(uiSlider.value - v) > 0.0005f)
                {
                    _uiSuppressSliderCallback = true;
                    uiSlider.value = v;
                    _uiSuppressSliderCallback = false;
                }
            }

            // Handle
            if (uiHandle != null)
            {
                Vector2 pos = uiHandle.anchoredPosition;
                pos.x = _uiHandleBaseAnchoredPos.x;
                pos.y = Mathf.Lerp(uiHandleMinY, uiHandleMaxY, v);
                uiHandle.anchoredPosition = pos;
            }

            // Fill
            if (uiFillImage != null)
            {
                uiFillImage.fillAmount = v;
            }
        }

        #endregion

        /// <summary>
        /// Global同期時に、ローカルプレイヤーが確実にUdonSyncedを送信できるよう所有権を取得します。
        /// </summary>
        private void EnsureGlobalOwnership()
        {
            if (!IsGlobal)
            {
                return;
            }

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null)
            {
                return;
            }

            // UdonSynced を送れるように、このUdonBehaviourが付いているオブジェクトのオーナーになる
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(localPlayer, gameObject);
            }
        }

        /// <summary>
        /// Global同期時の初回初期化処理。デフォルト値を反映します。
        /// </summary>
        private void TryInitializeGlobalDefaultIfNeeded()
        {
            if (!IsGlobal)
            {
                return;
            }

            if (_globalInitialized)
            {
                // 既に初期化済み：同期された値を適用
                sliderValue = _syncedSliderValue;
                ApplySliderValue(sliderValue, snap: true);
                return;
            }

            // 初回はマスター（または既にオーナー）だけが初期値を確定させる
            bool canInit = Networking.IsMaster || Networking.IsOwner(gameObject);
            if (canInit)
            {
                EnsureGlobalOwnership();

                // デフォルト値を反映
                sliderValue = sliderDefaultValue;
                ApplySliderValue(sliderValue, snap: true);
                _syncedSliderValue = sliderValue;

                _globalInitialized = true;
                RequestSerialization();
                return;
            }

            // 自分が初期化担当でない場合は、同期された値を適用
            sliderValue = _syncedSliderValue;
            ApplySliderValue(sliderValue, snap: true);
        }

        /// <summary>
        /// ピックアップコライダーの位置から0～1の値を計算します。
        /// </summary>
        /// <returns>計算された値（0.0～1.0）</returns>
        private float ComputeValue01FromPickupCollider()
        {
            // sliderTarget の親空間で pickupCollider のYを読みたいので、親Transformを取得
            Transform parent = null;
            if (sliderTarget != null)
            {
                parent = sliderTarget.parent;
            }

            // pickupCollider の座標を「sliderTargetの親空間」に変換して取得
            Vector3 colliderLocalPos;
            if (parent != null)
            {
                colliderLocalPos = parent.InverseTransformPoint(pickupCollider.transform.position);
            }
            else
            {
                // 親が無い場合はローカル座標をそのまま使う
                colliderLocalPos = pickupCollider.transform.localPosition;
            }

            // Yを移動範囲にクランプして、0-1へ正規化
            float clampedY = Mathf.Clamp(colliderLocalPos.y, minPosition, maxPosition);
            float rawValue = Mathf.InverseLerp(minPosition, maxPosition, clampedY);
            return Mathf.Clamp01(rawValue);
        }

        /// <summary>
        /// 現在のsliderValueをスライダー位置へ反映します。
        /// </summary>
        /// <param name="syncPickupColliderPosition">ピックアップコライダーの位置も同期するか</param>
        private void UpdateSliderTransformsFromValue(bool syncPickupColliderPosition)
        {
            if (sliderTarget == null)
            {
                return;
            }

            // XZ座標のキャッシュが未完了の場合は、現在の位置からXZ座標を取得して保持
            // （初期化順序によってはキャッシュ前にこのメソッドが呼ばれる可能性があるため）
            Vector3 fixedXZ;
            if (_fixedLocalXZCached)
            {
                fixedXZ = _fixedLocalXZ;
            }
            else
            {
                fixedXZ = sliderTarget.localPosition;
                fixedXZ.y = 0f;
            }

            // 値(0-1)を位置(min-max)へ変換して、ローカルYに反映
            float y = Mathf.Lerp(minPosition, maxPosition, sliderValue);
            sliderTarget.localPosition = new Vector3(fixedXZ.x, y, fixedXZ.z);

            // pickupCollider も同じ位置に配置（掴み直し時のズレ防止）
            if (pickupCollider != null && syncPickupColliderPosition)
            {
                pickupCollider.transform.position = sliderTarget.position;
            }
        }

        /// <summary>
        /// ピックアップコライダーのTransformを書き換えてよいかどうかを判定します。
        /// </summary>
        /// <returns>書き換え可能な場合true</returns>
        private bool ShouldWritePickupColliderPosition()
        {
            if (pickupCollider == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 値をスライダーステップ間隔で丸めます。
        /// </summary>
        /// <param name="value01">入力値（0.0～1.0）</param>
        /// <returns>スナップされた値</returns>
        private float SnapValue01(float value01)
        {
            // stepが小さすぎる場合はスナップしない
            float step = Mathf.Abs(sliderStep);
            if (step < MinStep)
            {
                return Mathf.Clamp01(value01);
            }

            // value を step間隔で丸める
            float snapped = Mathf.Round(value01 / step) * step;
            return Mathf.Clamp01(snapped);
        }

        /// <summary>
        /// スナップ位置付近で値に抵抗をかけ、引っ掛かる感触を与えます。
        /// </summary>
        /// <param name="value01">入力値（0.0～1.0）</param>
        /// <returns>ソフトディテントが適用された値</returns>
        private float ApplySoftDetent(float value01)
        {
            // stepが無効なら抵抗も無効（基準となるスナップ点が無い）
            float step = Mathf.Abs(sliderStep);
            if (step < MinStep)
            {
                return Mathf.Clamp01(value01);
            }

            // 抵抗が出る範囲は「刻み幅に対する比率」で決める
            float range = step * SoftDetentRangeRatio;
            if (range <= 0f)
            {
                return Mathf.Clamp01(value01);
            }

            // 一番近いスナップ点
            float v = Mathf.Clamp01(value01);
            float snapped = SnapValue01(v);

            // スナップ点からの距離
            float distance = Mathf.Abs(v - snapped);
            if (distance >= range)
            {
                return v;
            }

            // 近いほど強く寄る（滑らかに変化させる）
            float t = 1f - (distance / range);
            t = Mathf.Clamp01(t);

            // SmoothStep: 0-1 を滑らかに
            float smooth = t * t * (3f - (2f * t));

            float weight = smooth * SoftDetentStrength;

            return Mathf.Lerp(v, snapped, weight);
        }

        /// <summary>
        /// スナップされた値から段番号を計算します。
        /// </summary>
        private int GetSnapIndex(float snappedValue01)
        {
            float step = Mathf.Abs(sliderStep);
            if (step < MinStep)
            {
                return 0;
            }

            return Mathf.RoundToInt(snappedValue01 / step);
        }

        #endregion

        #region SliderLamp（ゲージ）

        /// <summary>
        /// ゲージマテリアルを実行時にインスタンス化してキャッシュします。
        /// </summary>
        private void EnsureSliderLampMaterialInstance()
        {
            if (_sliderLampMaterialReady)
            {
                return;
            }

            if (sliderLamp == null)
            {
                return;
            }

            _sliderLampInstancedMaterials = sliderLamp.materials;
            _sliderLampMaterialReady = _sliderLampInstancedMaterials != null && _sliderLampInstancedMaterials.Length > 0;
        }

        /// <summary>
        /// スライダー値をゲージマテリアルに反映します。
        /// </summary>
        private void ApplyLampValueFromSliderValue(float value01)
        {
            if (sliderLamp == null)
            {
                return;
            }

            EnsureSliderLampMaterialInstance();
            if (!_sliderLampMaterialReady || _sliderLampInstancedMaterials == null || _sliderLampInstancedMaterials.Length == 0)
            {
                return;
            }

            float v = Mathf.Clamp01(value01);

            if (Mathf.Abs(v - _lastLampValue) < 0.0005f)
            {
                return;
            }

            _lastLampValue = v;

            for (int i = 0; i < _sliderLampInstancedMaterials.Length; i++)
            {
                var mat = _sliderLampInstancedMaterials[i];
                if (mat != null)
                {
                    mat.SetFloat(SliderLampValuePropertyName, v);
                }
            }
        }

        #endregion

        #region 値反映（Output）

        /// <summary>
        /// スライダー値を外部コントローラーに反映します。
        /// </summary>
        private void ApplyOutputFromValue(float value01)
        {
            float v = Mathf.Clamp01(value01);

            // 統一Controllerに通知
            if (controller != null)
            {
                // _value に書き込み
                controller.SetProgramVariable("_value", v);

                // OnValueChanged() を呼び出し
                controller.SendCustomEvent("OnValueChanged");
            }
        }

        #endregion

        #region サウンド

        /// <summary>
        /// AudioSource と Clip が揃っている場合のみ、OneShot を再生します。
        /// </summary>
        private void PlayOneShotSafe(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (slideAudioSource == null)
            {
                return;
            }

            slideAudioSource.PlayOneShot(clip);
        }

        private void PlayTouchSound()
        {
            PlayOneShotSafe(slideAudioClip_touch);
        }

        #endregion

        #region ハプティクス

        /// <summary>
        /// スナップ時のハプティクスフィードバックを再生します（VRのみ）。
        /// </summary>
        private void PlaySnapHapticsOnCurrentHand()
        {
            PlayHapticsOnCurrentHandInternal(0.5f);
        }

        /// <summary>
        /// 確定時のハプティクスフィードバックを再生します（VRのみ）。
        /// </summary>
        private void PlayConfirmHapticsOnCurrentHand()
        {
            PlayHapticsOnCurrentHandInternal(1f);
        }

        /// <summary>
        /// ハプティクスフィードバックを内部的に再生します。
        /// </summary>
        /// <param name="amplitudeMultiplier">振幅の倍率</param>
        private void PlayHapticsOnCurrentHandInternal(float amplitudeMultiplier)
        {
            if (!enableHapticsOnSnap)
            {
                return;
            }

            // ローカルプレイヤー取得（DesktopだとnullだったりVR判定で弾かれる）
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null)
            {
                return;
            }

            // VRユーザーのみハプティクスを鳴らす（Desktopでは不要）
            if (!localPlayer.IsUserInVR())
            {
                return;
            }

            // 値を安全な範囲に丸めてから送る
            float duration = Mathf.Max(0f, hapticsDurationSeconds);
            float amplitude = Mathf.Clamp01(hapticsAmplitude) * Mathf.Clamp01(amplitudeMultiplier) * HapticsAmplitudeScale;
            float frequency = Mathf.Max(0f, hapticsFrequency);

            // VRC_Pickupが取れない場合は、どちらの手か分からないため両手に送る
            if (_pickup == null)
            {
                localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, duration, amplitude, frequency);
                localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, duration, amplitude, frequency);
                return;
            }

            VRC_Pickup.PickupHand currentHand;

#if UNITY_EDITOR
            currentHand = VRC_Pickup.PickupHand.Right;
#else
            currentHand = _pickup.currentHand;
#endif

            localPlayer.PlayHapticEventInHand(currentHand, duration, amplitude, frequency);
        }

        #endregion
    }
}