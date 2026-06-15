
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// GameObjectのアクティブ状態に応じてColliderControllerのコライダーを制御します。
    /// このオブジェクトが有効になるとコライダーを有効化し、無効になると無効化します。
    /// </summary>
    public class Collider_Activator : UdonSharpBehaviour
    {
        #region インスペクターフィールド

        [Header("---------------System---------------")]
        [SerializeField] private ColliderController colliderController;

        #endregion

        #region Unityイベント

        /// <summary>
        /// このコンポーネントが有効化された際に呼ばれます。
        /// </summary>
        private void OnEnable()
        {
            if (colliderController != null)
            {
                colliderController.RequestColliderState(true);
                colliderController.PlayConfirmAnimation();
            }
        }

        /// <summary>
        /// このコンポーネントが無効化された際に呼ばれます。
        /// </summary>
        private void OnDisable()
        {
            if (colliderController != null)
            {
                colliderController.RequestColliderState(false);
            }
        }

        #endregion
    }
}
