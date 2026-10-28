using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Loads the changelog from a GitHub repository raw URL and writes it into a
/// text field. Supports localized changelogs (e.g. CHANGELOG-ja.md) by mapping
/// language codes to URLs. Falls back to the default changelogUrl when no
/// translation exists for the current language or when the localized file
/// fails to load.
///
/// In the Editor, language entries are auto-generated from the LocalizationManager
/// tables: set changelogUrl to the default file (e.g. .../CHANGELOG.md) and the
/// entries for other languages (e.g. .../CHANGELOG-ja.md) are populated automatically.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class ChangelogView : UdonSharpBehaviour
{
    [Header("Changelog Source")]
    [Tooltip("Default (fallback) changelog URL, e.g. https://raw.githubusercontent.com/<user>/<repo>/<branch>/CHANGELOG.md. Must be a VRCUrl because Udon blocks building URLs from runtime strings.")]
    public VRCUrl changelogUrl;

    [Tooltip("Language codes for localized changelogs. Auto-generated in the Editor from the LocalizationManager tables. Index-matched with languageUrls.")]
    public string[] languageCodes;

    [Tooltip("Localized changelog URLs. Auto-generated in the Editor from the LocalizationManager tables. Index-matched with languageCodes. The URL whose language code matches the current language is loaded; if none matches, changelogUrl is used.")]
    public VRCUrl[] languageUrls;

    [Header("Localization")]
    [Tooltip("LocalizationManager that drives language selection. Auto-wired in the Editor; also found automatically at runtime via GameObject.Find(\"Localization\").")]
    public LocalizationManager localization;

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
    private bool _triedFallback;
    private VRCUrl _currentUrl;

    public bool IsLoaded()
    {
        return _loaded;
    }

    public void _Reload()
    {
        VRCUrl url = GetUrlForCurrentLanguage();
        if (url == null)
        {
            Debug.LogWarning("[Changelog] No changelog URL available");
            return;
        }
        _currentUrl = url;
        _triedFallback = (url == changelogUrl);
        _loaded = false;
        _retryCount = 0;
        VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
    }

    private VRCUrl GetUrlForCurrentLanguage()
    {
        if (localization == null) return changelogUrl;

        string code = localization.CurrentLanguageCode;
        if (languageCodes != null && languageUrls != null)
        {
            int count = languageCodes.Length;
            if (languageUrls.Length < count) count = languageUrls.Length;
            for (int i = 0; i < count; i++)
            {
                if (languageCodes[i] == code && languageUrls[i] != null)
                {
                    return languageUrls[i];
                }
            }
        }
        return changelogUrl;
    }

    private void Start()
    {
        if (localization == null)
        {
            GameObject locGo = GameObject.Find("Localization");
            if (locGo != null)
                localization = locGo.GetComponent<LocalizationManager>();
        }

        if (localization != null)
            localization.Register(this);

        if (titleText != null)
        {
            titleText.SetPrefixKeyAndSuffix("", titleMessage, "");
        }
        _Reload();
    }

    public void _OnLanguageChanged()
    {
        VRCUrl newUrl = GetUrlForCurrentLanguage();
        if (newUrl != _currentUrl)
        {
            _Reload();
        }
    }

    public void _RetryLoad()
    {
        if (_currentUrl == null) return;
        VRCStringDownloader.LoadUrl(_currentUrl, (IUdonEventReceiver)this);
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
        // If the localized changelog failed and we haven't tried the default yet, fall back
        if (!_triedFallback && changelogUrl != null && _currentUrl != changelogUrl)
        {
            _triedFallback = true;
            _retryCount = 0;
            _currentUrl = changelogUrl;
            Debug.LogWarning($"[Changelog] Localized changelog failed ({result.ErrorCode}), falling back to default");
            VRCStringDownloader.LoadUrl(changelogUrl, (IUdonEventReceiver)this);
            return;
        }

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

#if !COMPILER_UDONSHARP && UNITY_EDITOR

    private void OnValidate()
    {
        AutoWireLocalization();
        // Only auto-generate when entries are empty (first-time setup or after manual clear).
        // Use the context menu to regenerate after changing changelogUrl or language tables.
        if (languageCodes == null || languageCodes.Length == 0)
        {
            AutoGenerateLanguageEntries();
        }
    }

    [ContextMenu("Auto-generate Language Entries")]
    public void AutoGenerateLanguageEntries()
    {
        if (localization == null) AutoWireLocalization();
        if (localization == null) return;
        if (changelogUrl == null || string.IsNullOrEmpty(changelogUrl.Get())) return;

        string[] codes = localization.GetAllLanguageCodes();
        if (codes == null || codes.Length <= 1) return;

        // Skip index 0 (the fallback/default language) — changelogUrl already covers it
        var codeList = new System.Collections.Generic.List<string>();
        var urlList = new System.Collections.Generic.List<VRCUrl>();
        for (int i = 1; i < codes.Length; i++)
        {
            if (string.IsNullOrEmpty(codes[i])) continue;
            string urlString = BuildLocalizedUrl(changelogUrl.Get(), codes[i]);
            if (string.IsNullOrEmpty(urlString)) continue;

            codeList.Add(codes[i]);
            urlList.Add(new VRCUrl(urlString));
        }

        languageCodes = codeList.ToArray();
        languageUrls = urlList.ToArray();
    }

    private void AutoWireLocalization()
    {
        if (localization != null) return;
        GameObject locGo = GameObject.Find("Localization");
        if (locGo != null)
            localization = locGo.GetComponent<LocalizationManager>();
    }

    /// <summary>
    /// Builds a localized changelog URL from the default URL and a language code.
    /// Pattern: .../CHANGELOG.md -> .../CHANGELOG-{code}.md
    ///          .../CHANGELOG-en.md -> .../CHANGELOG-{code}.md
    /// </summary>
    private static string BuildLocalizedUrl(string baseUrl, string languageCode)
    {
        int lastSlash = baseUrl.LastIndexOf('/');
        if (lastSlash < 0) return null;

        string directory = baseUrl.Substring(0, lastSlash + 1);
        string filename = baseUrl.Substring(lastSlash + 1);

        if (!filename.EndsWith(".md")) return null;
        string nameWithoutExt = filename.Substring(0, filename.Length - 3);

        // Strip an existing language code suffix (e.g. "CHANGELOG-en" -> "CHANGELOG")
        string baseName = nameWithoutExt;
        int lastDash = nameWithoutExt.LastIndexOf('-');
        if (lastDash > 0)
        {
            string suffix = nameWithoutExt.Substring(lastDash + 1);
            // Language codes are short (2-5 chars, letters and optional dash)
            if (suffix.Length >= 2 && suffix.Length <= 5 && IsAlphaDash(suffix))
            {
                baseName = nameWithoutExt.Substring(0, lastDash);
            }
        }

        return directory + baseName + "-" + languageCode + ".md";
    }

    private static bool IsAlphaDash(string s)
    {
        foreach (char c in s)
        {
            if (!char.IsLetter(c) && c != '-') return false;
        }
        return true;
    }

#endif
}
