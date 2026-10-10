
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UCS;
using VRC.SDKBase.Editor.Attributes;

public class UdonChipsAutoClamp : UdonChipsOfflineModuleBase
{

    [Header("■■■■■ UdonChipsAutoClamp ■■■■■")]
    [HelpBox("------------------ UdonChipsAutoClamp ----------------- \n\n 日本語:\nセーブデータロード時に所持金の最小値と最大値を反映させるギミックです。\n\n■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■\n UdonChips Offline moduleを使っているため、Offline moduleに登録する必要があります。\nUdonChips/UdonCipsOffline の 【SerchOfflineModulesボタン】を押してください \n■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■\n\n---------------------------------------------------\n英語:\nThis is a gimmick that reflects the minimum and maximum amount of money when loading save data.\n\n■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■\nThis uses the UdonChips Offline module, so you need to register in the Offline module.\n Click the [SearchOfflineModules button] in UdonChips/UdonCipsOffline\n■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■")]
    [Header("When loading save data, use the maximum and minimum values.")]
    [Header("セーブデータのロードのときに最大値と最小値を利用します.")]
    [Header("--------------------------Settings--------------------------")]
    [SerializeField] private bool useMaxClamp = false;
    [SerializeField] private float maxSaveUdonChips;
    [SerializeField] private bool useMinClamp = false;
    [SerializeField] private float minSaveUdonChips;

    public override void OnPostLoadUdonChips(UdonChipsOffline offline)
    {
        if (useMaxClamp)
        {
            if (offline.loadedMoney > maxSaveUdonChips)
            {
                offline.loadedMoney = maxSaveUdonChips;
            }
            else if (offline.loadedMoney < 0)
            {
                offline.loadedMoney = 0;
            }
        }

        if (useMinClamp)
        {
            if (offline.loadedMoney < minSaveUdonChips)
            {
                offline.loadedMoney = minSaveUdonChips;
            }
            else if (offline.loadedMoney > 0)
            {
                offline.loadedMoney = 0;
            }
        }

        offline.loadedMoney = Mathf.Clamp(offline.loadedMoney, minSaveUdonChips, maxSaveUdonChips);
    }
}
