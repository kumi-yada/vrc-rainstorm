
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;
using QuickBrown.LuraSwitch;

public class Controller_MirrorOpacity : UdonSharpBehaviour
{
    [HelpBox("━━━━━━━━━━━━━━━━━━━━━━━━━\nMirror Opacity Control\n━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Header("■ ミラー不透明度制御設定")]
    [Tooltip("不透明度を制御する対象のMirrorController")]
    [SerializeField] private MirrorController[] mirrorControllers;
    [HelpBox("JP:\nスライダーでミラーの不透明度を調整したいMirrorControllerを指定してください。\n複数指定することが可能です。\nスライダー値が0の時、ミラーは非アクティブ化されます（負荷軽減のため）。\n\nEN:\nSpecify the MirrorController(s) you want to adjust opacity with the slider.\nMultiple controllers can be specified.\nWhen slider value is 0, mirrors are deactivated (to reduce load).")]

    [Space(10)]
    [Header("--------------------System（変更不要）--------------------")]
    // ▼ スライダーから書き込まれる変数
    [HideInInspector] public float _value;

    private const float MirrorZeroEpsilon = 0.00001f;
    private bool _mirrorTemporarilyHidden;

    // ▼ スライダーから呼ばれるイベント
    public void OnValueChanged()
    {
        if (mirrorControllers == null) return;

        float v = Mathf.Clamp01(_value);

        // sliderValue が 0 の時だけ、ミラーを非アクティブ化する
        // (透明=0でもミラーがアクティブだと負荷が残るため)
        bool shouldHideMirror = v <= MirrorZeroEpsilon;

        for (int i = 0; i < mirrorControllers.Length; i++)
        {
            MirrorController mc = mirrorControllers[i];
            if (mc == null) continue;

            mc.SetMaxOpacity(v);

            // 0↔非0 の境目でだけ切り替える（毎フレーム呼ばない）
            if (shouldHideMirror != _mirrorTemporarilyHidden)
            {
                mc.SetTemporarilyHiddenByExternalControl(shouldHideMirror);
            }
        }

        _mirrorTemporarilyHidden = shouldHideMirror;
    }
}
