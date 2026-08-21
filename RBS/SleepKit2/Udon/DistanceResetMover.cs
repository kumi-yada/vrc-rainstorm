using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace RBS.SleepKit2.Udon
{
    public class DistanceResetMover : UdonSharpBehaviour
    {
        [Header("対象オブジェクト")]
        [Tooltip("objectA が objectB から離れている場合、objectA を objectC の位置に移動します。")]
        [HideInInspector]
        public GameObject objectA;

        [Tooltip("基準となるオブジェクト")]
        [HideInInspector]
        public GameObject objectB;

        [Tooltip("移動先となるオブジェクト")]
        [HideInInspector]
        public GameObject objectC;

        [Header("距離閾値")]
        [Tooltip(
            "objectA と objectB の間の距離がこの値（メートル）以上の場合、objectA を objectC の位置に移動します。"
        )]
        public float distanceThreshold = 1.0f;

        private void Update()
        {
            // objectA、objectB、objectCのいずれかが未設定の場合は処理を中断
            if (objectA == null || objectB == null || objectC == null)
                return;

            // objectAとobjectBの間の距離を計算
            float distance = Vector3.Distance(
                objectA.transform.position,
                objectB.transform.position
            );

            // 距離が閾値を超えている場合、objectAをobjectCの位置に移動
            if (distance > distanceThreshold)
            {
                objectA.transform.SetPositionAndRotation(
                    objectC.transform.position,
                    objectC.transform.rotation
                );
                var rb = objectA.GetComponent<Rigidbody>();
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
