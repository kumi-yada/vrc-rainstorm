using UdonSharp;
using UnityEngine;
using VRC.Udon;
using VRC.SDKBase;
using VRC.SDKBase.Editor.Attributes;

public class Controller_ExternalParameter : UdonSharpBehaviour
{
    [HelpBox("━━━━━━━━━━━━━━━━━━━━━━━━━\nExternalParameter Control\n━━━━━━━━━━━━━━━━━━━━━━━━━")]

    [Tooltip("値を書き込む対象のUdonBehaviour（複数指定可）")]
    [SerializeField] private UdonBehaviour[] targets;
    [HelpBox("JP:\nターゲットのUdonBehaviourの変数へ SetProgramVariable で書き込みます。\n\nEN:\nWrites to the variable of the target UdonBehaviour(s) via SetProgramVariable.", HelpBoxAttribute.MessageType.Info)]

    [Tooltip("書き込み先の変数名")]
    [SerializeField] private string targetVariableName = "";

    [Space(10)]
    [HideInInspector] public float _value;

    [Space(5)]
    [Header("--------------------System（変更不要）--------------------")]
    [Tooltip("初回反映の遅延秒数（0以上）")]
    [HelpBox("JP:\n初回数値反映の遅延秒数\n\nEN:\nInitial delay in seconds before applying the value for the first time.", HelpBoxAttribute.MessageType.Info)]
    [SerializeField] private float initialApplyDelaySeconds = 0.2f;


    private void Start()
    {
        ScheduleInitialApplyDelayed();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        ScheduleInitialApplyDelayed();
    }

    private void ScheduleInitialApplyDelayed()
    {
        float delay = initialApplyDelaySeconds;
        if (delay < 0f)
        {
            delay = 0f;
        }

        SendCustomEventDelayedSeconds(nameof(ApplyInitialValueDelayed), delay);
    }

    public void ApplyInitialValueDelayed()
    {
        ApplyValueToTargets(_value);
    }

    public void OnValueChanged()
    {
        ApplyValueToTargets(_value);
    }

    public void SetValue01(float value01)
    {
        _value = Mathf.Clamp01(value01);
        ApplyValueToTargets(_value);
    }

    private void ApplyValueToTargets(float value01)
    {
        if (string.IsNullOrEmpty(targetVariableName))
        {
            return;
        }

        float v = Mathf.Clamp01(value01);

        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            UdonBehaviour receiver = targets[i];
            if (receiver == null)
            {
                continue;
            }

            receiver.SetProgramVariable(targetVariableName, v);
        }
    }
}