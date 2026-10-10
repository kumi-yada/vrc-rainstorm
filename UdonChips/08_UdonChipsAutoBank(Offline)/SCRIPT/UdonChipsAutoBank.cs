/*
██╗   ██╗██████╗  ██████╗ ███╗   ██╗ ██████╗██╗  ██╗██╗██████╗ ███████╗
██║   ██║██╔══██╗██╔═══██╗████╗  ██║██╔════╝██║  ██║██║██╔══██╗██╔════╝
██║   ██║██║  ██║██║   ██║██╔██╗ ██║██║     ███████║██║██████╔╝███████╗
██║   ██║██║  ██║██║   ██║██║╚██╗██║██║     ██╔══██║██║██╔═══╝ ╚════██║
╚██████╔╝██████╔╝╚██████╔╝██║ ╚████║╚██████╗██║  ██║██║██║     ███████║
 ╚═════╝ ╚═════╝  ╚═════╝ ╚═╝  ╚═══╝ ╚═════╝╚═╝  ╚═╝╚═╝╚═╝     ╚══════╝
*/
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UCS;
using TMPro;
using VRC.SDK3.Persistence;
using VRC.SDKBase.Editor.Attributes;
using System;

namespace UCS
{
    public class UdonChipsAutoBank : UdonChipsOfflineModuleBase
    {
        [HelpBox("------------------ UdonChipsAutoBank ----------------- \n\n 日本語:\nワールドジョイン時に前回ログイン時間との差分を計算し、UdonChipsの割合を増減させるギミックです\n\n■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■\n UdonChips Offline moduleを使っているため、Offline moduleに登録する必要があります。\nUdonChips/UdonCipsOffline の 【SerchOfflineModulesボタン】を押してください \n■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■\n\n---------------------------------------------------\n英語:\n A gimmick that calculates the difference between the last login time and the current login time when joining the world, and increases or decreases the UdonChips ratio.\n\n■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■\nThis uses the UdonChips Offline module, so you need to register in the Offline module.\n Click the [SearchOfflineModules button] in UdonChips/UdonCipsOffline\n■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■")]
        [Header("--------------------------Use Offline Module--------------------------")]
        [Header("--------------------------UdonChips--------------------------")]
        [SerializeField]
        private UdonChips udonChips;
        [Header("----------------------Settings / 設定----------------------")]
        [HelpBox("日本語：\nRateValueに変動率（％）を指定することで、オフラインの間も所持金が変動します。\n\n例えば、-1.0に設定すると毎日１％所持金が減ります。\n\n英語：\nBy specifying a rate of change (%) in RateValue, the amount of money in your possession will fluctuate while you are offline.\n\nFor example, setting it to -1.0 will reduce your possessions by 1% daily.")]
        [Space(10)]
        [SerializeField] public float rateValue = -10.0f;

        [Space(10)]
        [HelpBox("日本語：\n デフォルトでは世界標準時で0時に更新されます。更新時間の時差を設定できます。\n\n例えば、日本時間（UTC+9）でワールドに入る場合、timeOffsetに9を設定してください。\n\n英語：\nBy default, it is updated at 0:00 UTC. You can set the time difference for updating.\n\nFor example, if you enter the world in America (UTC-8), set timeOffset to -8.")]
        [Space(10)]
        [SerializeField][Range(-24, 24)] private int timeOffset = 0;

        [Header("--------------------------System--------------------------")]
        public string saveKeyRate = "_UDON_CHIPS/AUTO_BANK/RATE";
        [SerializeField] private TextMeshProUGUI rate_text_1;
        [SerializeField] private TextMeshProUGUI rate_text_2;
        [SerializeField] private TextMeshProUGUI udonChips_Data_text;
        [SerializeField] private TextMeshProUGUI daysGap_text;
        [SerializeField] private TextMeshProUGUI udonChips_Today_text;

        [SerializeField] private GameObject activeDisplay;
        [SerializeField] private GameObject errorDisplay;
        [SerializeField] private GameObject noSaveDataDisplay;
        private int daysGap = 0;
        private float loadedMoney = 0;
        private float oldUdonChips;

        void Start()
        {
            if (udonChips == null)
            {
                udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
            }

            RefleshRateText();


            if (udonChips.saveMode != UdonChipsSaveMode.Auto)
            {
                SetDisplayState(false, true, false);
            }
            else
            {
                SetDisplayState(false, false, true);
            }
        }

        private void SetDisplayState(bool active, bool error, bool noSaveData)
        {
            activeDisplay.SetActive(active);
            errorDisplay.SetActive(error);
            noSaveDataDisplay.SetActive(noSaveData);
        }

        //--------------------------------------------------------------
        //ロード完了後の処理
        //--------------------------------------------------------------
        public override void OnPostLoadUdonChips(UdonChipsOffline offline)
        {
            if (offline.savedTime != offline.loadedTime)
            {
                //ロード完了後
                activeDisplay.SetActive(true);
                errorDisplay.SetActive(false);
                noSaveDataDisplay.SetActive(false);
            }


            //日付の差を計算
            TimeSpan timeDifference = offline.loadedTime - offline.savedTime;
            int totalHoursGap = (int)timeDifference.TotalHours + timeOffset; // 総時間差にtimeOffsetを加算
            daysGap = totalHoursGap / 24; // 日数に変換

            // daysGapをテキストに反映
            daysGap_text.text = daysGap + " Days";

            //saveKeyRateからrateValueをロード
            var localPlayer = Networking.LocalPlayer;
            if (PlayerData.HasKey(localPlayer, saveKeyRate))
            {
                rateValue = PlayerData.GetFloat(localPlayer, saveKeyRate);
            }

            RefleshRateText();

            //----------------------------------------------
            //ロードしたUdonChipsを計算
            //----------------------------------------------

            loadedMoney = offline.loadedMoney;
            oldUdonChips = offline.loadedMoney;


            //offline.loadedMoneyを更新
            offline.loadedMoney = offline.loadedMoney * Mathf.Pow(1 + rateValue / 100, daysGap);
        }

        //--------------------------------------------------------------
        //すべてのモジュールのロード完了後の処理
        //--------------------------------------------------------------
        public override void OnLoadCompleteUdonChips(UdonChipsOffline offline)
        {
            //udonChips_Today_textのテキスト表示を更新
            udonChips_Today_text.text = string.Format(udonChips.format, offline.loadedMoney);

            //udonChips_Data_textのテキスト表示を更新
            udonChips_Data_text.text =
            string.Format(udonChips.format, oldUdonChips) + "\n" + "\n" +
            offline.savedTime.ToString("yyyy/MM/dd") + "\n" +
            offline.loadedTime.ToString("yyyy/MM/dd") + "\n";
        }

        //--------------------------------------------------------------
        //セーブ前の処理
        //--------------------------------------------------------------
        public override void OnPreSaveUdonChips(UdonChipsOffline offline)
        {
            PlayerData.SetFloat(saveKeyRate, rateValue);
        }

        public void RefleshRateText()
        {
            //テキストを更新
            rate_text_1.text = rateValue.ToString() + "% / day";
            rate_text_2.text = rateValue.ToString() + "% / day";
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void Reset()
        {
            UdonChipsAutoSet();
        }

        public void OnValidate()
        {
            UdonChipsAutoSet();
        }

        public void UdonChipsAutoSet()
        {
            var foundObject = GameObject.Find("UdonChips");
            if (foundObject == null || foundObject.Equals(null))
            {
                Debug.LogWarning("UdonChipsAutoBank: UdonChipsがシーンに配置されていません");
            }
            else
            {
                udonChips = foundObject.GetComponent<UdonChips>();
                Debug.Log("UdonChipsAutoBank: UdonChipsを自動設定しました");
            }
        }
#endif
    }
}
