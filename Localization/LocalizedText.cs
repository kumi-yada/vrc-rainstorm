
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

    public TextMeshProUGUI TextElement => textElement;

    private void Start()
    {
        if (localization == null)
            localization = GameObject.Find("Localization").GetComponent<LocalizationManager>();
        if (textElement == null)
            textElement = GetComponent<TextMeshProUGUI>();
        if (localization == null) return;
        if (textElement != null)
            _key = textElement.text;
        localization.Register(this);
        _OnLanguageChanged();
    }

    public void SetKey(string key)
    {
        _key = key;
        _OnLanguageChanged();
    }

    public void SetSuffix(string suffix)
    {
        _suffix = suffix;
        _OnLanguageChanged();
    }

    public void SetKeyAndSuffix(string key, string suffix)
    {
        _key = key;
        _suffix = suffix;
        _OnLanguageChanged();
    }

    public void SetPrefixKeyAndSuffix(string prefix, string key, string suffix)
    {
        _prefix = prefix;
        _key = key;
        _suffix = suffix;
        _OnLanguageChanged();
    }

    public void _OnLanguageChanged()
    {
        if (localization == null || textElement == null) return;
        textElement.text = _prefix + localization.GetText(_key) + _suffix;
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
