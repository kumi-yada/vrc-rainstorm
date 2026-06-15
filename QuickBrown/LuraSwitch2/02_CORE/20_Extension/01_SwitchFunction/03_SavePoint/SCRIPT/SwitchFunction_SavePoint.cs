
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.SDKBase.Editor.Attributes;

public enum TeleportVisualMode
{
    None,
    Use
}

namespace QuickBrown.LuraSwitch
{
    public class SwitchFunction_SavePoint : UdonSharpBehaviour
    {
        [Header("ビジュアル設定")]
        [HelpBox("━━━━━━━━━━━━━━━━━━━━━━━━━\nTeleport Target\n━━━━━━━━━━━━━━━━━━━━━━━━━\n\nJP:\n[====SavePoint_Activator====]がアクティブのときは、ジョインおよびリスポーン位置が上書きされます。\n\nスイッチが移動するとセーブポイントも一緒に移動してしまうため、PrefabをUnpackして別階層に移して使用してください\n\nEN:\nWhen [====SavePoint_Activator====] is active, join and respawn positions are overridden.\n\nSince the save point moves with the switch, please Unpack the Prefab and move it to a separate hierarchy before use.", HelpBoxAttribute.MessageType.Info)]
        [SerializeField] private TeleportVisualMode useTeleportVisual = TeleportVisualMode.None;

        [Header("----------System（変更不要）----------")]

        [Header("テレポート先")]

        [SerializeField] public Transform TargetTransform;

        [Header("ビジュアル設定（System）")]
        [SerializeField] private Animator visualAnimator;
        [SerializeField] private GameObject visualObject;

        [Header("ランタイム設定（System）")]
        [Tooltip("ジョイン時テレポートを遅延させる秒数。\n同期オブジェクトの状態が確定するまでの時間より長く設定してください。")]
        [SerializeField] private float joinTeleportDelaySeconds = 3.0f;

        #region ランタイムフィールド

        /// <summary>
        /// セーブポイントが有効かどうか（SavePoint_Activatorのアクティブ状態に連動）
        /// </summary>
        private bool _isActive = false;

        /// <summary>
        /// ランタイムでビジュアルを表示するかどうか（TeleportVisualMode.Useとは別枠）
        /// </summary>
        private bool _runtimeTeleportVisualEnabled = true;

        #endregion

        #region Unityイベント

        private void Start()
        {
            // 初期見た目を反映
            UpdateVisualObject();
        }

        #endregion

        #region 公開メソッド

        /// <summary>
        /// SavePoint_Activatorから呼ばれ、セーブポイントの有効/無効を設定します。
        /// </summary>
        /// <param name="active">有効にする場合はtrue、無効にする場合はfalse</param>
        public void SetActive(bool active)
        {
            _isActive = active;

            // 表示切替は別枠（ランタイム用フラグ + Use設定）で制御
            UpdateVisualObject();
        }

        /// <summary>
        /// ランタイムでテレポートビジュアルを表示するかどうかを切り替えます。
        /// TeleportVisualMode.Use は「この機能を使うかどうか」の選択であり、
        /// ランタイムのON/OFFはこのメソッドで制御します。
        /// </summary>
        public void SetTeleportVisualEnabled(bool enabled)
        {
            _runtimeTeleportVisualEnabled = enabled;
            UpdateVisualObject();
        }

        #endregion

        #region VRChat イベント

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // GameObject が非アクティブの場合は処理しない（Udon はアクティブ状態をバイパスするため明示的にチェック）
            if (!gameObject.activeInHierarchy) return;

            if (!player.isLocal) return;

            // 同期オブジェクトの状態が確定するまで遅延してからテレポートを試みる
            // （ジョイン直後は他オブジェクトの同期がまだ届いていないため即時テレポートしない）
            SendCustomEventDelayedSeconds(nameof(OnDelayedJoinTeleport), joinTeleportDelaySeconds);
        }

        /// <summary>
        /// ジョイン時の遅延テレポート。同期が届いた後に _isActive の状態を確認してテレポートします。
        /// </summary>
        public void OnDelayedJoinTeleport()
        {
            if (!gameObject.activeInHierarchy) return;
            if (!_isActive || TargetTransform == null) return;

            var localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && localPlayer.IsValid())
            {
                TeleportToTarget(localPlayer);
                SendTeleportAnimationNetworkEvent();
            }
        }

        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            // GameObject が非アクティブの場合は処理しない（Udon はアクティブ状態をバイパスするため明示的にチェック）
            if (!gameObject.activeInHierarchy) return;

            // ローカルプレイヤーのリスポーン時のみ処理
            if (player.isLocal && _isActive && TargetTransform != null)
            {
                TeleportToTarget(player);

                // 自分がテレポートした事実を全員へ通知（各クライアントでアニメーションを発火）
                SendTeleportAnimationNetworkEvent();
            }
        }

        #endregion

        #region 内部メソッド

        private void TeleportToTarget(VRCPlayerApi player)
        {
            if (player != null && player.IsValid())
            {
                player.TeleportTo(TargetTransform.position, TargetTransform.rotation);
            }
        }

        private bool IsTeleportVisualEnabled()
        {
            return useTeleportVisual == TeleportVisualMode.Use && _runtimeTeleportVisualEnabled;
        }

        private void SendTeleportAnimationNetworkEvent()
        {
            if (!IsTeleportVisualEnabled() || visualAnimator == null)
            {
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Network_PlayTeleportEmission));
        }

        public void Network_PlayTeleportEmission()
        {
            if (!IsTeleportVisualEnabled() || visualAnimator == null)
            {
                return;
            }

            visualAnimator.SetTrigger("Emission");
        }

        private void UpdateVisualObject()
        {
            if (visualObject == null)
            {
                return;
            }

            // 表示自体は「機能を使う（Use）」「ランタイムで許可」「セーブポイントが有効」すべてが満たされたときのみ
            visualObject.SetActive(_isActive && IsTeleportVisualEnabled());
        }

        #endregion

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        #region エディタサポート

        /// <summary>
        /// Inspector上で値を変更した時に、エディタ上の見た目へ即反映します。
        /// 再生中はランタイム処理があるため、ここでは何もしません。
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

            // エディタ上でのビジュアル表示切り替え
            if (visualObject != null)
            {
                visualObject.SetActive(useTeleportVisual == TeleportVisualMode.Use);
            }
        }

        #endregion
#endif
    }
}