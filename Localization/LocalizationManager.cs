
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class LocalizationManager : UdonSharpBehaviour
{
    [Header("Language tables ([0] = fallback for unknown client languages and missing keys)")]
    [SerializeField] private LanguageTable[] tables;

    [Header("Keys (indices must match each table's translations)")]
    [SerializeField] private string[] keys;

    [Header("Registered UI scripts (auto-filled at runtime)")]
    [SerializeField] private UdonSharpBehaviour[] listeners = new UdonSharpBehaviour[32];
    private int _listenerCount;

    private int _language;

    public int LanguageCount => tables.Length;
    public int CurrentLanguage => _language;
    public string CurrentLanguageName => tables[_language].DisplayName;

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

    public void Register(UdonSharpBehaviour listener)
    {
        if (_listenerCount >= listeners.Length) return;
        for (int i = 0; i < _listenerCount; i++)
        {
            if (listeners[i] == listener) return;
        }
        listeners[_listenerCount++] = listener;
    }

    private void RefreshAll()
    {
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
