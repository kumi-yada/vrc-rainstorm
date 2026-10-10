
using UdonSharp;
using UnityEngine;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class LocalizedPanel : UdonSharpBehaviour
{
    [SerializeField] private LocalizationManager localization;
    [SerializeField] private TextMeshProUGUI textElement;

    private string _key;
    private string _suffix;

    private void Start()
    {
        if (localization == null)
            localization = GameObject.Find("LocalizationManager").GetComponent<LocalizationManager>();
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

    public void _OnLanguageChanged()
    {
        if (localization == null || textElement == null) return;
        if (!string.IsNullOrEmpty(_key))
            textElement.text = localization.GetText(_key) + _suffix;
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
        var foundObject = GameObject.Find("LocalizationManager");
        if (foundObject == null || foundObject.Equals(null))
        {
            Debug.LogWarning("LocalizedPanel: LocalizationManagerがシーンに配置されていません");
        }
        else
        {
            localization = foundObject.GetComponent<LocalizationManager>();
            Debug.Log("LocalizedPanel: LocalizationManagerを自動設定しました");
        }
    }
#endif
}
