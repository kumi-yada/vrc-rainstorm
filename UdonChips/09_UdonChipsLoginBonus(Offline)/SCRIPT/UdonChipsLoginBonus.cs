using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UCS;
using System;
using VRC.SDK3.Persistence;
using VRC.SDKBase.Editor.Attributes;

public class UdonChipsLoginBonus : UdonChipsOfflineModuleBase
{

    [HelpBox("日本語：\nOfflineModuleを使用します。UdonChips/UdonChipsOffline の 【SerchOfflineModulesボタン】を押してください\n\n英語：\nUses OfflineModule. Click the [SearchOfflineModules button] in UdonChips/UdonChipsOffline")]
    [Header("--------------------------UdonChips--------------------------")]
    [SerializeField] private UdonChips udonChips;

    [HelpBox("日本語：\n デフォルトでは世界標準時(UTC)で0時に更新されます。更新時間の時差を設定できます。\n\n例えば、日本時間（UTC+9）でワールドに入る場合、timeOffsetに9を設定してください。\n\n英語：\nBy default, it is updated at 0:00 UTC. You can set the time difference for updating.\n\nFor example, if you enter the world in America (UTC-8), set timeOffset to -8.")]
    [Space(10)]
    [SerializeField]
    [Range(-24, 24)]
    private int timeOffset = 0;

    [Header("1日のログインボーナス額")]
    public int dailyBonus = 1000;


    void Start()
    {
        if (udonChips == null)
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
        }
    }

    // --------------------------------------------------------------------------------
    // プレイヤーがワールドに参加した時に呼ばれる
    // --------------------------------------------------------------------------------
    public override void OnPostLoadUdonChips(UdonChipsOffline offline)
    {
        // ログインボーナスの付与を判定
        CheckDailyBonus(offline);
        Debug.Log("LoadUdonChips");
    }

    // --------------------------------------------------------------------------------
    // ログインボーナスの付与を判定
    // --------------------------------------------------------------------------------
    public void CheckDailyBonus(UdonChipsOffline offline)
    {
        // 現在のUTC時刻に timeOffset 分だけ足して、更新の基準となるローカル時刻を作成
        DateTime currentDateTime = offline.loadedTime.ToUniversalTime().AddHours(timeOffset);
        string currentDate = currentDateTime.ToString("yyyyMMdd");

        // 前回セーブ（保存）した時刻を UTC から timeOffset 分だけ足し、前回の基準ローカル時刻を作成
        DateTime lastSavedTime = offline.savedTime.ToUniversalTime().AddHours(timeOffset);
        string lastSavedDate = lastSavedTime.ToString("yyyyMMdd");

        Debug.Log("UdonChipsLoginBonus: " + currentDate + " " + lastSavedDate);

        // 日付が違っていればログインボーナス付与
        if (currentDate != lastSavedDate)
        {
            offline.loadedMoney += dailyBonus;
            Debug.Log("udonChips.money: " + udonChips.money + " UdonChipsを付与しました");
            Debug.Log("UdonChipsLoginBonus: ログインボーナス付与");
        }
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
            Debug.LogWarning("UdonChipsLoginBonus: UdonChipsがシーンに配置されていません");
        }
        else
        {
            udonChips = foundObject.GetComponent<UdonChips>();
            Debug.Log("UdonChipsLoginBonus: UdonChipsを自動設定しました");
        }
    }
#endif
}
