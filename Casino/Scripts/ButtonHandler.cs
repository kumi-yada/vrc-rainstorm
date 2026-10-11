
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
    }

    public void _OnClick()
    {
        if (targetScript == null) return;
        targetScript.SetProgramVariable("selectedIndex", buttonIndex);
        targetScript.SendCustomEvent(methodName);
    }

    public void _SetSelected(bool selected)
    {
        if (_textElement == null) return;
        _textElement.color = selected ? Color.gray : Color.white;
    }
}
