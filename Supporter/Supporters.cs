using System.Text;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Loads the supporter list from a GitHub gist once at init.
/// Expected gist content, one display name per line:
/// ChouKuma
/// Mikan
/// Udon cannot access account IDs at runtime, so supporter checks match by
/// display name.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class Supporters : UdonSharpBehaviour
{
    [Header("Gist Source")]
    [Tooltip("Raw gist URL. Open the gist, press Raw, copy the URL, e.g. https://gist.githubusercontent.com/<user>/<gistId>/raw. Must be a VRCUrl because Udon blocks building URLs from runtime strings.")]
    public VRCUrl gistUrl;

    [Header("Optional Display")]
    [Tooltip("If set, all supporter display names are written here, one per line")]
    public TextMeshProUGUI listText;

    [Tooltip("Objects deactivated when the supporter list is empty, activated otherwise")]
    public GameObject[] supporterObjects;

    [Tooltip("If set, this text is written when the local player is a supporter")]
    public TextMeshProUGUI supporterText;

    [Tooltip("Text shown when the local player is a supporter")]
    public string supporterTextMessage = "Thank you for your support";

    [Header("Auto Scroll")]
    [Tooltip("ScrollRect to auto-scroll vertically. Leave empty to disable.")]
    public ScrollRect autoScrollRect;

    [Tooltip("Scroll speed in normalized units per second (fraction of content height).")]
    public float autoScrollSpeed = 0.1f;

    private const int MaxRetries = 3;
    private const float RetryDelaySeconds = 6f; // string downloads are limited to one per 5s

    private string[] _names = new string[0];
    private int _count;
    private int _retryCount;
    private bool _loaded;
    private int _scrollDirection = -1;

    public bool IsLoaded()
    {
        return _loaded;
    }

    public int GetSupporterCount()
    {
        return _count;
    }

    public bool IsSupporter(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return false;
        return _FindNameIndex(player.displayName) >= 0;
    }

    public bool IsSupporterDisplayName(string displayName)
    {
        return _FindNameIndex(displayName) >= 0;
    }

    public string GetSupporterName(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return null;
        int index = _FindNameIndex(player.displayName);
        if (index < 0) return null;
        return _names[index];
    }

    public string[] GetSupporterNames()
    {
        string[] names = new string[_count];
        for (int i = 0; i < _count; i++)
        {
            names[i] = _names[i];
        }
        return names;
    }

    public void _Reload()
    {
        if (gistUrl == null)
        {
            Debug.LogWarning("[Supporters] gistUrl not set");
            return;
        }
        VRCStringDownloader.LoadUrl(gistUrl, (IUdonEventReceiver)this);
    }

    private void Start()
    {
        _Reload();
    }

    private void Update()
    {
        if (autoScrollRect == null) return;
        if (autoScrollSpeed <= 0f) return;
        if (!_HasScrollableContent()) return;

        Vector2 pos = autoScrollRect.normalizedPosition;
        pos.y += _scrollDirection * autoScrollSpeed * Time.deltaTime;

        if (pos.y >= 1f)
        {
            pos.y = 1f;
            _scrollDirection = -1;
        }
        else if (pos.y <= 0f)
        {
            pos.y = 0f;
            _scrollDirection = 1;
        }

        autoScrollRect.normalizedPosition = pos;
    }

    private bool _HasScrollableContent()
    {
        RectTransform content = autoScrollRect.content;
        if (content == null) return false;

        RectTransform viewport = autoScrollRect.viewport;
        if (viewport == null) viewport = (RectTransform)autoScrollRect.transform;

        return content.rect.height > viewport.rect.height + 0.01f;
    }

    public void _RetryLoad()
    {
        VRCStringDownloader.LoadUrl(gistUrl, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _retryCount = 0;
        _ParseSupporters(result.Result);
        _loaded = true;
        _UpdateListText();
        _UpdateSupporterObjects();
        _UpdateSupporterText();
        Debug.Log($"[Supporters] Loaded {_count} supporters");
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        if (_retryCount < MaxRetries)
        {
            _retryCount++;
            Debug.LogWarning($"[Supporters] Load error {result.ErrorCode} ({result.Error}), retry {_retryCount}/{MaxRetries}");
            SendCustomEventDelayedSeconds(nameof(_RetryLoad), RetryDelaySeconds);
        }
        else
        {
            Debug.LogError($"[Supporters] Load failed after {MaxRetries} retries: {result.ErrorCode} {result.Error}");
        }
    }

    private void _ParseSupporters(string content)
    {
        if (content == null) content = "";
        string[] lines = content.Replace("\r\n", "\n").Replace("\t", " ").Split('\n');
        _names = new string[lines.Length];
        _count = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string name = lines[i].Trim();
            if (name == "") continue;
            _names[_count] = name;
            _count++;
        }
    }

    private int _FindNameIndex(string displayName)
    {
        if (displayName == null) return -1;
        for (int i = 0; i < _count; i++)
        {
            if (_names[i] == displayName) return i;
        }
        return -1;
    }

    private void _UpdateListText()
    {
        if (listText == null) return;
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < _count; i++)
        {
            builder.Append(_names[i]);
            if (i < _count - 1) builder.Append("\n");
        }
        listText.text = builder.ToString();
    }

    private void _UpdateSupporterObjects()
    {
        if (supporterObjects == null) return;
        bool hasSupporters = _count > 0;
        for (int i = 0; i < supporterObjects.Length; i++)
        {
            if (supporterObjects[i] == null) continue;
            supporterObjects[i].SetActive(hasSupporters);
        }
    }

    private void _UpdateSupporterText()
    {
        if (supporterText == null) return;
        supporterText.text = IsSupporter(Networking.LocalPlayer) ? supporterTextMessage : "";
    }
}
