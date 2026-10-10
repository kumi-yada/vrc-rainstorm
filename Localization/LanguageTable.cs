
using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class LanguageTable : UdonSharpBehaviour
{
    [Tooltip("RFC 5646 code matched against the client language, e.g. en, ja, zh-CN")]
    [SerializeField] private string languageCode = "en";
    [SerializeField] private string displayName = "English";

    [Header("Translations (indices must match LocalizationManager keys)")]
    [SerializeField] private string[] texts;

    public string LanguageCode => languageCode;
    public string DisplayName => displayName;

    public string GetTextAt(int index)
    {
        if (texts == null || index < 0 || index >= texts.Length) return null;
        return texts[index];
    }
}
