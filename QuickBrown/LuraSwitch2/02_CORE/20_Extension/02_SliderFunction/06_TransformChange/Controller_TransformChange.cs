
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;

public class Controller_TransformChange : UdonSharpBehaviour
{
    [HelpBox("━━━━━━━━━━━━━━━━━━━━━━━━━\nTransform Control\n━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("位置・回転・スケールを変更する対象のTransform")]
    [SerializeField] private Transform[] transformTargets;
    [HelpBox("JP:\nスライダーでTransform（位置・回転・スケール）を変更したい対象を指定してください。\nここで選択したオブジェクトが可動します。\n\ntransformRefs_Aには変形前のTransformを指定し、\ntransformRefs_Bには変形後のTransformを指定します。\nこれにより、スライダー値に応じて対象のオブジェクトを移動、回転、スケールさせることができます。\nまた、配列に対応しているため、複数のオブジェクトを同時に制御可能です。\n\nEN:\nSpecify the target(s) you want to change Transform (position, rotation, scale) with the slider.\nThe selected objects will move.\n\nSpecify the Transform before deformation in transformRefs_A,\nand the Transform after deformation in transformRefs_B.\nThis allows you to move, rotate, and scale target objects according to slider value.\nSupports arrays, so multiple objects can be controlled simultaneously.", HelpBoxAttribute.MessageType.Info)]

    [Space(5)]
    [Tooltip("スライダー値が0の時の参照Transform（位置・回転・スケール）")]
    [SerializeField] private Transform[] transformRefs_A;
    [Space(5)]
    [Tooltip("スライダー値が1の時の参照Transform（位置・回転・スケール）")]
    [SerializeField] private Transform[] transformRefs_B;

    [Space(10)]
    [Header("■ Transform Control Flags")]
    [Tooltip("位置を変更するか")]
    [SerializeField] private bool usePosition = true;
    [Tooltip("回転を変更するか")]
    [SerializeField] private bool useRotation = true;
    [Tooltip("スケールを変更するか")]
    [SerializeField] private bool useScale = true;

    [Space(10)]
    [Header("--------------------System（変更不要）--------------------")]
    // ▼ スライダーから書き込まれる変数
    [HideInInspector] public float _value;

    // ▼ スライダーから呼ばれるイベント
    public void OnValueChanged()
    {
        if (transformTargets == null || transformRefs_A == null || transformRefs_B == null) return;

        float v = Mathf.Clamp01(_value);

        for (int i = 0; i < transformTargets.Length; i++)
        {
            Transform t = transformTargets[i];
            if (t == null) continue;

            Transform a = (i < transformRefs_A.Length) ? transformRefs_A[i] : null;
            Transform b = (i < transformRefs_B.Length) ? transformRefs_B[i] : null;
            if (a == null || b == null) continue;

            // フラグに応じて各要素を適用
            if (usePosition)
            {
                Vector3 pos = Vector3.Lerp(a.position, b.position, v);
                t.position = pos;
            }

            if (useRotation)
            {
                Quaternion rot = Quaternion.Slerp(a.rotation, b.rotation, v);
                t.rotation = rot;
            }

            if (useScale)
            {
                t.localScale = Vector3.Lerp(a.localScale, b.localScale, v);
            }
        }
    }
}
