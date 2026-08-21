using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class InputFieldUpdater : UdonSharpBehaviour
{
    [Header("対象のInputField")]
    [Tooltip("更新するInputFieldコンポーネントをアサインしてください")]
    public InputField inputField;

    [Header("更新後のテキスト")]
    [Tooltip("InputFieldに設定するテキスト")]
    public string newText = "初期テキスト";

    /// <summary>
    /// Start時にInputFieldのテキストを更新
    /// </summary>
    void Start()
    {
        UpdateInputField();
    }

    /// <summary>
    /// 外部からも呼び出してInputFieldの中身を更新するための関数
    /// </summary>
    public void UpdateInputField()
    {
        if (inputField != null)
        {
            inputField.text = newText;
        }
        else
        {
            Debug.LogWarning("InputFieldが未アサインです。");
        }
    }
}
