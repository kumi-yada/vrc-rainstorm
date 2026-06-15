
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace QuickBrown.LuraSwitch
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SwitchBoard_Holder : UdonSharpBehaviour
    {
        [Header("■ 基本設定／Basic Settings")]
        [Tooltip("スナップ先Transform。未設定の場合はこのGameObjectのTransformを使用します")]
        [SerializeField] private Transform snapTransform;

        [Tooltip("Respawnボタン（Interact用Collider）です。\nSwitchBoardがPickupモードで、このHolderから離れている時のみ有効になります")]
        [SerializeField] private Collider respawnInteractCollider;

        [Header("----------System----------")]
        [SerializeField] private float respawnEnableDistanceMeters = 0.05f;

        private SwitchBoard _cachedBoard;
        private bool _isBoardAway;

        public Transform SnapTransform
        {
            get
            {
                if (snapTransform != null)
                {
                    return snapTransform;
                }

                return transform;
            }
        }

        public void Trigger_SnapBoardHere()
        {
            if (_cachedBoard == null)
            {
                return;
            }

            _cachedBoard.Trigger_SnapToHolder(this);

            // Respawnしたら「このHolderからは離れていない」
            _isBoardAway = false;
            UpdateRespawnColliderState();
        }

        public void RegisterBoard(SwitchBoard board)
        {
            _cachedBoard = board;

            // 初期化時は距離で「離れているか」を決める
            _isBoardAway = !IsBoardAtHomeByDistance();
            UpdateRespawnColliderState();
        }

        public void MarkBoardAway(bool away)
        {
            _isBoardAway = away;
            UpdateRespawnColliderState();
        }

        public void NotifyBoardModeChanged()
        {
            UpdateRespawnColliderState();
        }

        public bool IsBoardAtHomeByDistance()
        {
            if (_cachedBoard == null)
            {
                return false;
            }

            Transform snap = SnapTransform;
            if (snap == null)
            {
                return false;
            }

            float d = respawnEnableDistanceMeters;
            if (d < 0.001f)
            {
                d = 0.001f;
            }

            Vector3 boardPos = _cachedBoard.transform.position;
            Vector3 snapPos = snap.position;
            float sqr = (boardPos - snapPos).sqrMagnitude;
            float thresholdSqr = d * d;

            return sqr <= thresholdSqr;
        }

        private void UpdateRespawnColliderState()
        {
            if (respawnInteractCollider == null)
            {
                return;
            }

            if (_cachedBoard == null)
            {
                respawnInteractCollider.enabled = false;
                return;
            }

            if (_cachedBoard.Mode != SwitchBoardMode.Pickup)
            {
                respawnInteractCollider.enabled = false;
                return;
            }

            respawnInteractCollider.enabled = _isBoardAway;
        }
    }
}
