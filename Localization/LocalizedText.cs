
using UdonSharp;
using UnityEngine;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class LocalizedText : UdonSharpBehaviour
{
    [SerializeField] private LocalizationManager localization;
    [SerializeField] private TextMeshProUGUI textElement;

    private string _prefix = "";
    private string _key;
    private string _suffix = "";
    private string _suffixKey = "";

    public TextMeshProUGUI TextElement => textElement;

    private void Start()
    {
        if (localization == null)
            localization = GameObject.Find("Localization").GetComponent<LocalizationManager>();
        if (textElement == null)
            textElement = GetComponent<TextMeshProUGUI>();
        if (localization == null) return;
        // Only fall back to the authored scene text when no key was already set
        // programmatically. Solitaire (and other scripts) call SetPrefixKeyAndSuffix
        // from their own Start, which may run before this Start: capturing the raw
        // text here would clobber that key with the prefab's placeholder (e.g. the
        // win banner's "You win!"), leaving the label untranslated.
        if (textElement != null && string.IsNullOrEmpty(_key))
            _key = textElement.text;
        localization.Register(this);
        _OnLanguageChanged();
    }

    public void SetKey(string key)
    {
        _key = key;
        _suffixKey = "";
        _OnLanguageChanged();
    }

    public void SetSuffix(string suffix)
    {
        _suffix = suffix;
        _suffixKey = "";
        _OnLanguageChanged();
    }

    public void SetKeyAndSuffix(string key, string suffix)
    {
        _key = key;
        _suffix = suffix;
        _suffixKey = "";
        _OnLanguageChanged();
    }

    public void SetPrefixKeyAndSuffix(string prefix, string key, string suffix)
    {
        _prefix = prefix;
        _key = key;
        _suffix = suffix;
        _suffixKey = "";
        _OnLanguageChanged();
    }

    public void SetPrefixKeyAndSuffixKey(string prefix, string key, string suffixKey)
    {
        _prefix = prefix;
        _key = key;
        _suffix = "";
        _suffixKey = suffixKey;
        _OnLanguageChanged();
    }

    public void _OnLanguageChanged()
    {
        if (localization == null || textElement == null) return;
        string suffix = string.IsNullOrEmpty(_suffixKey) ? _suffix : localization.GetText(_suffixKey);
        textElement.text = _prefix + localization.GetText(_key) + suffix;
        Debug.Log($"[LocalizedText] _OnLanguageChanged '{gameObject.name}' key='{_key}' -> '{textElement.text}'");
    }

#if !COMPILER_UDONSHARP && UNITY_EDITOR

    public void Reset()
    {
        LocalizationManagerAutoSet();
    }

    public void OnValidate()
    {
        LocalizationManagerAutoSet();
    }

    public void LocalizationManagerAutoSet()
    {
        if (textElement == null)
            textElement = GetComponent<TextMeshProUGUI>();
        var foundObject = GameObject.Find("Localization");
        if (foundObject == null || foundObject.Equals(null))
        {
            Debug.LogWarning("LocalizedText: LocalizationManagerがシーンに配置されていません");
        }
        else
        {
            localization = foundObject.GetComponent<LocalizationManager>();
            Debug.Log("LocalizedText: LocalizationManagerを自動設定しました");
        }
    }
#endif
}
