using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Loads the changelog from a GitHub repository raw URL once at init and
/// writes it into a text field.
/// Expected URL points at the raw CHANGELOG.md, e.g.
/// https://raw.githubusercontent.com/kumi-yada/vrc-rainstorm/master/CHANGELOG.md
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class ChangelogView : UdonSharpBehaviour
{
    [Header("Changelog Source")]
    [Tooltip("Raw changelog URL. Open the file on GitHub, press Raw, copy the URL, e.g. https://raw.githubusercontent.com/<user>/<repo>/<branch>/CHANGELOG.md. Must be a VRCUrl because Udon blocks building URLs from runtime strings.")]
    public VRCUrl changelogUrl;

    [Header("Display")]
    [Tooltip("Text field the changelog content is written into")]
    public TextMeshProUGUI changelogText;

    [Tooltip("If set, this text shows the localized changelog title")]
    public LocalizedText titleText;

    [Tooltip("Localization key for the changelog title")]
    public string titleMessage = "CHANGELOG";

    private const int MaxRetries = 3;
    private const float RetryDelaySeconds = 6f; // string downloads are limited to one per 5s

    private int _retryCount;
    private bool _loaded;

    public bool IsLoaded()
    {
        return _loaded;
    }

    public void _Reload()
    {
        if (changelogUrl == null)
        {
            Debug.LogWarning("[Changelog] changelogUrl not set");
            return;
        }
        VRCStringDownloader.LoadUrl(changelogUrl, (IUdonEventReceiver)this);
    }

    private void Start()
    {
        if (titleText != null)
        {
            titleText.SetPrefixKeyAndSuffix("", titleMessage, "");
        }
        _Reload();
    }

    public void _RetryLoad()
    {
        VRCStringDownloader.LoadUrl(changelogUrl, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _retryCount = 0;
        _loaded = true;
        if (changelogText != null)
        {
            changelogText.text = result.Result;
        }
        Debug.Log("[Changelog] Loaded changelog");
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        if (_retryCount < MaxRetries)
        {
            _retryCount++;
            Debug.LogWarning($"[Changelog] Load error {result.ErrorCode} ({result.Error}), retry {_retryCount}/{MaxRetries}");
            SendCustomEventDelayedSeconds(nameof(_RetryLoad), RetryDelaySeconds);
        }
        else
        {
            Debug.LogError($"[Changelog] Load failed after {MaxRetries} retries: {result.ErrorCode} {result.Error}");
        }
    }
}
