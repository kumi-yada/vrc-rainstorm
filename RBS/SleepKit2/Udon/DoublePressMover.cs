using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces; // HandType を使用する場合

namespace RBS.SleepKit2.Udon
{
    public class DoublePressMover : UdonSharpBehaviour
    {
        [Header("対象オブジェクト")]
        [Tooltip("移動させるオブジェクト")]
        [HideInInspector]
        public GameObject objectA;

        [Tooltip("参照位置として使うオブジェクト")]
        [HideInInspector]
        public GameObject objectB;

        [Header("ダブルクリックの検出間隔（秒）")]
        [Tooltip("2回押しと判定する間隔（秒）")]
        public float doublePressThreshold = 0.25f;

        [Header("トリガーエリア内にいる場合のみ呼び出せるようにする")]
        [Tooltip(
            "この機能を有効にすると、指定のトリガーエリア内にいる場合のみダブルプレスが機能します。"
        )]
        public bool requireTriggerArea = false;

        [Tooltip(
            "トリガーエリアのColliderを指定します。対象オブジェクトとは別のオブジェクトにColliderが設定されている場合も指定可能です。"
        )]
        public Collider triggerAreaCollider;

        // プレイヤーがトリガーエリア内にいるかどうかのフラグ（Updateで判定）
        private bool isPlayerInAllowedArea = false;

        // 左手の最後にボタンが押された時刻（秒）
        private float lastLeftTriggerTime = 0f;

        /// <summary>
        /// VRコントローラーのトリガー（InputUse）が押された際の処理
        /// </summary>
        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            // VRでボタンが押されたタイミングのみ処理する
            if (!value || !Networking.LocalPlayer.IsUserInVR())
                return;

            // 左手からの入力のみ処理
            if (args.handType != HandType.LEFT)
                return;

            // requireTriggerArea が有効の場合、エリア内にいなければ処理しない
            if (requireTriggerArea && !isPlayerInAllowedArea)
                return;

            if (Time.time - lastLeftTriggerTime < doublePressThreshold)
            {
                // ダブルプレスと判定した場合、objectA を objectB の位置・回転に移動する
                ProcessDoublePress();
                // ダブルプレス判定用のタイマーをリセット
                lastLeftTriggerTime = 0f;
            }
            else
            {
                // 初回の押下とするため、タイムスタンプを記録
                lastLeftTriggerTime = Time.time;
            }
        }

        /// <summary>
        /// UpdateでTabキーの入力を検出し、ダブルプレス判定を行う（テスト用）
        /// </summary>
        public void Update()
        {
            // requireTriggerArea が有効で、triggerAreaCollider が設定されている場合、プレイヤーの位置がCollider内にあるかチェック
            if (requireTriggerArea && triggerAreaCollider != null && Networking.LocalPlayer != null)
            {
                // ※ Collider.bounds は軸に沿った境界ボックスであるため、厳密な判定が必要な場合は別途実装してください
                isPlayerInAllowedArea = triggerAreaCollider.bounds.Contains(
                    Networking.LocalPlayer.GetPosition()
                );
            }
            else if (requireTriggerArea)
            {
                // requireTriggerArea が有効だが Collider が設定されていない場合は警告
                Debug.LogWarning(
                    "DoublePressMover: requireTriggerArea が有効ですが、triggerAreaCollider が設定されていません。"
                );
                isPlayerInAllowedArea = false;
            }

            // テスト用：キーボードのEキーでもダブルプレス処理を呼び出す
            if (Input.GetKeyDown(KeyCode.E))
            {
                ProcessDoublePress();
            }
        }

        /// <summary>
        /// ダブルプレスと判定された際に対象オブジェクトを移動させる処理
        /// </summary>
        private void ProcessDoublePress()
        {
            // requireTriggerArea が有効の場合、エリア内にいなければ何もしない
            if (requireTriggerArea && !isPlayerInAllowedArea)
                return;

            if (objectA != null && objectB != null)
            {
                objectA.transform.SetPositionAndRotation(
                    objectB.transform.position,
                    objectB.transform.rotation
                );
                Rigidbody rb = objectA.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
