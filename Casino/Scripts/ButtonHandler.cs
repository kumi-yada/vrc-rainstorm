
using UdonSharp;
using UnityEngine;
using TMPro;

public class ButtonHandler : UdonSharpBehaviour
{
    public int buttonIndex;
    public UdonSharpBehaviour targetScript;
    public string methodName;

    private TextMeshProUGUI _textElement;

    private void Start()
    {
        _textElement = GetComponentInChildren<TextMeshProUGUI>();
        Debug.Log($"[ButtonHandler] Start on '{gameObject.name}' buttonIndex={buttonIndex}, textElement={(_textElement != null ? "found" : "MISSING")}, targetScript={(targetScript != null ? targetScript.name : "NULL")}, methodName='{methodName}'");
    }

    public void _OnClick()
    {
        Debug.Log($"[ButtonHandler] _OnClick on '{gameObject.name}' buttonIndex={buttonIndex}, targetScript={(targetScript != null ? targetScript.name : "NULL")}, methodName='{methodName}'");

        if (targetScript == null)
        {
            Debug.LogError($"[ButtonHandler] _OnClick '{gameObject.name}' targetScript is NULL — aborting");
            return;
        }

        targetScript.SetProgramVariable("selectedIndex", buttonIndex);
        targetScript.SendCustomEvent(methodName);
    }

    public void _SetSelected(bool selected)
    {
        if (_textElement == null)
            _textElement = GetComponentInChildren<TextMeshProUGUI>();
        if (_textElement == null)
        {
            Debug.LogWarning($"[ButtonHandler] _SetSelected '{gameObject.name}' textElement is NULL");
            return;
        }
        _textElement.color = selected ? Color.gray : Color.white;
    }
}
