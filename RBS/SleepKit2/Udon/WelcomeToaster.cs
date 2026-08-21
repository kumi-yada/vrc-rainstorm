
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace RBS.SleepKit2.Udon
{
    public class WelcomeToaster : UdonSharpBehaviour
    {
        [Header("通知のタイトル(VR)")]
        public string titleVr = "ようこそ！";
        [Header("通知のタイトル(デスクトップ)")]
        public string titleDesktop = "ようこそ！";
        [Header("通知の内容(VR)")]
        public string messageVr = "左トリガーをダブルクリックするとメニューを呼び出せます";
        [Header("通知の内容(デスクトップ)")]
        public string messageDesktop = "Eキーを押すとメニューを呼び出せます";
        [HideInInspector] public ToastManager toastManager;
        [Header("参加時のメッセージ機能を有効にするか")]
        public bool enableOnStart = true;
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!enableOnStart) return;
            if (player == null || !player.isLocal) return;
            if(Networking.LocalPlayer.IsUserInVR()) {
                toastManager.ShowToast(titleVr, messageVr);
            }
            else
            {
                toastManager.ShowToast(titleDesktop, messageDesktop);
            }
        }
    }
}
