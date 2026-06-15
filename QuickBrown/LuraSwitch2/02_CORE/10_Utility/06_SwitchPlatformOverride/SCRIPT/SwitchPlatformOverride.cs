
using QuickBrown.LuraSwitch;
using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
#endif

public enum MobilePreview
{
    disabled = 0,
    enabled = 1,
}

public enum SwitchState
{
    Off = 0,
    On = 1,
    Hide = 2,
}

public class SwitchPlatformOverride : UdonSharpBehaviour
{
    [HelpBox("SwitchPlatformOverride\n\nJP:\n他プラットフォームでのスイッチやオブジェクトの状態を制御するためのコンポーネントです。\n\nEN:\nSwitchPlatformOverride is a component for controlling the state of switches and objects on different platforms.", HelpBoxAttribute.MessageType.Info)]
    [FormerlySerializedAs("questPreview")]
    [SerializeField] private MobilePreview mobilePreview = MobilePreview.disabled;
    [Header("--------------------------------------------------")]
    [HelpBox("JP:\nMobileビルド時のスイッチの状態を設定できます。\nOFF：スイッチのデフォルト状態がOFFになります。\nON：スイッチのデフォルト状態がONになります。\nHIDE：スイッチが非表示になります。\n\nEN:\nYou can set the switch state for Mobile builds.\nOFF: The default state of the switch will be OFF.\nON: The default state of the switch will be ON.\nHIDE: The switch will be hidden.")]
    [SerializeField] private SwitchBase[] switchBases;
    [SerializeField] private SwitchState[] switchOverrideStates;


    [HelpBox("JP:\nMobileビルド時に非アクティブになるオブジェクトと、アクティブになるオブジェクトを設定できます。\n\nEN:\nYou can set the objects that will be deactivated and activated for Mobile builds.")]
    [FormerlySerializedAs("questDisabledObjects")]
    [SerializeField] private GameObject[] mobileDisabledObjects;
    [FormerlySerializedAs("questActiveObjects")]
    [SerializeField] private GameObject[] mobileActiveObjects;



    private bool _applied;

    // Editorプレビュー解除時に確実に元へ戻すため、プレビュー適用前の状態を保持します。
    private bool _mobileObjectStateCached;
    private bool[] _mobileDisabledOriginalStates;
    private bool[] _mobileActiveOriginalStates;

    private void OnValidate()
    {

        int desiredLength = switchBases != null ? switchBases.Length : 0;

        if (switchOverrideStates == null || switchOverrideStates.Length != desiredLength)
        {
            var newStates = new SwitchState[desiredLength];

            if (switchOverrideStates != null)
            {
                int copyLength = Mathf.Min(switchOverrideStates.Length, desiredLength);
                for (int i = 0; i < copyLength; i++)
                {
                    newStates[i] = switchOverrideStates[i];
                }
            }

            switchOverrideStates = newStates;
        }

        // プレビュー用：エディタ上での見え方を即時反映したいので、適用済みフラグはリセットします。
        _applied = false;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // OnValidateの呼び出し順（他スクリプトのOnValidate）で上書きされることがあるため、
        // delayCallで「最後に」プレビュー反映/復帰を行います。
        EditorApplication.delayCall -= Editor_ApplyPreviewDelayed;
        EditorApplication.delayCall -= Editor_RestoreDefaultsDelayed;

        if (mobilePreview == MobilePreview.enabled)
        {
            EditorApplication.delayCall += Editor_ApplyPreviewDelayed;
        }
        else
        {
            EditorApplication.delayCall += Editor_RestoreDefaultsDelayed;
        }
#endif
    }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
    private void Editor_ApplyPreviewDelayed()
    {
        // domain reloadや破棄タイミングで呼ばれても安全に
        if (this == null)
        {
            return;
        }

        if (mobilePreview != MobilePreview.enabled)
        {
            return;
        }

        ApplyOverrides_PreviewOnly();
    }

    private void Editor_RestoreDefaultsDelayed()
    {
        if (this == null)
        {
            return;
        }

        if (mobilePreview == MobilePreview.enabled)
        {
            return;
        }

        RestoreDefaults_PreviewOnly();
    }
#endif

    public void Start()
    {
        if (!ShouldApplyOverridesInThisBuild())
        {
            return;
        }

        // SwitchBase側の初期化後に上書きしたいので、1フレーム遅延させます。
        SendCustomEventDelayedFrames(nameof(ApplyOverrides_MobileOnly), 1);
    }

    private bool ShouldApplyOverridesInThisBuild()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#elif (LURASWITCH_BUILD_FORCE_MOBILEPREVIEW || LURASWITCH_BUILD_FORCE_QUESTPREVIEW) && !UNITY_EDITOR
        return true;
#else
        return mobilePreview == MobilePreview.enabled;
#endif
    }

    public void ApplyOverrides_MobileOnly()
    {
        if (!ShouldApplyOverridesInThisBuild())
        {
            return;
        }

        if (_applied)
        {
            return;
        }
        _applied = true;

        ApplyOverridesInternal();
    }

    // エディタ上の見た目確認用（適用済みフラグを使わず、何度でも反映できる）
    public void ApplyOverrides_PreviewOnly()
    {
        ApplyOverridesInternal();
    }

    public void RestoreDefaults_PreviewOnly()
    {
#if !UNITY_EDITOR || COMPILER_UDONSHARP
        return;
#else
    RestoreMobileObjectStates_PreviewOnly();

        if (switchBases == null || switchBases.Length == 0)
        {
            return;
        }

        for (int i = 0; i < switchBases.Length; i++)
        {
            var switchBase = switchBases[i];
            if (switchBase == null)
            {
                continue;
            }

            switchBase.SetHidden(false);
            switchBase.Editor_ApplyDefaultVisual();
        }
#endif
    }

    private void ApplyOverridesInternal()
    {

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        CacheMobileObjectStatesIfNeeded_PreviewOnly();
#endif

        ApplyMobileObjectOverrides();

        if (switchBases == null || switchBases.Length == 0)
        {
            return;
        }

        for (int i = 0; i < switchBases.Length; i++)
        {
            var switchBase = switchBases[i];
            if (switchBase == null)
            {
                continue;
            }

            SwitchState state = SwitchState.Off;
            if (switchOverrideStates != null && i < switchOverrideStates.Length)
            {
                state = switchOverrideStates[i];
            }

            if (state == SwitchState.Hide)
            {
                // Hide はスイッチ自体を無効化せず、メッシュ部分だけ非表示にします。
                switchBase.SetHidden(true);
                continue;
            }

            // Hide解除
            switchBase.SetHidden(false);

            if (switchBase.Mode != SwitchMode.Toggle)
            {
                continue;
            }

            bool on = state == SwitchState.On;
            // Mobileビルド時のみのローカル上書き用途のため、ネットワーク同期は行いません。
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
            {
                switchBase.Editor_ApplyToggleVisual(on);
            }
            else
            {
                switchBase.ApplyToggleStateFromExternal(on, syncNetwork: false);
            }
#else
            switchBase.ApplyToggleStateFromExternal(on, syncNetwork: false);
#endif
        }
    }

    private void ApplyMobileObjectOverrides()
    {
        // Mobileモードのとき:
        // - mobileDisabledObjects は非アクティブ
        // - mobileActiveObjects はアクティブ
        SetActiveSafe(mobileDisabledObjects, false);
        SetActiveSafe(mobileActiveObjects, true);
    }

    private void SetActiveSafe(GameObject[] targets, bool active)
    {
        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            var go = targets[i];
            if (go == null)
            {
                continue;
            }

            go.SetActive(active);
        }
    }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
    private void CacheMobileObjectStatesIfNeeded_PreviewOnly()
    {
        // 実行時は不要（プレビュー復帰用のキャッシュ）
        if (Application.isPlaying)
        {
            return;
        }

        int disabledLen = mobileDisabledObjects != null ? mobileDisabledObjects.Length : 0;
        int activeLen = mobileActiveObjects != null ? mobileActiveObjects.Length : 0;

        bool needsRebuild = !_mobileObjectStateCached
                            || _mobileDisabledOriginalStates == null
                            || _mobileDisabledOriginalStates.Length != disabledLen
                            || _mobileActiveOriginalStates == null
                            || _mobileActiveOriginalStates.Length != activeLen;

        if (!needsRebuild)
        {
            return;
        }

        _mobileDisabledOriginalStates = new bool[disabledLen];
        _mobileActiveOriginalStates = new bool[activeLen];

        for (int i = 0; i < disabledLen; i++)
        {
            var go = mobileDisabledObjects[i];
            _mobileDisabledOriginalStates[i] = go != null && go.activeSelf;
        }

        for (int i = 0; i < activeLen; i++)
        {
            var go = mobileActiveObjects[i];
            _mobileActiveOriginalStates[i] = go != null && go.activeSelf;
        }

        _mobileObjectStateCached = true;
    }

    private void RestoreMobileObjectStates_PreviewOnly()
    {
        if (Application.isPlaying)
        {
            return;
        }

        // キャッシュが無い場合は「逆」をデフォルトとして戻します。
        // (mobileDisabledObjects: 有効 / mobileActiveObjects: 無効)
        if (!_mobileObjectStateCached)
        {
            SetActiveSafe(mobileDisabledObjects, true);
            SetActiveSafe(mobileActiveObjects, false);
            return;
        }

        int disabledLen = mobileDisabledObjects != null ? mobileDisabledObjects.Length : 0;
        int activeLen = mobileActiveObjects != null ? mobileActiveObjects.Length : 0;

        for (int i = 0; i < disabledLen; i++)
        {
            var go = mobileDisabledObjects[i];
            if (go == null)
            {
                continue;
            }

            bool active = _mobileDisabledOriginalStates != null && i < _mobileDisabledOriginalStates.Length
                ? _mobileDisabledOriginalStates[i]
                : true;
            go.SetActive(active);
        }

        for (int i = 0; i < activeLen; i++)
        {
            var go = mobileActiveObjects[i];
            if (go == null)
            {
                continue;
            }

            bool active = _mobileActiveOriginalStates != null && i < _mobileActiveOriginalStates.Length
                ? _mobileActiveOriginalStates[i]
                : false;
            go.SetActive(active);
        }

        _mobileObjectStateCached = false;
    }
#endif
}
