using RBS.SleepKit2.Udon; // SleepKitSettingsManager を見つけるために追加
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace RBS.SleepKit2.Udon
{
    public class NightModeManager : UdonSharpBehaviour
    {
        [Header("Manager Reference")]
        [Tooltip("SleepKitSettingsManagerへの参照")]
        public SleepKitSettingsManager settingsManager;

        [Header("対象のスライダー群")]
        [Tooltip("複数のSliderをアサインしてください。全て同じ数値になります。")]
        public Slider[] sliders;

        // targetMaterial と propertyName は SleepKitSettingsManager で管理するため削除
        // [Header("対象のマテリアル設定")]
        // [Tooltip("数値を設定する対象のマテリアルを指定してください。")]
        // public Material targetMaterial;
        //
        // [Tooltip("マテリアルのプロパティ名 (例: \"_NightMode\")")]
        // public string propertyName = "_NightMode";

        void Start()
        {
            if (Utilities.IsValid(settingsManager))
            {
                settingsManager.RegisterNightModeManager(this);
                // 初期値の反映はManagerからの通知で行う
            }
            else
            {
                Debug.LogError("[NightModeManager] SleepKitSettingsManagerが設定されていません！");
            }
        }

        void OnDestroy()
        {
            if (Utilities.IsValid(settingsManager))
            {
                settingsManager.UnregisterNightModeManager(this);
            }
        }

        /// <summary>
        /// SliderのOnValueChangedイベントから呼ばれる関数です。
        /// </summary>
        public void OnNightModeValueChanged()
        {
            if (!Utilities.IsValid(settingsManager))
                return;

            // 例として、配列内の先頭のSliderの値を新しい数値として使用
            if (sliders == null || sliders.Length == 0 || sliders[0] == null)
            {
                Debug.LogWarning("NightModeManager: slidersが設定されていません。");
                return;
            }

            float newValue = sliders[0].value;
            settingsManager.SetNightModeValue(newValue); // Managerに値を設定
            // UIとマテリアルの更新はManagerからの通知で行う
        }

        /// <summary>
        /// SleepKitSettingsManagerからの更新通知を受け取ります。
        /// </summary>
        public void _OnNightModeUpdate()
        {
            if (!Utilities.IsValid(settingsManager))
                return;

            float newValue = settingsManager.GetNightModeValue();

            // マテリアルの更新は SleepKitSettingsManager が担当するため、ここでは不要

            // 全てのSliderに通知せずに新しい値を反映する
            if (sliders != null)
            {
                for (int i = 0; i < sliders.Length; i++)
                {
                    if (sliders[i] != null)
                    {
                        sliders[i].SetValueWithoutNotify(newValue);
                    }
                }
            }
        }
    }
}
