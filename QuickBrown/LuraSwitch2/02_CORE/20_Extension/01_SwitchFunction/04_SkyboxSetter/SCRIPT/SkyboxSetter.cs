
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;


#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
#endif

public enum SkyboxPreview
{
    Off,  // プレビューなし
    On        // プレビュー表示
}

/// <summary>
/// Skyboxと霧設定を制御するコンポーネント。
/// SkyboxSetter_Activatorからの制御により、シーンのSkyboxと霧設定を切り替えます。
/// </summary>
public class SkyboxSetter : UdonSharpBehaviour
{
    [Space(10)]
    [Header("■ Editor Preview")]
    [HelpBox("エディタで変更した設定をプレビューすることができます。\nランタイムでは無視されます。", HelpBoxAttribute.MessageType.Info)]
    [Tooltip("エディタでのプレビュー表示（ランタイムでは無視されます）")]
    public SkyboxPreview preview = SkyboxPreview.Off;

    [Space(30)]

    #region フィールド
    [HelpBox("JP:\nアクティブ時に適用するSkyboxとfogの設定を指定します。\n\nEN:\nSpecify the Skybox and fog settings to apply when active.", HelpBoxAttribute.MessageType.Info)]

    [Header("■ Skybox and Fog Settings")]
    [Tooltip("アクティブ時に適用するSkyboxマテリアル")]
    [SerializeField] private Material customSkyboxMaterial;

    [Tooltip("アクティブ時に霧を有効にするかどうか")]
    [SerializeField] private bool fogEnabled = true;

    [Tooltip("アクティブ時の霧の色")]
    [SerializeField] private Color customFogColor = Color.gray;

    [Tooltip("アクティブ時の霧のモード")]
    [SerializeField] private FogMode customFogMode = FogMode.ExponentialSquared;

    [Tooltip("アクティブ時の霧の濃度")]
    [SerializeField] private float customFogDensity = 0.01f;

    [Tooltip("アクティブ時の霧の開始距離（Linearモード時）")]
    [SerializeField] private float customFogStartDistance = 0f;

    [Tooltip("アクティブ時の霧の終了距離（Linearモード時）")]
    [SerializeField] private float customFogEndDistance = 300f;



    #endregion

    #region ランタイムフィールド

    // Start時の設定を保存（復元用）
    private Material _originalSkybox;
    private bool _originalFogEnabled;
    private Color _originalFogColor;
    private FogMode _originalFogMode;
    private float _originalFogDensity;
    private float _originalFogStartDistance;
    private float _originalFogEndDistance;

    private bool _isApplied = false;
    private bool _initialized = false;
    private bool _pendingApply = false;

    private const float DELAY_SECONDS = 1f;

    // エディタプレビュー用（ランタイムでは使用しない）
    [System.NonSerialized] private SkyboxPreview _previousPreview = SkyboxPreview.Off;
    [System.NonSerialized] private Material _editorOriginalSkybox;
    [System.NonSerialized] private bool _editorOriginalFogEnabled;
    [System.NonSerialized] private Color _editorOriginalFogColor;
    [System.NonSerialized] private FogMode _editorOriginalFogMode;
    [System.NonSerialized] private float _editorOriginalFogDensity;
    [System.NonSerialized] private float _editorOriginalFogStartDistance;
    [System.NonSerialized] private float _editorOriginalFogEndDistance;
    [System.NonSerialized] private bool _editorSettingsSaved = false;

    #endregion

    #region Unity イベント

    /// <summary>
    /// 初期化処理。実行開始直後のRenderSettings値を保存します。
    /// </summary>
    void Start()
    {
        // 実行直後のSkybox設定を即座に保存（オフ状態）
        _originalSkybox = RenderSettings.skybox;
        _originalFogEnabled = RenderSettings.fog;
        _originalFogColor = RenderSettings.fogColor;
        _originalFogMode = RenderSettings.fogMode;
        _originalFogDensity = RenderSettings.fogDensity;
        _originalFogStartDistance = RenderSettings.fogStartDistance;
        _originalFogEndDistance = RenderSettings.fogEndDistance;

        // 初期化完了フラグを遅延実行で立てる
        SendCustomEventDelayedSeconds(nameof(_InitializationComplete), DELAY_SECONDS);
    }

    #endregion

    #region 公開メソッド

    /// <summary>
    /// 初期化完了時の処理。保留中の設定適用があれば実行します。
    /// </summary>
    public void _InitializationComplete()
    {
        _initialized = true;

        // 初期化完了時、保留中の適用があれば実行
        if (_pendingApply)
        {
            _pendingApply = false;
            ApplySettings();
        }
    }

    /// <summary>
    /// カスタム設定を適用します。
    /// 初期化完了前に呼ばれた場合は保留されます。
    /// </summary>
    public void ApplySettings()
    {
        // 初期化完了まで保留
        if (!_initialized)
        {
            _pendingApply = true;
            return;
        }

        if (_isApplied) return;

        if (customSkyboxMaterial != null)
        {
            RenderSettings.skybox = customSkyboxMaterial;
        }
        RenderSettings.fog = fogEnabled;
        RenderSettings.fogColor = customFogColor;
        RenderSettings.fogMode = customFogMode;

        if (customFogMode == FogMode.Linear)
        {
            RenderSettings.fogStartDistance = customFogStartDistance;
            RenderSettings.fogEndDistance = customFogEndDistance;
        }
        else
        {
            RenderSettings.fogDensity = customFogDensity;
        }

        _isApplied = true;
    }

    /// <summary>
    /// オリジナル設定に復元します。
    /// </summary>
    public void RestoreSettings()
    {
        // 初期化完了まで何もしない
        if (!_initialized) return;
        if (!_isApplied) return;

        // 他のSkyboxSetterが既に上書き済みなら復元しない（競合回避）
        if (!IsMySettingsCurrentlyApplied())
        {
            _isApplied = false;
            return;
        }

        RenderSettings.skybox = _originalSkybox;
        RenderSettings.fog = _originalFogEnabled;
        RenderSettings.fogColor = _originalFogColor;
        RenderSettings.fogMode = _originalFogMode;
        RenderSettings.fogDensity = _originalFogDensity;
        RenderSettings.fogStartDistance = _originalFogStartDistance;
        RenderSettings.fogEndDistance = _originalFogEndDistance;
        _isApplied = false;
    }

    private bool IsMySettingsCurrentlyApplied()
    {
        if (customSkyboxMaterial != null && RenderSettings.skybox != customSkyboxMaterial)
        {
            return false;
        }

        if (RenderSettings.fog != fogEnabled) return false;
        if (RenderSettings.fogColor != customFogColor) return false;
        if (RenderSettings.fogMode != customFogMode) return false;

        if (customFogMode == FogMode.Linear)
        {
            if (RenderSettings.fogStartDistance != customFogStartDistance) return false;
            if (RenderSettings.fogEndDistance != customFogEndDistance) return false;
        }
        else
        {
            if (RenderSettings.fogDensity != customFogDensity) return false;
        }

        return true;
    }

    #endregion

    #region エディタサポート

#if UNITY_EDITOR && !COMPILER_UDONSHARP
    private void SyncFogModeFromScene()
    {
        FogMode sceneFogMode = RenderSettings.fogMode;
        if (customFogMode != sceneFogMode)
        {
            customFogMode = sceneFogMode;
        }
    }

    /// <summary>
    /// エディタでの値変更時の検証処理。プレビュー機能を提供します。
    /// </summary>
    private void OnValidate()
    {
        SyncFogModeFromScene();

        // Off→On: プレビュー開始
        if (_previousPreview == SkyboxPreview.Off && preview == SkyboxPreview.On)
        {
            // 現在の設定を保存
            _editorOriginalSkybox = RenderSettings.skybox;
            _editorOriginalFogEnabled = RenderSettings.fog;
            _editorOriginalFogColor = RenderSettings.fogColor;
            _editorOriginalFogMode = RenderSettings.fogMode;
            _editorOriginalFogDensity = RenderSettings.fogDensity;
            _editorOriginalFogStartDistance = RenderSettings.fogStartDistance;
            _editorOriginalFogEndDistance = RenderSettings.fogEndDistance;
            _editorSettingsSaved = true;

            // カスタム設定を適用
            if (customSkyboxMaterial != null)
            {
                RenderSettings.skybox = customSkyboxMaterial;
            }
            RenderSettings.fog = fogEnabled;
            RenderSettings.fogColor = customFogColor;
            RenderSettings.fogMode = customFogMode;

            if (customFogMode == FogMode.Linear)
            {
                RenderSettings.fogStartDistance = customFogStartDistance;
                RenderSettings.fogEndDistance = customFogEndDistance;
            }
            else
            {
                RenderSettings.fogDensity = customFogDensity;
            }
        }
        // On→Default: プレビュー終了
        else if (_previousPreview == SkyboxPreview.On && preview == SkyboxPreview.Off)
        {
            // 保存した設定に戻す
            if (_editorSettingsSaved)
            {
                RenderSettings.skybox = _editorOriginalSkybox;
                RenderSettings.fog = _editorOriginalFogEnabled;
                RenderSettings.fogColor = _editorOriginalFogColor;
                RenderSettings.fogMode = _editorOriginalFogMode;
                RenderSettings.fogDensity = _editorOriginalFogDensity;
                RenderSettings.fogStartDistance = _editorOriginalFogStartDistance;
                RenderSettings.fogEndDistance = _editorOriginalFogEndDistance;
                _editorSettingsSaved = false;
            }
        }
        // On→On: 設定値が変更された場合は再適用
        else if (preview == SkyboxPreview.On)
        {
            if (customSkyboxMaterial != null)
            {
                RenderSettings.skybox = customSkyboxMaterial;
            }
            RenderSettings.fog = fogEnabled;
            RenderSettings.fogColor = customFogColor;
            RenderSettings.fogMode = customFogMode;

            if (customFogMode == FogMode.Linear)
            {
                RenderSettings.fogStartDistance = customFogStartDistance;
                RenderSettings.fogEndDistance = customFogEndDistance;
            }
            else
            {
                RenderSettings.fogDensity = customFogDensity;
            }
        }

        _previousPreview = preview;
    }

    /// <summary>
    /// プレビュー状態をリセットします（プレイモード開始前に呼ばれます）。
    /// </summary>
    public void ResetPreview()
    {
        preview = SkyboxPreview.Off;
        _previousPreview = SkyboxPreview.Off;

        if (_editorSettingsSaved)
        {
            RenderSettings.skybox = _editorOriginalSkybox;
            RenderSettings.fog = _editorOriginalFogEnabled;
            RenderSettings.fogColor = _editorOriginalFogColor;
            RenderSettings.fogMode = _editorOriginalFogMode;
            RenderSettings.fogDensity = _editorOriginalFogDensity;
            RenderSettings.fogStartDistance = _editorOriginalFogStartDistance;
            RenderSettings.fogEndDistance = _editorOriginalFogEndDistance;
            _editorSettingsSaved = false;
        }

        EditorUtility.SetDirty(this);
    }
#endif

    #endregion
}
