
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;

public class Controller_LightIntensity : UdonSharpBehaviour
{
    [HelpBox("━━━━━━━━━━━━━━━━━━━━━━━━━\nLight Intensity Control\n━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Header("■ ライト強度制御設定")]
    [Tooltip("強度を制御する対象のLight")]
    [SerializeField] private Light[] targetLights;
    [HelpBox("JP:\nスライダーで明るさを調整したいLightを指定してください。\n複数指定することが可能です。\n\nEN:\nSpecify the Light(s) you want to adjust brightness with the slider.\nMultiple lights can be specified.", HelpBoxAttribute.MessageType.Info)]

    [Space(5)]
    [Tooltip("PointLightVolumeInstanceなど、UdonBehaviourベースのライト")]
    [SerializeField] private UdonBehaviour[] targetPointLightVolumes;
    [HelpBox("JP:\nVRCLVのPointLightVolumeが使用可能です。PointLightVolumeInstanceを指定してください。\nSliderDefaultValueの変更は実行しないと反映されませんが、機能しています。\n\nEN:\nVRCLV's PointLightVolume can be used. Specify PointLightVolumeInstance.\nSliderDefaultValue changes do not reflect until executed, but it works.", HelpBoxAttribute.MessageType.Info)]

    [SerializeField] private UdonBehaviour[] targetLightVolumes;


    [Space(5)]
    [Tooltip("スライダー値が0.0の時の最小強度")]
    [SerializeField] private float intensityMin = 0f;

    [Space(5)]
    [Tooltip("スライダー値が1.0の時の最大強度")]
    [SerializeField] private float intensityMax = 1f;

    [Space(10)]
    [Header("--------------------System（変更不要）--------------------")]
    // ▼ スライダーから書き込まれる変数
    [HideInInspector] public float _value;

    // ▼ スライダーから呼ばれるイベント
    public void OnValueChanged()
    {
        float v = Mathf.Clamp01(_value);
        float intensity = Mathf.Lerp(intensityMin, intensityMax, v);
        bool shouldBeActive = v > 0f;

        // 通常のLight制御
        if (targetLights != null)
        {
            for (int i = 0; i < targetLights.Length; i++)
            {
                Light light = targetLights[i];
                if (light == null) continue;

                light.intensity = intensity;
                light.gameObject.SetActive(shouldBeActive);
            }
        }

        // VolumeLightなど、UdonBehaviourベースのライト制御
        if (targetPointLightVolumes != null)
        {
            for (int i = 0; i < targetPointLightVolumes.Length; i++)
            {
                UdonBehaviour volumeLight = targetPointLightVolumes[i];
                if (volumeLight == null) continue;

                // PointLightVolumeInstanceのIntensityプロパティを設定
                volumeLight.SetProgramVariable("Intensity", intensity);

                // アクティブ状態も制御
                volumeLight.gameObject.SetActive(shouldBeActive);
            }
        }

        // LightVolumeなど、UdonBehaviourベースのライト制御
        if (targetLightVolumes != null)
        {
            for (int i = 0; i < targetLightVolumes.Length; i++)
            {
                UdonBehaviour lightVolume = targetLightVolumes[i];
                if (lightVolume == null) continue;

                lightVolume.SetProgramVariable("Intensity", intensity);
            }
        }
    }
}
