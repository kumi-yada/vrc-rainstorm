
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
            localization = GameObject.Find("Localization").GetComponent<LocalizationManager>();
        if (localization == null) return;

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;

            buttons[i].buttonIndex = i;
            buttons[i].targetScript = this;
            buttons[i].methodName = nameof(_OnLanguageButtonClicked);
        }

        localization.Register(this);
        RefreshSelected();
    }

    public void _OnLanguageButtonClicked()
    {
        if (localization == null) return;

        int index = selectedIndex;
        selectedIndex = -1;

        if (index < 0) return;
        localization.SetLanguage(index);
    }

    public void _OnLanguageChanged()
    {
        RefreshSelected();
    }

    private void RefreshSelected()
    {
        if (localization == null) return;

        int current = localization.CurrentLanguage;

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            buttons[i]._SetSelected(buttons[i].buttonIndex == current);
        }
    }
}
