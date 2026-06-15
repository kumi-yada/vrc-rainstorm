using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using QuickBrown.LuraSwitch;

namespace QuickBrown.LuraSwitch
{
    /// <summary>
    /// SwitchBase の Targets に設定されたオブジェクトをリスポーンさせる機能拡張
    /// </summary>
    public class SwitchFunction_Respawn : UdonSharpBehaviour
    {
        #region フィールド

        [Header("Source")]
        [Tooltip("リスポーン対象は SwitchBase の Targets を使います（ExternalModeからこのUdonBehaviourを指定して呼ぶ想定）")]
        [SerializeField] private SwitchBase sourceSwitch;

        [Header("Udon Respawn")]
        [Tooltip("対象にUdonBehaviourが付いている場合、SendCustomNetworkEventで呼ぶイベント名")]
        [SerializeField] private string udonRespawnNetworkEventName = "Respawn";

        private Vector3[] _initialPositions;
        private Quaternion[] _initialRotations;

        private VRCObjectSync[] _objectSyncs_vrc;
        private UdonBehaviour[][] _udonBehaviours;

        public void Respawn() => RespawnAllObjects();

        #endregion



        #region 初期化

        /// <summary>
        /// 初期化処理
        /// </summary>
        public void Start()
        {
            InitializeCaches();
        }

        /// <summary>
        /// 対象オブジェクトの初期状態をキャッシュ
        /// </summary>
        private void InitializeCaches()
        {
            var objs = GetTargetObjects();
            var length = objs != null ? objs.Length : 0;

            _initialPositions = new Vector3[length];
            _initialRotations = new Quaternion[length];
            _objectSyncs_vrc = new VRCObjectSync[length];
            _udonBehaviours = new UdonBehaviour[length][];

            for (var i = 0; i < length; i++)
            {
                var obj = objs[i];
                if (obj == null)
                {
                    _initialPositions[i] = Vector3.zero;
                    _initialRotations[i] = Quaternion.identity;
                    _objectSyncs_vrc[i] = null;
                    _udonBehaviours[i] = null;
                    continue;
                }

                var t = obj.transform;
                _initialPositions[i] = t.position;
                _initialRotations[i] = t.rotation;
                _objectSyncs_vrc[i] = obj.GetComponent<VRCObjectSync>();
                _udonBehaviours[i] = obj.GetComponents<UdonBehaviour>();
            }
        }

        /// <summary>
        /// SwitchBase から対象オブジェクトを取得
        /// </summary>
        /// <returns>対象オブジェクト配列</returns>
        private GameObject[] GetTargetObjects()
        {
            if (sourceSwitch == null)
            {
                return null;
            }

            return sourceSwitch.Targets;
        }

        #endregion

        #region リスポーン処理

        /// <summary>
        /// すべての対象オブジェクトをリスポーン
        /// </summary>
        private void RespawnAllObjects()
        {
            var targetObjects = GetTargetObjects();
            if (targetObjects == null)
            {
                return;
            }

            var localPlayer = Networking.LocalPlayer;
            if (localPlayer == null)
            {
                return;
            }

            var length = targetObjects.Length;
            if (_initialPositions == null || _initialRotations == null || _objectSyncs_vrc == null || _udonBehaviours == null
                || _initialPositions.Length != length || _initialRotations.Length != length || _objectSyncs_vrc.Length != length || _udonBehaviours.Length != length)
            {
                InitializeCaches();
            }

            for (var i = 0; i < length; i++)
            {
                var obj = targetObjects[i];
                if (obj == null)
                {
                    continue;
                }

                if (!Networking.IsOwner(obj))
                {
                    Networking.SetOwner(localPlayer, obj);
                }

                var vrcSync = _objectSyncs_vrc != null ? _objectSyncs_vrc[i] : null;
                if (vrcSync != null)
                {
                    vrcSync.Respawn();
                    continue;
                }

                var udons = _udonBehaviours != null ? _udonBehaviours[i] : null;
                if (udons != null && udons.Length > 0)
                {
                    var eventName = string.IsNullOrEmpty(udonRespawnNetworkEventName) ? "Respawn" : udonRespawnNetworkEventName;

                    for (var j = 0; j < udons.Length; j++)
                    {
                        var u = udons[j];
                        if (u == null)
                        {
                            continue;
                        }

                        u.SendCustomNetworkEvent(NetworkEventTarget.All, eventName);
                    }

                    continue;
                }

                var t = obj.transform;
                t.SetPositionAndRotation(_initialPositions[i], _initialRotations[i]);

                var rb = (Rigidbody)obj.GetComponent(typeof(Rigidbody));
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        #endregion
    }
}
