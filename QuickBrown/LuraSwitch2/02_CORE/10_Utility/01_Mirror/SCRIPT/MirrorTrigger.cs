
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using QuickBrown.LuraSwitch;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// プレイヤーがミラーエリアに入ったときにミラーの反射強度を距離に応じて自動調整。
    /// Colliderと組み合わせてトリガーエリアを設定。
    /// </summary>
    public class MirrorTrigger : UdonSharpBehaviour
    {
        [Tooltip("制御対象のMirrorControllerを設定します。")]
        [SerializeField] private MirrorController mirrorController;

        private bool isPlayerInside = false;
        private VRCPlayerApi localPlayer;


        /// <summary>
        /// 初期化処理。ローカルプレイヤーの参照を取得します。
        /// </summary>
        void Start()
        {
            localPlayer = Networking.LocalPlayer;
            // ワールド参加時にすでにトリガー内にいる可能性があるため、遅延チェック
            SendCustomEventDelayedSeconds(nameof(CheckInitialPlayerPosition), 1.0f);
        }

        /// <summary>
        /// ワールド参加時にプレイヤーがすでにトリガー内にいるかチェックします。
        /// </summary>
        public void CheckInitialPlayerPosition()
        {
            if (localPlayer == null || mirrorController == null) return;

            // トリガーのColliderを取得
            Collider triggerCollider = GetComponent<Collider>();
            if (triggerCollider == null) return;

            // プレイヤーの頭部位置を取得
            Vector3 headPosition = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            // プレイヤーがトリガー範囲内にいるかチェック
            if (triggerCollider.bounds.Contains(headPosition))
            {
                // 範囲内にいる場合、手動でEnter処理を実行
                isPlayerInside = true;
                mirrorController.OnPlayerEnterMirrorArea();

                // 初回の距離に基づくミラーパワーも更新
                UpdateMirrorPowerBasedOnDistance();
            }
        }

        /// <summary>
        /// プレイヤーがトリガーエリアに入った際に呼ばれます。
        /// ローカルプレイヤーの場合のみ、ミラーコントローラーに入場を通知します。
        /// </summary>
        /// <param name="player">トリガーに入ったプレイヤー</param>
        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (IsLocalPlayer(player))
            {
                isPlayerInside = true;
                if (mirrorController != null)
                {
                    mirrorController.OnPlayerEnterMirrorArea();
                }
            }
        }

        /// <summary>
        /// プレイヤーがトリガーエリア内に滞在している間、継続的に呼ばれます。
        /// ローカルプレイヤーの場合、距離に基づいてミラーパワーを更新します。
        /// </summary>
        /// <param name="player">トリガー内に滞在しているプレイヤー</param>
        public override void OnPlayerTriggerStay(VRCPlayerApi player)
        {
            if (IsLocalPlayer(player) && isPlayerInside && mirrorController != null)
            {
                UpdateMirrorPowerBasedOnDistance();
            }
        }

        /// <summary>
        /// プレイヤーがトリガーエリアから出た際に呼ばれます。
        /// ローカルプレイヤーの場合のみ、ミラーコントローラーに退場を通知します。
        /// </summary>
        /// <param name="player">トリガーから出たプレイヤー</param>
        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (IsLocalPlayer(player))
            {
                isPlayerInside = false;
                if (mirrorController != null)
                {
                    mirrorController.OnPlayerExitMirrorArea();
                }
            }
        }

        /// <summary>
        /// 指定されたプレイヤーがローカルプレイヤーかどうかを確認します。
        /// </summary>
        /// <param name="player">確認対象のプレイヤー</param>
        /// <returns>ローカルプレイヤーの場合true</returns>
        private bool IsLocalPlayer(VRCPlayerApi player)
        {
            if (localPlayer == null || player == null) return false;
            return player.isLocal;
        }

        /// <summary>
        /// プレイヤーの頭部位置からミラーエリアまでの距離に基づいてミラーパワーを更新します。
        /// MirrorControllerの距離計算機能を使用して反射強度を調整します。
        /// </summary>
        private void UpdateMirrorPowerBasedOnDistance()
        {
            if (localPlayer == null || mirrorController == null) return;

            // プレイヤーの頭部（視点）位置を取得
            Vector3 headPosition = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            // Start/Full エリアへの距離比に基づいてミラーパワーを計算・設定
            float newMirrorPower = mirrorController.CalculateMirrorPowerFromPosition(headPosition);
            mirrorController.SetMirrorPower(newMirrorPower);
        }
    }
}
