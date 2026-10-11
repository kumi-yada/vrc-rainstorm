
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class Languages : UdonSharpBehaviour
{
    [Tooltip("LocalizationManager that drives the language tables.")]
    [SerializeField] private LocalizationManager localization;

    [Tooltip("Language buttons. Array index must match the localization table index.")]
    [SerializeField] private ButtonHandler[] buttons;

    [HideInInspector] public int selectedIndex = -1;

    private void Start()
    {
        if (localization == null)
        {
            GameObject locGo = GameObject.Find("Localization");
            Debug.Log($"[Languages] Start: 'Localization' GameObject {(locGo != null ? "found" : "NOT FOUND")}");
            if (locGo != null)
                localization = locGo.GetComponent<LocalizationManager>();
        }

        if (localization == null)
        {
            Debug.LogError("[Languages] Start: localization is NULL — aborting setup");
            return;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null)
            {
                Debug.LogWarning($"[Languages] Start: buttons[{i}] is NULL");
                continue;
            }

            buttons[i].buttonIndex = i;
            buttons[i].targetScript = this;
            buttons[i].methodName = nameof(_OnLanguageButtonClicked);
            Debug.Log($"[Languages] Start: wired buttons[{i}] -> '{buttons[i].gameObject.name}'");
        }

        localization.Register(this);
        RefreshSelected();
    }

    public void _OnLanguageButtonClicked()
    {
        Debug.Log($"[Languages] _OnLanguageButtonClicked entered, localization={(localization != null ? localization.name : "NULL")}, selectedIndex={selectedIndex}");

        if (localization == null)
        {
            Debug.LogError("[Languages] _OnLanguageButtonClicked: localization is NULL — aborting");
            return;
        }

        int index = selectedIndex;
        selectedIndex = -1;

        if (index < 0)
        {
            Debug.LogWarning($"[Languages] _OnLanguageButtonClicked: index={index} is invalid — aborting");
            return;
        }

        Debug.Log($"[Languages] _OnLanguageButtonClicked: calling SetLanguage({index})");
        localization.SetLanguage(index);
    }

    public void SetEnglish()
    {
        if (localization == null) return;
        localization.SetLanguageByCode("en");
    }

    public void SetJapanese()
    {
        if (localization == null) return;
        localization.SetLanguageByCode("ja");
    }

    public void _OnLanguageChanged()
    {
        RefreshSelected();
    }

    private void RefreshSelected()
    {
        if (localization == null) return;

        int current = localization.CurrentLanguage;
        Debug.Log($"[Languages] RefreshSelected: current={current}");

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            Debug.Log($"[Languages] RefreshSelected: buttons[{i}] '{buttons[i].gameObject.name}' buttonIndex={buttons[i].buttonIndex} selected={(buttons[i].buttonIndex == current)}");
            buttons[i]._SetSelected(buttons[i].buttonIndex == current);
        }
    }
}
