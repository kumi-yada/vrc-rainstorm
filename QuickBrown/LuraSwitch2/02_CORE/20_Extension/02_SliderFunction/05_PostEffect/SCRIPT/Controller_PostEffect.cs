
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.Rendering.PostProcessing;
using VRC.SDKBase.Editor.Attributes;

public class Controller_PostEffect : UdonSharpBehaviour
{
    [HelpBox("━━━━━━━━━━━━━━━━━━━━━━━━━\nPost Process Effect Control\n━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Header("■ ポストエフェクト制御設定")]
    [Tooltip("強度を制御する対象のPostProcessVolume")]
    [SerializeField] private PostProcessVolume[] targetPostProcessVolumes;
    [HelpBox("JP:\nスライダーでエフェクトの強度（Weight）を調整したいPostProcessVolumeを指定してください。\n複数指定することが可能です。\n\nEN:\nSpecify the PostProcessVolume(s) you want to adjust effect intensity (Weight) with the slider.\nMultiple volumes can be specified.")]

    [Space(20)]
    [SerializeField] private MeshRenderer[] NightModeMeshRenderers;
    [HelpBox("JP:\nナイトモード用のメッシュを設定します。\nEN:\nSet the mesh for night mode.")]

    [Header("--------------------System（変更不要）--------------------")]
    [SerializeField] private string ValuePropertyName = "_Value";
    [SerializeField] private string overlaySortingLayerName = "Default";
    [SerializeField] private int overlaySortingOrder = 1000;
    // ▼ スライダーから書き込まれる変数
    [HideInInspector] public float _value;

    private MaterialPropertyBlock _propertyBlock;

    // ▼ スライダーから呼ばれるイベント
    public void OnValueChanged()
    {
        ApplyValue(Mathf.Clamp01(_value));
    }

    private void ApplyValue(float v)
    {
        bool shouldBeActive = v > 0f;

        if (targetPostProcessVolumes != null)
        {
            for (int i = 0; i < targetPostProcessVolumes.Length; i++)
            {
                PostProcessVolume pp = targetPostProcessVolumes[i];
                if (pp == null) continue;

                pp.weight = v;
                pp.gameObject.SetActive(shouldBeActive);
            }
        }

        if (NightModeMeshRenderers == null) return;

        if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < NightModeMeshRenderers.Length; i++)
        {
            MeshRenderer renderer = NightModeMeshRenderers[i];
            if (renderer == null) continue;

            renderer.gameObject.SetActive(shouldBeActive);
            renderer.enabled = shouldBeActive;
            if (!shouldBeActive) continue;

            renderer.sortingLayerName = overlaySortingLayerName;
            renderer.sortingOrder = overlaySortingOrder;

            if (string.IsNullOrEmpty(ValuePropertyName)) continue;

            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(ValuePropertyName, v);
            renderer.SetPropertyBlock(_propertyBlock);

            Material[] materials = renderer.materials;
            if (materials == null) continue;

            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material == null) continue;
                if (!material.HasProperty(ValuePropertyName)) continue;

                material.SetFloat(ValuePropertyName, v);
            }
        }
    }

    private void OnValidate()
    {
        ApplyValue(Mathf.Clamp01(_value));
    }
}
