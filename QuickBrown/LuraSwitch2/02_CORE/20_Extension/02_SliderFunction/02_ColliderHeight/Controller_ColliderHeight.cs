
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;
using QuickBrown.LuraSwitch;

public class Controller_ColliderHeight : UdonSharpBehaviour
{
    [HelpBox("━━━━━━━━━━━━━━━━━━━━━━━━━\nCollider Height Control\n━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Header("■ コライダー高さ制御設定")]
    [Tooltip("高さを制御する対象のColliderController")]
    [SerializeField] private ColliderController[] colliderControllers;
    [HelpBox("JP:\n操作したいLuraColliderを指定してください。\n複数指定することが可能です。\n\nEN:\nSpecify the LuraCollider(s) you want to control.\nMultiple colliders can be specified.", HelpBoxAttribute.MessageType.Info)]

    [Space(10)]
    [Header("--------------------System（変更不要）--------------------")]
    // ▼ スライダーから書き込まれる変数
    [HideInInspector] public float _value;

    // ▼ スライダーから呼ばれるイベント
    public void OnValueChanged()
    {
        if (colliderControllers == null) return;

        float v = Mathf.Clamp01(_value);

        for (int i = 0; i < colliderControllers.Length; i++)
        {
            ColliderController c = colliderControllers[i];
            if (c == null) continue;

            c.ApplySliderValue01(v);
        }
    }
}
