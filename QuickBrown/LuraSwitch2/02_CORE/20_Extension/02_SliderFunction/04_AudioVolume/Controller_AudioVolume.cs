using UdonSharp;
using UnityEngine;
using VRC.SDKBase.Editor.Attributes;

public class Controller_AudioVolume : UdonSharpBehaviour
{

    [HelpBox("━━━━━━━━━━━━━━━━━━━━━━━━━\nAudioSource Control\n━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Header("■ 音量制御設定")]
    [Tooltip("音量を制御する対象のAudioSource（旧：単体 / 後方互換用）")]
    [SerializeField] private AudioSource targetAudioSource;

    [Tooltip("音量を制御する対象のAudioSource（複数指定可）")]
    [SerializeField] private AudioSource[] targetAudioSource_multiple;
    [HelpBox("JP:\nスライダーで音量を調整したいAudioSourceを指定してください。\n複数指定することが可能です。\n\nmaxVolume:スライダー値が1.0の時の最大音量\n\nEN:\nSpecify the AudioSource(s) you want to adjust volume with the slider.\nMultiple sources can be specified.\n\nmaxVolume: Maximum volume when slider value is 1.0", HelpBoxAttribute.MessageType.Info)]

    [Space(5)]
    [Tooltip("スライダー値が1.0の時の最大音量（0.0～1.0）")]
    [SerializeField, Range(0f, 1f)] private float maxVolume = 1.0f;

    [Space(5)]
    [Tooltip("Audio Taperを使用するか")]
    [SerializeField] private bool useAudioTaper = true;
    [HelpBox("JP:\nAudio Taperを有効にすると、音量カーブが自然になります。\nOFF: リニア（直線的）\nON: 対数カーブ（自然な音量変化）\n\nEN:\nEnabling Audio Taper makes the volume curve more natural.\nOFF: Linear (straight)\nON: Logarithmic curve (natural volume change)", HelpBoxAttribute.MessageType.None)]


    [Space(10)]
    [Header("--------------------System（変更不要）--------------------")]
    // ▼ スライダーから書き込まれる変数
    [HideInInspector] public float _value;

    // ▼ スライダーから呼ばれるイベント
    public void OnValueChanged()
    {
        bool hasArrayTargets = targetAudioSource_multiple != null && targetAudioSource_multiple.Length > 0;
        if (!hasArrayTargets && targetAudioSource == null) return;

        float v = Mathf.Clamp01(_value);

        if (useAudioTaper && v > 0)
        {
            // Audio Taper (対数カーブに近い補正)
            float db = Mathf.Lerp(-60f, 0f, v);
            v = Mathf.Pow(10f, db / 20f);
        }

        float finalVolume = v * maxVolume;

        if (hasArrayTargets)
        {
            for (int i = 0; i < targetAudioSource_multiple.Length; i++)
            {
                AudioSource audioSource = targetAudioSource_multiple[i];
                if (audioSource == null) continue;
                audioSource.volume = finalVolume;
            }
        }

        // 後方互換
        if (targetAudioSource != null)
        {
            targetAudioSource.volume = finalVolume;
        }
    }
}