using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace RBS.SleepKit2.Script
{
    public enum ActionType
    {
        Call,
        Toggle,
    }

    public class MenuItemAction : UdonSharpBehaviour
    {
        [Tooltip("このメニュー項目が対象とするオブジェクト")]
        public GameObject targetObject;

        [Tooltip("実行するアクション（Call：対象を移動、Toggle：On/Off 切替）")]
        public ActionType actionType = ActionType.Call;

        [Tooltip(
            "Call アクションの場合に利用する共通の移動先 Transform（MenuGenerator で設定されます）"
        )]
        public Transform callDestination;

        [Tooltip("ON/OFFの状態を表現するテキスト（TMP_Text）")]
        private TMP_Text statusText;

        private void Start()
        {
            if (actionType == ActionType.Toggle)
            {
                statusText = GetComponentInChildren<TMP_Text>();
                UpdateTextAlpha(targetObject.activeSelf);
            }
        }

        /// <summary>
        /// ボタン押下時等に呼び出してください。
        /// </summary>
        public void OnMenuPressed()
        {
            if (targetObject == null)
            {
                Debug.LogWarning("MenuItemAction: 対象オブジェクトが未設定です。");
                return;
            }

            switch (actionType)
            {
                case ActionType.Call:
                    if (callDestination != null)
                    {
                        targetObject.transform.position = callDestination.position;
                    }
                    else
                    {
                        Debug.LogWarning(
                            "MenuItemAction: Call アクションですが、移動先が設定されていません。"
                        );
                    }
                    break;

                case ActionType.Toggle:
                    targetObject.SetActive(!targetObject.activeSelf);
                    UpdateTextAlpha(targetObject.activeSelf);
                    break;
            }
        }

        /// <summary>
        /// TMP_Textの色のアルファ値を変更してON/OFFを表現
        /// </summary>
        /// <param name="isActive">ONの状態かどうか</param>
        private void UpdateTextAlpha(bool isActive)
        {
            if (statusText != null)
            {
                Color textColor = statusText.color;
                textColor.a = isActive ? 1f : 100f / 255f; // ONは255、OFFは100
                statusText.color = textColor;
            }
        }
    }
}
