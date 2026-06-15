
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// GameObjectのアクティブ状態に応じてSavePointの有効/無効を制御します。
    /// このオブジェクトが有効になるとセーブポイントを有効化し、無効になると無効化します。
    /// </summary>
    public class SavePoint_Activator : UdonSharpBehaviour
    {
        #region インスペクターフィールド

        [Header("---------------System---------------")]
        [SerializeField] private SwitchFunction_SavePoint savePoint;

        #endregion

        #region Unityイベント

        /// <summary>
        /// このコンポーネントが有効化された際に呼ばれます。
        /// </summary>
        private void OnEnable()
        {
            if (savePoint != null)
            {
                savePoint.SetActive(true);
            }
        }

        /// <summary>
        /// このコンポーネントが無効化された際に呼ばれます。
        /// </summary>
        private void OnDisable()
        {
            if (savePoint != null)
            {
                savePoint.SetActive(false);
            }
        }

        #endregion
    }
}
