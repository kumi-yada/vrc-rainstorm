
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class LocalizationManager : UdonSharpBehaviour
{
    private const string LANGUAGE_KEY = "language";
    private bool _dataReady;
    [Header("Language tables ([0] = fallback for unknown client languages and missing keys)")]
    [SerializeField] private LanguageTable[] tables;

    [Header("Keys (indices must match each table's translations)")]
    [SerializeField] private string[] keys;

    [Header("Registered UI scripts (auto-filled at runtime)")]
    private UdonSharpBehaviour[] listeners = new UdonSharpBehaviour[512];
    private int _listenerCount;

    private int _language;

    public int LanguageCount => tables.Length;
    public int CurrentLanguage => _language;
    public string CurrentLanguageName => tables[_language].DisplayName;
    public string CurrentLanguageCode => tables[_language].LanguageCode;

    private void Start()
    {
        _language = FindTableIndex(VRCPlayerApi.GetCurrentLanguage());
        RefreshAll();
    }

    public override void OnLanguageChanged(string language)
    {
        _language = FindTableIndex(language);
        RefreshAll();
    }

    public void SetLanguage(int index)
    {
        if (tables == null || index < 0 || index >= tables.Length) return;
        _language = index;
        RefreshAll();
        SaveLanguage();
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        _dataReady = true;

        if (PlayerData.TryGetString(player, LANGUAGE_KEY, out string saved))
        {
            int idx = FindTableIndex(saved);
            if (idx != _language)
            {
                _language = idx;
                RefreshAll();
            }
        }
    }

    private void SaveLanguage()
    {
        if (!_dataReady || tables == null || _language < 0 || _language >= tables.Length || tables[_language] == null) return;
        PlayerData.SetString(LANGUAGE_KEY, tables[_language].LanguageCode);
    }

    public void SetLanguageByCode(string code)
    {
        SetLanguage(FindTableIndex(code));
    }

    public void _CycleLanguage()
    {
        if (tables == null || tables.Length == 0) return;
        SetLanguage((_language + 1) % tables.Length);
    }

    public string GetText(string key)
    {
        if (tables == null || tables.Length == 0 || keys == null || string.IsNullOrEmpty(key)) return key;
        int index = FindKeyIndex(key);
        if (index < 0) return key;
        string s = tables[_language].GetTextAt(index);
        if (string.IsNullOrEmpty(s) && _language != 0) s = tables[0].GetTextAt(index);
        return string.IsNullOrEmpty(s) ? key : s;
    }

    public string[] GetAllLanguageCodes()
    {
        if (tables == null) return new string[0];
        string[] codes = new string[tables.Length];
        for (int i = 0; i < tables.Length; i++)
        {
            codes[i] = tables[i] != null ? tables[i].LanguageCode : "";
        }
        return codes;
    }

    public void Register(UdonSharpBehaviour listener)
    {
        if (listener == null) return;

        for (int i = 0; i < _listenerCount; i++)
        {
            if (listeners[i] == listener) return;
        }

        if (_listenerCount >= listeners.Length)
        {
            Debug.LogWarning($"[LocalizationManager] Register: listener list FULL ({_listenerCount}) — dropped '{listener.name}'");
            return;
        }

        listeners[_listenerCount++] = listener;
        Debug.Log($"[LocalizationManager] Register: '{listener.name}' -> slot {_listenerCount - 1} (total {_listenerCount})");
    }

    private void RefreshAll()
    {
        Debug.Log($"[LocalizationManager] RefreshAll: notifying {_listenerCount} listeners");
        for (int i = 0; i < _listenerCount; i++)
        {
            if (listeners[i] != null)
                listeners[i].SendCustomEvent("_OnLanguageChanged");
        }
    }

    private int FindKeyIndex(string key)
    {
        if (keys == null) return -1;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == key) return i;
        }
        return -1;
    }

    private int FindTableIndex(string lang)
    {
        if (tables == null || tables.Length == 0 || string.IsNullOrEmpty(lang)) return 0;
        for (int i = 0; i < tables.Length; i++)
        {
            if (tables[i] == null) continue;
            string code = tables[i].LanguageCode;
            if (lang == code || lang.StartsWith(code + "-")) return i;
        }
        return 0;
    }
}
