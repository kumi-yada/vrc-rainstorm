using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace RBS.SleepKit2.Udon
{
    public class TopMirrorHeightManager : UdonSharpBehaviour
    {
        [Header("Transform移動対象 (複数対応)")]
        [Tooltip("各Sliderで移動させる対象のTransformの配列")]
        public Transform[] targetTransforms;
        
        [Tooltip("各対象の移動開始地点のTransformの配列")]
        public Transform[] startTransforms;
        
        [Tooltip("各対象の移動終了地点のTransformの配列")]
        public Transform[] endTransforms;
        
        [Header("UI Sliders (複数対応)")]
        [Tooltip("各対象の位置移動に使用するSlider（値は0～1、全て同じ値になります）")]
        public Slider[] sliders;
        
        [Header("Persistence")]
        [Tooltip("全てのSliderの値を保存するためのキー")]
        string persistenceKey = "TOP_MIRROR_HEIGHT";

        [Header("Persistence Settings")]
        [Tooltip("PersistenceManagerの参照。永続データのキーにこのprefixが付加されます。")]
        [HideInInspector] public PersistenceManager persistenceManager;
        
        // 全てのSliderが共有する値
        private float sharedValue = 0f;
        
        private void Start()
        {
            int count = sliders.Length;
            // 配列の要素数がすべて同じであるかチェック
            if (targetTransforms.Length != count ||
                startTransforms.Length != count ||
                endTransforms.Length != count)
            {
                Debug.LogError("TopMirrorHeightManager: 各配列の長さが一致していません。");
                return;
            }
        }
        
        /// <summary>
        /// 指定した基本キーに、PersistenceManagerが設定されていればそのprefixを付与したキーを返します。
        /// </summary>
        private string GetPrefixedKey(string baseKey)
        {
            return (persistenceManager != null ? persistenceManager.prefix : "") + baseKey;
        }
        
        /// <summary>
        /// GUI側で各SliderのOnValueChangedイベントに登録する関数です。  
        /// </summary>
        public void OnSliderValueChanged()
        {
            // ここでは、配列の先頭のSliderの値を基準の新しい値として使用する
            sharedValue = sliders[0].value;
            
            int count = sliders.Length;
            for (int i = 0; i < count; i++)
            {
                // 通知せずに各Sliderの値を更新する
                sliders[i].SetValueWithoutNotify(sharedValue);
                UpdatePosition(i, sharedValue);
            }
            
            // Persistenceに新しい値を保存（プレフィックス付きのキーを使用）
            if (Networking.LocalPlayer != null)
            {
                PlayerData.SetFloat(GetPrefixedKey(persistenceKey), sharedValue);
            }
        }
        
        /// <summary>
        /// 指定したインデックスの対象Transformを、startTransform～endTransform間で補間移動させます。
        /// </summary>
        /// <param name="index">配列のインデックス</param>
        /// <param name="value">Sliderの値（0～1）</param>
        private void UpdatePosition(int index, float value)
        {
            if (targetTransforms[index] == null ||
                startTransforms[index] == null ||
                endTransforms[index] == null)
            {
                Debug.LogError("TopMirrorHeightManager: Transformの参照が不足しています。index:" + index);
                return;
            }
            targetTransforms[index].position = Vector3.Lerp(startTransforms[index].position,
                                                             endTransforms[index].position,
                                                             value);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            // Persistenceから保存された共有値を、プレフィックス付きのキーで読み込む（存在すれば）
            if (Networking.LocalPlayer != null &&
                PlayerData.TryGetFloat(Networking.LocalPlayer, GetPrefixedKey(persistenceKey), out float savedValue))
            {
                sharedValue = savedValue;
                
                // 各Sliderに保存された値を反映し、対応Transformの位置を更新
                for (int i = 0; i < sliders.Length; i++)
                {
                    sliders[i].SetValueWithoutNotify(sharedValue);
                    UpdatePosition(i, sharedValue);
                }
            }
        }
    }
}
