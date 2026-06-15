
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDKBase.Editor.Attributes;
using VRC.SDK3.Persistence;
using VRC.Udon;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;





#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
#endif

namespace QuickBrown.LuraSwitch
{
    public enum SwitchMode
    {
        Toggle = 0,
        External = 1,
    }

    public enum SwitchSyncMode
    {
        Local = 0,
        Global = 1,
        LocalSave = 2,
    }

    public enum ToggleDefaultState
    {
        Off = 0,
        On = 1,
    }

    public enum SwitchVisualMode
    {
        [InspectorName("2D_Interact")]
        Mode2D = 0,

        [InspectorName("2D_UI")]
        Mode2DUI = 1,

        [InspectorName("3D")]
        Mode3D = 2,

        [InspectorName("Hide")]
        Hide = 3,
    }

    public enum UseTextMode
    {
        Off = 0,
        On = 1,
    }

    public enum UseContactTouchMode
    {
        Off = 0,
        On = 1,
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SwitchBase : UdonSharpBehaviour
    {


        #region フィールド

        [Header("■ SwitchMode")]
        [HelpBox("JP:\nLocal: ローカルのみ同期\nGlobal: 全体同期\nLocalSave: 各プレイヤーの状態を保存/復元\n\nEN:\nLocal: Local sync only\nGlobal: Global sync\nLocalSave: Save/restore each player's state")]
        [SerializeField] private SwitchSyncMode syncMode = SwitchSyncMode.Local;
        [SerializeField] private SwitchVisualMode switchVisualMode = SwitchVisualMode.Mode3D;
        [SerializeField] private SwitchMode mode = SwitchMode.Toggle;
        [SerializeField] private UseContactTouchMode useContactTouch = UseContactTouchMode.On;

        [Space(10)]
        [HelpBox("JP:\nスイッチの初期状態を設定します。エディタ上でデフォルト状態が反映されます。\nセーブデータがある場合はそちらが優先されます。\n\nEN:\nSet the initial state of the switch. The default state is reflected in the editor.\nSave data takes priority if available.")]
        [SerializeField] private ToggleDefaultState toggleDefaultOn = ToggleDefaultState.Off;
        [Header("--------------------------------------------------")]

        [Space(10)]
        [Header("■ Targets")]
        [HelpBox("JP:\nTarget:操作の対象を設定してください。\nTargetDisableは、Toggleモードでオンのときに非アクティブになるオブジェクトです。\n\nEN:\nTarget: Set the objects to operate.\nTargetDisable: Objects that become inactive when ON in Toggle mode.", HelpBoxAttribute.MessageType.Info)]
        [Tooltip("Toggle: ONで全てアクティブ / External: 外部スクリプト（例: RespawnSwitch）が参照する対象（=SwitchBase.Targets）")]
        [SerializeField] private GameObject[] targets;

        [Tooltip("Toggleのみ使用：ONのとき非アクティブ（OFFでアクティブ）")]
        [SerializeField] private GameObject[] targetDisables;

        [Header("--------------------------------------------------")]
        [Space(10)]
        [Header("■ Switch Text")]
        [SerializeField] private UseTextMode useText = UseTextMode.On;
        [HelpBox("JP:\nスイッチに表示するテキストの設定です。UseTextをOnにすると、2D/3Dテキストが表示されます。\n\nEN:\nSettings for text displayed on the switch. When UseText is On, 2D/3D text will be displayed.")]

        [Tooltip("有効なとき、Start/OnValidate で Text の表示/文字を自動更新します。")]
        [SerializeField] private bool TextAutoUpdate = true;

        [Tooltip("2D/3D の表示テキストに反映する文字列です（OnValidateで即時反映）。")]
        [SerializeField, TextArea] private string switchText;

        [SerializeField] private string switch_InteractionText = "Text";
        [HelpBox("JP:\nVRChatでUseしようとするときに浮かび上がるテキストです。\nTextAutoUpdateが有効な場合、自動更新されます。\n\nEN:\nText that appears when trying to Use in VRChat.\nAutomatically updated when TextAutoUpdate is enabled.")]


        [Space(10)]

        [Header("--------------------------------------------------")]
        [Header("■ LocalSave")]
        [SerializeField] private string persistanceKey = "Switch_Value";
        [HelpBox("JP:\nLocalSaveモード使用時に、各プレイヤーの状態を保存/復元するためのキーを設定します。\n\nEN:\nSet the key for saving/restoring each player's state when using LocalSave mode.")]


        [Space(10)]
        [Header("■ External")]

        [Tooltip("Externalモードの送信先UdonBehaviourを直指定します（例: RespawnSwitch）。\n設定されている場合はこのUdonBehaviourにSendCustomEventし、未設定の場合のみTargets上のUdonBehaviourを走査して送信します")]
        [SerializeField] private UdonBehaviour externalScript;
        [HelpBox("JP:\nExternalモード使用時に、押下時のイベントを送信する外部スクリプトを設定します。\n\nEN:\nSet the external script to send events to when pressed in External mode.")]
        [Tooltip("ExternalモードはPush限定。押したらこのイベント名を外部スクリプト（externalScript）に SendCustomEvent します（例: RespawnSwitchの Respawn）")]
        [SerializeField] private string externalEventName = "TriggerPush";

        [Space(100)]
        [Header("----------System（変更不要）----------")]
        [HelpBox("JP:\n以下の設定は通常変更する必要はありません。システム内部で使用されます。\n\nEN:\nThe following settings do not usually need to be changed. They are used internally by the system.")]
        [Header("■ Sound")]
        [SerializeField] private AudioSource switchAudioSource;
        [SerializeField] private AudioClip switchOnClip_3D;
        [SerializeField] private AudioClip switchOnClip_2D;
        [SerializeField] private AudioClip switchOffClip_3D;
        [SerializeField] private AudioClip switchOffClip_2D;

        [Space(10)]
        [Header("■ Text Components")]
        [Tooltip("2D(UGUI)のテキスト見た目制御対象です。\nToggle状態に応じて、TextMeshProUGUIのVertexColor（color）を変更します。")]
        [SerializeField] private TextMeshProUGUI[] _2DSwitch_Text;

        [Tooltip("3D(UGUI)のテキスト表示対象です。\nSwitch Text の内容を反映します（OnValidateで即時反映）。")]
        [SerializeField] private TextMeshProUGUI[] _3DSwitch_Text;

        [Space(10)]
        [Header("■ Animation")]
        [Tooltip("Animatorへ状態を送ります。\nToggle: Animatorの bool 'isOn' を更新\nExternal: trigger 'push' を発火")]
        [SerializeField] private Animator[] animators;

        [Space(10)]
        [Header("■ Haptics")]
        [SerializeField] private float hapticsDurationSeconds = 0.15f;
        [SerializeField, Range(0f, 1f)] private float hapticsAmplitude = 1.0f;
        [SerializeField, Range(0f, 1f)] private float hapticsFrequency = 0.9f;

        [Space(10)]
        [Header("■ 2D/3D Mode Objects")]
        [Tooltip("3Dモードで有効にするオブジェクト（未設定なら何もしません）")]
        [SerializeField] private GameObject _3DModeObject;

        [Tooltip("2Dモードで有効にするオブジェクト（未設定なら何もしません）")]
        [SerializeField] private GameObject _2DModeObject;

        [Tooltip("2Dモードでのみ使用する Switch_Trigger です。\n2D(UI)では非アクティブにします。\n3Dでは何もしません（既存挙動維持）。")]
        [SerializeField] private Switch_Trigger _switchTrigger2D;

        [Tooltip("2D(UI)モードでのみ有効化するColliderです。\n2D/UI以外(2D/3D)では Disable されます。")]
        [SerializeField] private Collider switchCollider2D;

        [Space(10)]

        [Tooltip("エディタ上のデフォルト状態表示（OnValidate）に使用します。\n実行時の見た目はAnimatorで管理する方針のため、このスクリプトはSwitchImageを更新しません。")]
        [SerializeField] private MeshRenderer[] switchImages;

        [Tooltip("2D(UGUI)の見た目制御対象です。\nToggle状態に応じて、ボタンの色を変更します。")]
        [SerializeField] private Button[] _2DSwitch_Buttons;

        [Tooltip("2D(UGUI)の見た目制御対象です。\nToggle状態に応じて、Imageの色を変更します。")]
        [SerializeField] private Image[] _2DSwitch_Images;

        [Tooltip("Buttonフィールドへの入力補助（ON/アクティブの基準色）。\nこの色を変更すると OnValidate で各Buttonの ColorBlock を即座に更新します。")]
        [SerializeField] private Color _2DSwitch_ActiveColor = Color.white;

        [Tooltip("Buttonフィールドへの入力補助（OFF/非アクティブの基準色）。\nこの色を変更すると OnValidate で各Buttonの ColorBlock を即座に更新します。")]
        [SerializeField] private Color _2DSwitch_DisableColor = Color.gray;

        [Header("■ InteractionText Settings")]
        [Tooltip("InteractionText設定用のUdonBehaviour配列です。\nTextAutoUpdateが有効な場合、エディタ/実行時に switch_InteractionText を反映します。")]
        [SerializeField] private UdonBehaviour[] targetUdonBehaviours;

        [Header("■ Contact Receiver Settings")]
        [Tooltip("Physbone接触イベントを受け取るコンポーネントです。\n接触イベントをUI_Click()に転送します。")]
        [SerializeField] private VRCContactReceiver[] contactReceivers;


        #endregion

        #region 公開プロパティ

        public SwitchMode Mode => mode;
        public SwitchSyncMode SyncMode => syncMode;
        public bool ToggleIsOn => _toggleIsOn;
        public GameObject[] Targets => targets;
        public bool HasLocalSaveRestored => _localSaveRestored;
        public int LocalSaveRestoredInt => _localSaveRestoredInt;

        #endregion

        #region ランタイムフィールド

        private VRCPlayerApi _localPlayer;
        private MaterialPropertyBlock _switchImagePropertyBlock;

        private bool IsGlobal => syncMode == SwitchSyncMode.Global;
        private bool IsLocalSave => syncMode == SwitchSyncMode.LocalSave;

        private bool _toggleIsOn;
        [UdonSynced] private bool _syncedToggleIsOn;

        private bool _localSaveRestored;
        private int _localSaveRestoredInt;

        private SwitchSelector _selector;
        private bool _suppressSelectorNotify;

        private bool _isInteractable = true;
        private bool _isHidden = false;
        private bool _initialized = false;

        private const string SwitchImageValuePropertyName = "_Value";
        private const string AnimatorIsOnParameterName = "isOn";
        private const string AnimatorPushTriggerName = "push";
        private const float HandDetectMaxDistance = 0.25f;

        /// <summary>
        /// 現在のビジュアルモードに応じて、ON時に再生するクリップを返します。
        /// 3D: switchOnClip_3D / 2D(2D・2DUI): switchOnClip_2D
        /// </summary>
        private AudioClip switchOnClip
        {
            get
            {
                // 要件: 3DモードON時は3D用、2DモードON時は2D用
                // ただし未設定時の事故を減らすため、反対側が設定されていればフォールバックします。
                if (switchVisualMode == SwitchVisualMode.Mode3D)
                {
                    return switchOnClip_3D != null ? switchOnClip_3D : switchOnClip_2D;
                }

                return switchOnClip_2D != null ? switchOnClip_2D : switchOnClip_3D;
            }
        }

        /// <summary>
        /// 現在のビジュアルモードに応じて、OFF時に再生するクリップを返します。
        /// 3D: switchOffClip_3D / 2D(2D・2DUI): switchOffClip_2D
        /// </summary>
        private AudioClip switchOffClip
        {
            get
            {
                // 3DモードOFF時は3D用、2DモードOFF時は2D用
                // 未設定時のフォールバックもON側と同様。
                if (switchVisualMode == SwitchVisualMode.Mode3D)
                {
                    return switchOffClip_3D != null ? switchOffClip_3D : switchOffClip_2D;
                }

                return switchOffClip_2D != null ? switchOffClip_2D : switchOffClip_3D;
            }
        }

        #endregion

        /// <summary>
        /// SwitchSelectorを設定します。
        /// </summary>
        /// <param name="selector">設定するSwitchSelector。nullの場合はインタラクト可能状態をリセットします。</param>
        public void SetSelector(SwitchSelector selector)
        {
            _selector = selector;

            // Selector影響下から外れた場合は、インタラクト可能状態をリセット
            if (selector == null)
            {
                _isInteractable = true;
            }
        }

        /// <summary>
        /// スイッチのインタラクト可能状態を設定します。
        /// </summary>
        /// <param name="interactable">インタラクト可能かどうか</param>
        public void SetInteractable(bool interactable)
        {
            _isInteractable = interactable;
        }

        /// <summary>
        /// スイッチの見た目(メッシュ部分)を隠す/戻すための関数です。
        /// Hide中は _3DModeObject / _2DModeObject を両方非アクティブにします。
        /// </summary>
        /// <param name="hidden">true: 非表示 / false: 表示</param>
        public void SetHidden(bool hidden)
        {
            _isHidden = hidden;
            Apply2DModeObjects();
        }

        /// <summary>
        /// ランタイム時に同期モードを設定します。
        /// </summary>
        /// <param name="newMode">設定する同期モード</param>
        public void Runtime_SetSyncMode(SwitchSyncMode newMode)
        {
            syncMode = newMode;

            if (TextAutoUpdate)
            {
                ApplyInteractionText();
            }
        }

        /// <summary>
        /// ランタイム時にビジュアルモードを設定します。
        /// </summary>
        /// <param name="newMode">設定するビジュアルモード（2D、2D UI、3D）</param>
        public void Runtime_SetVisualMode(SwitchVisualMode newMode)
        {
            switchVisualMode = newMode;
            Apply2DModeObjects();
        }

        /// <summary>
        /// ランタイム時にContactTouchモードを設定します。
        /// </summary>
        /// <param name="newMode">設定するContactTouchモード（On/Off）</param>
        public void Runtime_SetContactTouchMode(UseContactTouchMode newMode)
        {
            useContactTouch = newMode;
            ApplyContactReceiverEnabled();
        }

        /// <summary>
        /// スイッチの初期化を行います。
        /// </summary>
        public virtual void Start()
        {
            _localPlayer = Networking.LocalPlayer;
            if (TextAutoUpdate)
            {
                ApplySwitchText();
                ApplyInteractionText();
            }
            ApplyContactReceiverEnabled();
            Apply2DModeObjects();
            InitializeByMode();
            _initialized = true;
        }

        /// <summary>
        /// オブジェクトが有効化された際に状態を復元します。
        /// </summary>
        private void OnEnable()
        {
            // Start前の初回OnEnableでは何もしない
            if (!_initialized)
            {
                return;
            }

            // 非アクティブから復帰した際に状態を復元
            if (TextAutoUpdate)
            {
                ApplySwitchText();
                ApplyInteractionText();
            }

            ApplyContactReceiverEnabled();
            Apply2DModeObjects();

            if (mode == SwitchMode.Toggle)
            {
                ApplyToggleTargetsState(_toggleIsOn);
            }
            else
            {
                // External
                SetAnimatorsIsOn(false);
            }
        }

        /// <summary>
        /// スイッチテキストを2D/3Dテキストコンポーネントに適用します。
        /// </summary>
        private void ApplySwitchText()
        {
            bool active = useText == UseTextMode.On;

            if (_2DSwitch_Text != null)
            {
                for (int i = 0; i < _2DSwitch_Text.Length; i++)
                {
                    var t = _2DSwitch_Text[i];
                    if (t == null)
                    {
                        continue;
                    }

                    t.gameObject.SetActive(active);
                    if (active)
                    {
                        t.text = switchText;
                    }
                }
            }

            if (_3DSwitch_Text != null)
            {
                for (int i = 0; i < _3DSwitch_Text.Length; i++)
                {
                    var t = _3DSwitch_Text[i];
                    if (t == null)
                    {
                        continue;
                    }

                    t.gameObject.SetActive(active);
                    if (active)
                    {
                        t.text = switchText;
                    }
                }
            }
        }

        /// <summary>
        /// switch_InteractionText を targetUdonBehaviours の InteractionText に適用します。
        /// </summary>
        private void ApplyInteractionText()
        {
            if (targetUdonBehaviours == null || targetUdonBehaviours.Length == 0)
            {
                return;
            }

            var nextText = BuildInteractionTextWithSyncMode();
            for (int i = 0; i < targetUdonBehaviours.Length; i++)
            {
                var udon = targetUdonBehaviours[i];
                if (udon == null)
                {
                    continue;
                }

                udon.InteractionText = nextText;
            }
        }

        private string BuildInteractionTextWithSyncMode()
        {
            var raw = (switch_InteractionText ?? string.Empty).Trim();

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

            // Externalモードの場合は同期モード表記を付けない（常にローカルイベント発火のみのため）
            if (mode == SwitchMode.External)
            {
                return raw;
            }

            // UdonSharpではenum.ToString()が数値を返すため、明示的に文字列化
            string syncModeText;

            // SwitchSelector 管理下の場合、SwitchBase の内部SyncModeとは別に
            // Selector のモードをユーザーに見せたい（特に LocalSave のとき）。
            if (_selector != null)
            {
                var selectorMode = _selector.SyncMode;
                if (selectorMode == SwitchSelectorSyncMode.Local)
                {
                    syncModeText = "Local";
                }
                else if (selectorMode == SwitchSelectorSyncMode.Global)
                {
                    syncModeText = "Global";
                }
                else if (selectorMode == SwitchSelectorSyncMode.LocalSave)
                {
                    syncModeText = "LocalSave";
                }
                else
                {
                    syncModeText = selectorMode.ToString();
                }
            }
            else
            {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
                if (!Application.isPlaying && TryGetSelectorSyncModeInEditor(out var selectorModeInEditor))
                {
                    if (selectorModeInEditor == SwitchSelectorSyncMode.Local)
                    {
                        syncModeText = "Local";
                    }
                    else if (selectorModeInEditor == SwitchSelectorSyncMode.Global)
                    {
                        syncModeText = "Global";
                    }
                    else if (selectorModeInEditor == SwitchSelectorSyncMode.LocalSave)
                    {
                        syncModeText = "LocalSave";
                    }
                    else
                    {
                        syncModeText = selectorModeInEditor.ToString();
                    }
                }
                else
#endif
                {
                    if (syncMode == SwitchSyncMode.Local)
                    {
                        syncModeText = "Local";
                    }
                    else if (syncMode == SwitchSyncMode.Global)
                    {
                        syncModeText = "Global";
                    }
                    else if (syncMode == SwitchSyncMode.LocalSave)
                    {
                        syncModeText = "LocalSave";
                    }
                    else
                    {
                        syncModeText = syncMode.ToString();
                    }
                }
            }

            var suffix = " (" + syncModeText + ")";
            if (raw.Length == 0)
            {
                return suffix.TrimStart();
            }

            return raw + suffix;
        }

        /// <summary>
        /// スイッチのモードに応じた初期化を行います。
        /// </summary>
        private void InitializeByMode()
        {
            if (mode == SwitchMode.Toggle)
            {
                InitializeToggle();
                return;
            }

            // External
            SetAnimatorsIsOn(false);
        }

        /// <summary>
        /// トグルモードの初期化を行います。
        /// </summary>
        private void InitializeToggle()
        {
            bool initial = toggleDefaultOn == ToggleDefaultState.On;

            if (!IsGlobal)
            {
                _toggleIsOn = initial;
                ApplyToggleTargetsState(_toggleIsOn);
                return;
            }

            if (Networking.IsOwner(gameObject))
            {
                _toggleIsOn = initial;
                _syncedToggleIsOn = _toggleIsOn;
                ApplyToggleTargetsState(_toggleIsOn);
                RequestSerialization();
            }
            else
            {
                _toggleIsOn = initial;
                ApplyToggleTargetsState(_toggleIsOn);
            }
        }


        #region ハプティクス

        /// <summary>
        /// 右手にハプティクスを与えます（VR時のみ）。
        /// </summary>
        public void Haptics_RightHand()
        {
            PlayHaptics(VRC_Pickup.PickupHand.Right);
        }

        /// <summary>
        /// 左手にハプティクスを与えます（VR時のみ）。
        /// </summary>
        public void Haptics_LeftHand()
        {
            PlayHaptics(VRC_Pickup.PickupHand.Left);
        }

        /// <summary>
        /// 指定した手にハプティクスフィードバックを再生します（VR時のみ）。
        /// </summary>
        /// <param name="hand">ハプティクスを再生する手</param>
        private void PlayHaptics(VRC_Pickup.PickupHand hand)
        {
            if (_localPlayer == null)
            {
                _localPlayer = Networking.LocalPlayer;
            }

            if (_localPlayer == null || !_localPlayer.IsUserInVR())
            {
                return;
            }

            _localPlayer.PlayHapticEventInHand(hand, hapticsDurationSeconds, hapticsAmplitude, hapticsFrequency);
        }

        #endregion

        #region サウンド

        /// <summary>
        /// スイッチONのサウンドを再生します。
        /// </summary>
        public void PlaySwitchOnSound()
        {
            PlayOneShotSafe(switchOnClip);
        }

        /// <summary>
        /// スイッチOFFのサウンドを再生します。
        /// </summary>
        public void PlaySwitchOffSound()
        {
            PlayOneShotSafe(switchOffClip);
        }

        /// <summary>
        /// 押下時のサウンド。基本はOnを優先し、無ければOffを使います。
        /// </summary>
        public void PlayPushSound()
        {
            if (switchOnClip != null)
            {
                PlayOneShotSafe(switchOnClip);
                return;
            }

            PlayOneShotSafe(switchOffClip);
        }

        /// <summary>
        /// 指定したオーディオクリップを安全に再生します。
        /// </summary>
        /// <param name="clip">再生するオーディオクリップ</param>
        private void PlayOneShotSafe(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (switchAudioSource == null)
            {
                return;
            }

            switchAudioSource.PlayOneShot(clip);
        }

        #endregion


        #region Trigger API（Switch_Trigger から呼ばれる）

        /// <summary>
        /// UI(Button)などから SendCustomEvent で呼ぶ用の入口です。
        /// Toggleならトグル、ExternalならPushとして動作します。
        /// </summary>
        public void UI_Click()
        {
            TriggerPush();
        }

        /// <summary>
        /// トグル動作をトリガーします。トグルモードでのみ機能します。
        /// </summary>
        public void TriggerToggle()
        {
            if (!_isInteractable)
            {
                return;
            }

            if (mode != SwitchMode.Toggle)
            {
                return;
            }

            Toggle_SetOn(!_toggleIsOn);
        }

        /// <summary>
        /// プッシュ動作をトリガーします。トグルモードではトグル、Externalモードでは外部イベントを送信します。
        /// </summary>
        public void TriggerPush()
        {
            if (!_isInteractable)
            {
                return;
            }

            if (mode == SwitchMode.Toggle)
            {
                Toggle_SetOn(!_toggleIsOn);
                return;
            }

            // External（Push限定）
            External_Send(externalEventName);
            FireAnimatorsPush();
            PlayInteractFeedback();
        }

        /// <summary>
        /// Externalモードのビジュアルをリセットします。
        /// </summary>
        public void ResetExternalVisuals()
        {
            if (mode != SwitchMode.External)
            {
                return;
            }
        }

        #endregion


        #region Feedback（Sound/Haptics）

        /// <summary>
        /// インタラクト時のフィードバック（サウンド、ハプティクス）を再生します。
        /// </summary>
        private void PlayInteractFeedback()
        {
            PlayPushSound();

            if (TryDetectInteractingHand(out var usedHand))
            {
                PlayHaptics(usedHand);
            }
            else
            {
                PlayHaptics(VRC_Pickup.PickupHand.Left);
                PlayHaptics(VRC_Pickup.PickupHand.Right);
            }
        }

        /// <summary>
        /// トグル時のフィードバック（サウンド、ハプティクス）を再生します。
        /// </summary>
        /// <param name="isOn">トグル状態（ON/OFF）</param>
        private void PlayToggleFeedback(bool isOn)
        {
            if (isOn)
            {
                PlaySwitchOnSound();
            }
            else
            {
                PlaySwitchOffSound();
            }

            if (TryDetectInteractingHand(out var usedHand))
            {
                PlayHaptics(usedHand);
            }
            else
            {
                PlayHaptics(VRC_Pickup.PickupHand.Left);
                PlayHaptics(VRC_Pickup.PickupHand.Right);
            }
        }

        #endregion


        #region Toggle 実装

        /// <summary>
        /// トグルの状態を設定します（内部用）。
        /// </summary>
        /// <param name="on">設定する状態（true: ON、false: OFF）</param>
        private void Toggle_SetOn(bool on)
        {
            if (mode != SwitchMode.Toggle)
            {
                return;
            }

            SetToggleState(on, syncNetwork: IsGlobal, playFeedback: true, notifySelector: true);
        }

        /// <summary>
        /// 外部からトグル状態を適用します。
        /// </summary>
        /// <param name="on">設定する状態（true: ON、false: OFF）</param>
        /// <param name="syncNetwork">ネットワーク同期を行うかどうか</param>
        public void ApplyToggleStateFromExternal(bool on, bool syncNetwork)
        {
            if (mode != SwitchMode.Toggle)
            {
                return;
            }

            SetToggleState(on, syncNetwork: IsGlobal && syncNetwork, playFeedback: false, notifySelector: false);
        }

        /// <summary>
        /// SwitchSelectorからトグル状態を適用します。
        /// </summary>
        /// <param name="on">設定する状態（true: ON、false: OFF）</param>
        public void ApplyToggleStateFromSelector(bool on)
        {
            if (mode != SwitchMode.Toggle)
            {
                return;
            }

            SetToggleState(on, syncNetwork: IsGlobal, playFeedback: false, notifySelector: false);
        }

        /// <summary>
        /// トグル状態を設定し、必要に応じて同期、フィードバック、通知を行います。
        /// </summary>
        /// <param name="on">設定する状態（true: ON、false: OFF）</param>
        /// <param name="syncNetwork">ネットワーク同期を行うかどうか</param>
        /// <param name="playFeedback">フィードバックを再生するかどうか</param>
        /// <param name="notifySelector">Selectorに通知するかどうか</param>
        private void SetToggleState(bool on, bool syncNetwork, bool playFeedback, bool notifySelector)
        {
            if (IsGlobal && syncNetwork)
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            bool changed = _toggleIsOn != on;
            _toggleIsOn = on;

            if (changed)
            {
                ApplyToggleTargetsState(_toggleIsOn);
            }

            if (IsGlobal && syncNetwork)
            {
                _syncedToggleIsOn = _toggleIsOn;
                RequestSerialization();
            }
            else
            {
                SaveLocalIfNeeded(_toggleIsOn ? 1 : 0);
            }

            if (playFeedback)
            {
                PlayToggleFeedback(_toggleIsOn);
            }

            if (notifySelector)
            {
                NotifySelector();
            }
        }

        /// <summary>
        /// Selectorにスイッチ状態の変更を通知します。
        /// </summary>
        private void NotifySelector()
        {
            if (_selector == null)
            {
                return;
            }

            if (_suppressSelectorNotify)
            {
                return;
            }

            _selector.OnSwitchStateChanged(this, _toggleIsOn);
        }

        /// <summary>
        /// トグルのターゲットオブジェクトに状態を適用します。
        /// </summary>
        /// <param name="state">適用する状態（true: ON、false: OFF）</param>
        private void ApplyToggleTargetsState(bool state)
        {
            SetActiveSafe(targets, state);
            SetActiveSafe(targetDisables, !state);
            SetAnimatorsIsOn(state);
            Apply2DSwitchButtonsVisual(state);
        }

        /// <summary>
        /// トグル状態をスイッチ画像に適用します。
        /// </summary>
        /// <param name="isOn">トグル状態（true: ON、false: OFF）</param>
        private void ApplySwitchImageForToggle(bool isOn)
        {
            ApplySwitchImageValueAll(isOn ? 1f : 0f);
        }

        /// <summary>
        /// 全てのスイッチ画像に値を適用します。
        /// </summary>
        /// <param name="value">適用する値（0.0～1.0）</param>
        private void ApplySwitchImageValueAll(float value)
        {
            if (switchImages == null)
            {
                return;
            }

            for (int i = 0; i < switchImages.Length; i++)
            {
                ApplySwitchImageValue(switchImages[i], value);
            }
        }

        /// <summary>
        /// 2Dスイッチボタンのビジュアルを状態に応じて適用します。
        /// </summary>
        /// <param name="isOn">トグル状態（true: ON、false: OFF）</param>
        private void Apply2DSwitchButtonsVisual(bool isOn)
        {
            if (_2DSwitch_Buttons == null || _2DSwitch_Buttons.Length == 0)
            {
                Apply2DSwitchTextVisual(isOn, pressedColor: DerivePressedColor(_2DSwitch_ActiveColor), disabledColor: DeriveDisabledColor(_2DSwitch_DisableColor));
                return;
            }

            // 状態表示の基準色（要件：ON=PressedColor / OFF=DisabledColor）
            var activePressed = DerivePressedColor(_2DSwitch_ActiveColor);
            var inactiveDisabled = DeriveDisabledColor(_2DSwitch_DisableColor);

            for (int i = 0; i < _2DSwitch_Buttons.Length; i++)
            {
                var b = _2DSwitch_Buttons[i];
                if (b == null)
                {
                    continue;
                }

                var colors = b.colors;
                colors.pressedColor = activePressed;
                colors.disabledColor = inactiveDisabled;

                var stateBase = isOn ? activePressed : inactiveDisabled;
                colors.normalColor = stateBase;
                colors.highlightedColor = DeriveHighlightedColor(stateBase);
                colors.selectedColor = DeriveSelectedColor(stateBase);

                b.colors = colors;
            }

            // テキストも同じルールで連動
            Apply2DSwitchTextVisual(isOn, pressedColor: activePressed, disabledColor: inactiveDisabled);

            // Imageも同じルールで連動
            Apply2DSwitchImagesVisual(isOn, activeColor: activePressed, inactiveColor: inactiveDisabled);
        }

        /// <summary>
        /// 2Dスイッチテキストのビジュアルを状態に応じて適用します。
        /// </summary>
        /// <param name="isOn">トグル状態（true: ON、false: OFF）</param>
        /// <param name="pressedColor">ON時の色</param>
        /// <param name="disabledColor">OFF時の色</param>
        private void Apply2DSwitchTextVisual(bool isOn, Color pressedColor, Color disabledColor)
        {
            if (_2DSwitch_Text == null || _2DSwitch_Text.Length == 0)
            {
                return;
            }

            var c = isOn ? pressedColor : disabledColor;
            for (int i = 0; i < _2DSwitch_Text.Length; i++)
            {
                var t = _2DSwitch_Text[i];
                if (t == null)
                {
                    continue;
                }

                t.color = c;
            }
        }

        /// <summary>
        /// 2Dスイッチ画像のビジュアルを状態に応じて適用します。
        /// </summary>
        /// <param name="isOn">トグル状態（true: ON、false: OFF）</param>
        /// <param name="activeColor">アクティブ時の色</param>
        /// <param name="inactiveColor">非アクティブ時の色</param>
        private void Apply2DSwitchImagesVisual(bool isOn, Color activeColor, Color inactiveColor)
        {
            if (_2DSwitch_Images == null || _2DSwitch_Images.Length == 0)
            {
                return;
            }

            var c = isOn ? activeColor : inactiveColor;
            for (int i = 0; i < _2DSwitch_Images.Length; i++)
            {
                var img = _2DSwitch_Images[i];
                if (img == null)
                {
                    continue;
                }

                img.color = c;
            }
        }

        /// <summary>
        /// ハイライト時の色を基本色から導出します。
        /// </summary>
        /// <param name="baseColor">基本色</param>
        /// <returns>ハイライト時の色</returns>
        private Color DeriveHighlightedColor(Color baseColor)
        {
            // 少し明るく、少し彩度を落とす
            return AdjustHSV(baseColor, sMul: 0.92f, vMul: 1.12f, aMul: 1f);
        }

        /// <summary>
        /// プレス時の色を基本色から導出します。
        /// </summary>
        /// <param name="baseColor">基本色</param>
        /// <returns>プレス時の色</returns>
        private Color DerivePressedColor(Color baseColor)
        {
            // 少し暗く、少し彩度を上げる
            return AdjustHSV(baseColor, sMul: 1.05f, vMul: 0.85f, aMul: 1f);
        }

        /// <summary>
        /// 選択時の色を基本色から導出します。
        /// </summary>
        /// <param name="baseColor">基本色</param>
        /// <returns>選択時の色</returns>
        private Color DeriveSelectedColor(Color baseColor)
        {
            // Highlighted寄り（軽く明るい）
            return AdjustHSV(baseColor, sMul: 0.97f, vMul: 1.06f, aMul: 1f);
        }

        /// <summary>
        /// 無効時の色を基本色から導出します。
        /// </summary>
        /// <param name="baseColor">基本色</param>
        /// <returns>無効時の色</returns>
        private Color DeriveDisabledColor(Color baseColor)
        {
            // 暗く・彩度を落として、Alphaも少し落とす
            return AdjustHSV(baseColor, sMul: 0.25f, vMul: 0.65f, aMul: 0.65f);
        }

        /// <summary>
        /// HSV値を調整して新しい色を生成します。
        /// </summary>
        private Color AdjustHSV(Color color, float sMul, float vMul, float aMul)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * sMul);
            v = Mathf.Clamp01(v * vMul);

            var rgb = Color.HSVToRGB(h, s, v);
            rgb.a = Mathf.Clamp01(color.a * aMul);
            return rgb;
        }

        /// <summary>
        /// MeshRendererに対してスイッチ画像の値を適用します。
        /// </summary>
        private void ApplySwitchImageValue(MeshRenderer renderer, float value)
        {
            if (renderer == null)
            {
                return;
            }

            if (_switchImagePropertyBlock == null)
            {
                _switchImagePropertyBlock = new MaterialPropertyBlock();
            }

            renderer.GetPropertyBlock(_switchImagePropertyBlock);
            _switchImagePropertyBlock.SetFloat(SwitchImageValuePropertyName, value);
            renderer.SetPropertyBlock(_switchImagePropertyBlock);
        }

        /// <summary>
        /// 全てのAnimatorにトグル状態を設定します。
        /// </summary>
        /// <param name="isOn">トグル状態（true: ON、false: OFF）</param>
        private void SetAnimatorsIsOn(bool isOn)
        {
            if (animators == null || animators.Length <= 0)
            {
                return;
            }

            for (int i = 0; i < animators.Length; i++)
            {
                var a = animators[i];
                if (a == null || !a.enabled || !a.gameObject.activeInHierarchy || a.runtimeAnimatorController == null)
                {
                    continue;
                }

                a.SetBool(AnimatorIsOnParameterName, isOn);
            }
        }

        /// <summary>
        /// 全てのAnimatorにプッシュトリガーを発火します。
        /// </summary>
        private void FireAnimatorsPush()
        {
            if (animators == null)
            {
                return;
            }

            for (int i = 0; i < animators.Length; i++)
            {
                var a = animators[i];
                if (a == null || !a.enabled || !a.gameObject.activeInHierarchy || a.runtimeAnimatorController == null)
                {
                    continue;
                }

                a.SetTrigger(AnimatorPushTriggerName);
            }
        }

        #endregion


        #region External 実装（Targets上のUdonBehaviourへSendCustomEvent）

        /// <summary>
        /// 外部スクリプトにイベントを送信します（Externalモード専用）。
        /// </summary>
        /// <param name="eventName">送信するイベント名</param>
        private void External_Send(string eventName)
        {
            if (mode != SwitchMode.External)
            {
                return;
            }

            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }

            if (externalScript != null)
            {
                externalScript.SendCustomEvent(eventName);
                return;
            }

            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var obj = targets[i];
                if (obj == null)
                {
                    continue;
                }

                var udons = obj.GetComponents<UdonBehaviour>();
                if (udons == null || udons.Length == 0)
                {
                    continue;
                }

                for (int j = 0; j < udons.Length; j++)
                {
                    var u = udons[j];
                    if (u == null)
                    {
                        continue;
                    }

                    u.SendCustomEvent(eventName);
                }
            }
        }

        #endregion


        #region Sync / Persistence

        /// <summary>
        /// ネットワーク同期時のデシリアライゼーション処理を行います。
        /// </summary>
        public override void OnDeserialization()
        {
            // NOTE: IsGlobal チェックをここでは行わない。
            // SwitchSelector (Global) 使用時、SwitchBase の syncMode は Start() 時点ではまだ Local（Inspector デフォルト）であり、
            // ForceSyncModeToSwitches() が呼ばれるのは DeferredInitialize（1フレーム後）のため、
            // IsGlobal=false の間に OnDeserialization が届くと正しい同期状態が捨てられてしまう。
            // BehaviourSyncMode.Manual では OnDeserialization は RequestSerialization() が呼ばれた時のみ発火するため、
            // モードに関わらず常に処理することが安全。
            if (mode != SwitchMode.Toggle)
            {
                return;
            }

            _toggleIsOn = _syncedToggleIsOn;
            ApplyToggleTargetsState(_toggleIsOn);

            if (_selector != null)
            {
                _selector.RefreshSelectionStateFromSwitches();
            }
        }

        /// <summary>
        /// プレイヤーの状態が復元された際の処理を行います。
        /// </summary>
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

            string key = GetPersistenceKey();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!PlayerData.TryGetInt(player, key, out int saved))
            {
                return;
            }

            _localSaveRestored = true;
            _localSaveRestoredInt = saved;

            if (mode == SwitchMode.Toggle)
            {
                _toggleIsOn = saved != 0;
                ApplyToggleTargetsState(_toggleIsOn);
                NotifySelector();
                return;
            }
        }

        /// <summary>
        /// 永続化キーを取得します。
        /// </summary>
        private string GetPersistenceKey() => persistanceKey;

        /// <summary>
        /// 必要に応じてローカル保存を行います。
        /// </summary>
        private void SaveLocalIfNeeded(int value)
        {
            if (!IsLocalSave)
            {
                return;
            }

            string key = GetPersistenceKey();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            PlayerData.SetInt(key, value);
        }

        #endregion


        #region 2D Mode

        private bool Is2DVisualMode => switchVisualMode == SwitchVisualMode.Mode2D || switchVisualMode == SwitchVisualMode.Mode2DUI;
        private bool IsHideVisualMode => switchVisualMode == SwitchVisualMode.Hide;

        /// <summary>
        /// 2D/3Dモードに応じてオブジェクトの表示を切り替えます。
        /// </summary>
        private void Apply2DModeObjects()
        {
            if (_isHidden || IsHideVisualMode)
            {
                SetActiveSafe(_3DModeObject, false);
                SetActiveSafe(_2DModeObject, false);

                // Hide時は取り残しが出ないよう、2D専用トリガー/コライダーも確実に無効化します。
                if (_switchTrigger2D != null)
                {
                    SetActiveSafe(_switchTrigger2D.gameObject, false);
                }

                if (switchCollider2D != null)
                {
                    switchCollider2D.enabled = false;
                }
                return;
            }

            // ユーザーが触らなくても良い補助。未設定なら何もしない。
            SetActiveSafe(_3DModeObject, switchVisualMode == SwitchVisualMode.Mode3D);
            SetActiveSafe(_2DModeObject, Is2DVisualMode);

            // 2D専用トリガー：2Dでのみ使用。2D(UI)のときは無効化。
            if (_switchTrigger2D != null)
            {
                if (switchVisualMode == SwitchVisualMode.Mode2D)
                {
                    SetActiveSafe(_switchTrigger2D.gameObject, true);
                }
                else if (switchVisualMode == SwitchVisualMode.Mode2DUI)
                {
                    SetActiveSafe(_switchTrigger2D.gameObject, false);
                }
            }

            // 2D(UI)のときだけ、2D用Colliderを有効化します。
            if (switchCollider2D != null)
            {
                switchCollider2D.enabled = switchVisualMode == SwitchVisualMode.Mode2DUI;
            }
        }

        /// <summary>
        /// UseContactTouchの設定に応じて、ContactReceiverの有効/無効を切り替えます。
        /// Off: Disable / On: Enable
        /// </summary>
        private void ApplyContactReceiverEnabled()
        {
            if (contactReceivers == null || contactReceivers.Length == 0)
            {
                return;
            }

            bool shouldEnable = useContactTouch == UseContactTouchMode.On;
            for (int i = 0; i < contactReceivers.Length; i++)
            {
                var receiver = contactReceivers[i];
                if (receiver == null)
                {
                    continue;
                }

                receiver.enabled = shouldEnable;
            }
        }

        #endregion


        #region 手の判定

        /// <summary>
        /// インタラクト中の手を検出します（VR時のみ）。
        /// </summary>
        /// <param name="hand">検出された手</param>
        /// <returns>手の検出に成功した場合true</returns>
        private bool TryDetectInteractingHand(out VRC_Pickup.PickupHand hand)
        {
            hand = VRC_Pickup.PickupHand.Right;

            if (_localPlayer == null)
            {
                _localPlayer = Networking.LocalPlayer;
            }

            if (_localPlayer == null || !_localPlayer.IsUserInVR())
            {
                return false;
            }

            var referencePos = transform.position;

            var leftPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            var rightPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;

            var leftDist = Vector3.Distance(leftPos, referencePos);
            var rightDist = Vector3.Distance(rightPos, referencePos);

            var minDist = leftDist < rightDist ? leftDist : rightDist;
            if (minDist > HandDetectMaxDistance)
            {
                return false;
            }

            hand = leftDist <= rightDist ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right;
            return true;
        }

        #endregion


        /// <summary>
        /// GameObjectの配列に対して安全にアクティブ状態を設定します。
        /// </summary>
        private void SetActiveSafe(GameObject[] targets, bool active)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (target == null)
                {
                    continue;
                }

                target.SetActive(active);
            }
        }

        /// <summary>
        /// GameObjectに対して安全にアクティブ状態を設定します。
        /// </summary>
        private void SetActiveSafe(GameObject target, bool active)
        {
            if (target == null)
            {
                return;
            }

            target.SetActive(active);
        }


        #region エディタサポート

#if UNITY_EDITOR && !COMPILER_UDONSHARP

        /// <summary>
        /// エディタ上で、インスペクタ設定に基づくデフォルトの見た目へ戻します。
        /// MobilePreviewの解除時などの「復帰」用途を想定しています。
        /// </summary>
        public void Editor_ApplyDefaultVisual()
        {
            if (Application.isPlaying)
            {
                return;
            }

            Editor_EnsureInteractionTextFieldShowsSyncMode();

            if (TextAutoUpdate)
            {
                ApplySwitchText();
                Editor_ApplyInteractionText();
            }

            ApplyContactReceiverEnabled();
            Apply2DModeObjects();

            if (IsManagedByAnySelectorInEditor())
            {
                return;
            }

            if (mode == SwitchMode.Toggle)
            {
                bool initialOn = toggleDefaultOn == ToggleDefaultState.On;
                SetActiveSafe(targets, initialOn);
                SetActiveSafe(targetDisables, !initialOn);
                SetAnimatorsIsOn(initialOn);
                ApplySwitchImageForToggle(initialOn);
                Apply2DSwitchButtonsVisual(initialOn);
                return;
            }

            // External
            SetAnimatorsIsOn(false);
        }

        /// <summary>
        /// エディタ上で同期モードを設定します。
        /// </summary>
        public void Editor_SetSyncMode(SwitchSyncMode newMode)
        {
            if (Application.isPlaying)
            {
                return;
            }

            syncMode = newMode;

            Editor_EnsureInteractionTextFieldShowsSyncMode();
            if (TextAutoUpdate)
            {
                Editor_ApplyInteractionText();
            }
        }

        /// <summary>
        /// エディタ上でビジュアルモードを設定します。
        /// </summary>
        public void Editor_SetVisualMode(SwitchVisualMode newMode)
        {
            if (Application.isPlaying)
            {
                return;
            }

            switchVisualMode = newMode;
            Apply2DModeObjects();
        }

        /// <summary>
        /// エディタ上でトグルのビジュアルを適用します。
        /// </summary>
        public void Editor_ApplyToggleVisual(bool isOn)
        {
            if (Application.isPlaying)
            {
                return;
            }

            Apply2DModeObjects();

            if (mode != SwitchMode.Toggle)
            {
                return;
            }

            SetActiveSafe(targets, isOn);
            SetActiveSafe(targetDisables, !isOn);
            SetAnimatorsIsOn(isOn);
            ApplySwitchImageForToggle(isOn);
            Apply2DSwitchButtonsVisual(isOn);
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

            Editor_EnsureInteractionTextFieldShowsSyncMode();

            if (TextAutoUpdate)
            {
                ApplySwitchText();
                Editor_ApplyInteractionText();
            }

            ApplyContactReceiverEnabled();
            Apply2DModeObjects();

            if (IsManagedByAnySelectorInEditor())
            {
                return;
            }

            if (mode == SwitchMode.Toggle)
            {
                bool initialOn = toggleDefaultOn == ToggleDefaultState.On;
                SetActiveSafe(targets, initialOn);
                SetActiveSafe(targetDisables, !initialOn);
                SetAnimatorsIsOn(initialOn);
                ApplySwitchImageForToggle(initialOn);
                Apply2DSwitchButtonsVisual(initialOn);
                return;
            }

            // External
            SetAnimatorsIsOn(false);
        }

        private void Editor_EnsureInteractionTextFieldShowsSyncMode()
        {
            var desired = BuildInteractionTextWithSyncMode();
            if (switch_InteractionText == desired)
            {
                return;
            }

            Undo.RecordObject(this, "Update Switch InteractionText");
            switch_InteractionText = desired;
            EditorUtility.SetDirty(this);
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        private void Editor_ApplyInteractionText()
        {
            if (targetUdonBehaviours == null || targetUdonBehaviours.Length == 0)
            {
                return;
            }

            var nextText = BuildInteractionTextWithSyncMode();

            int count = 0;
            for (int i = 0; i < targetUdonBehaviours.Length; i++)
            {
                var udon = targetUdonBehaviours[i];
                if (udon == null)
                {
                    continue;
                }

                if (udon.InteractionText == nextText)
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
            for (int i = 0; i < targetUdonBehaviours.Length; i++)
            {
                var udon = targetUdonBehaviours[i];
                if (udon == null)
                {
                    continue;
                }

                if (udon.InteractionText == nextText)
                {
                    continue;
                }

                toRecord[writeIndex] = udon;
                writeIndex++;
            }

            Undo.RecordObjects(toRecord, "Update Udon InteractionText");
            for (int i = 0; i < toRecord.Length; i++)
            {
                var udon = (UdonBehaviour)toRecord[i];
                udon.InteractionText = nextText;
                EditorUtility.SetDirty(udon);
                PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
            }
        }

        /// <summary>
        /// エディタ上で、いずれかのSwitchSelectorによって管理されているかを判定します。
        /// </summary>
        private bool IsManagedByAnySelectorInEditor()
        {
            return TryFindManagingSelectorInEditor(out _);
        }

        private bool TryGetSelectorSyncModeInEditor(out SwitchSelectorSyncMode selectorMode)
        {
            selectorMode = SwitchSelectorSyncMode.Local;

            if (!TryFindManagingSelectorInEditor(out var selector))
            {
                return false;
            }

            selectorMode = selector.SyncMode;
            return true;
        }

        private bool TryFindManagingSelectorInEditor(out SwitchSelector selector)
        {
            selector = null;

            var root = transform.root;
            if (root != null)
            {
                var localSelectors = root.GetComponentsInChildren<SwitchSelector>(true);
                if (localSelectors != null && localSelectors.Length > 0)
                {
                    for (int i = 0; i < localSelectors.Length; i++)
                    {
                        var sel = localSelectors[i];
                        if (sel == null)
                        {
                            continue;
                        }

                        if (!sel.Editor_ReferencesSwitch(this))
                        {
                            continue;
                        }

                        selector = sel;
                        return true;
                    }
                }
            }

            var selectors = Object.FindObjectsOfType<SwitchSelector>(true);
            if (selectors == null || selectors.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < selectors.Length; i++)
            {
                var sel = selectors[i];
                if (sel == null)
                {
                    continue;
                }

                if (!sel.Editor_ReferencesSwitch(this))
                {
                    continue;
                }

                selector = sel;
                return true;
            }

            return false;
        }
#endif

        #endregion
    }
}
