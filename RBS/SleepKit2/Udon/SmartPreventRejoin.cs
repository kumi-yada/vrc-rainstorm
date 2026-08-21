using System;
using System.Globalization;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace RBS.SleepKit2.Udon
{
    /// <summary>
    /// 最後に退出したプレイヤーの情報（オーナー・非オーナー問わず）を同期し、
    /// 再参加時にその退出時の位置／Rotationへテレポートする仕組みと、
    /// NightMode設定の永続化を行います。
    /// </summary>
    public class SmartPreventRejoin : UdonSharpBehaviour
    {
        // Persistence 用のキー
        private const string PositionKey = "SMART_PREVENT_REJOIN_POSITION";
        private const string RotationKey = "SMART_PREVENT_REJOIN_ROTATION";
        private const string NightModeKey = "SMART_PREVENT_REJOIN_NIGHTMODE";

        // UdonSynced 変数（最新の退出情報を格納）
        [UdonSynced]
        private string _lastPlayerLeftDate = "";

        [UdonSynced]
        private string _lastLeavePlayerName = "";

        [UdonSynced]
        private Vector3 _lastLeavePlayerPosition = Vector3.zero;

        [UdonSynced]
        private Quaternion _lastLeavePlayerRotation = Quaternion.identity;

        // Inspector で設定するフィールド
        [SerializeField]
        private float _syncInterval = 10f;

        [Header("Night Mode Settings"), Tooltip("NightModeスライダーへの参照")]
        public Slider NightModeSlider;

        [
            Header("Persistence Settings"),
            Tooltip("PersistenceManagerの参照。永続データのキーにこのprefixが付加されます。")
        ]
        public PersistenceManager persistenceManager;

        [Header("Rejoin Threshold (seconds)"), Tooltip("再参加とみなす時間（秒）")]
        public float rejoinThreshold = 180f;

        /// <summary>
        /// Persistence用のキーに、PersistenceManagerのprefixを付加して返します。
        /// </summary>
        private string GetPrefixedKey(string baseKey)
        {
            return (persistenceManager != null ? persistenceManager.prefix : "") + baseKey;
        }

        private void Start()
        {
            SyncPersistenceDataLoop();
        }

        /// <summary>
        /// NightMode設定（および位置／Rotation）を Persistence に保存します。
        /// </summary>
        private void SyncPersistenceDataLoop()
        {
            if (NightModeSlider != null)
            {
                PlayerData.SetFloat(GetPrefixedKey(NightModeKey), NightModeSlider.value);
            }
            PlayerData.SetVector3(GetPrefixedKey(PositionKey), _lastLeavePlayerPosition);
            PlayerData.SetQuaternion(GetPrefixedKey(RotationKey), _lastLeavePlayerRotation);
            SendCustomEventDelayedSeconds(nameof(SyncPersistenceDataLoop), _syncInterval);
        }

        /// <summary>
        /// プレイヤー参加時、短い遅延後に退出情報をチェックし、
        /// 自分自身の情報と一致していればテレポートを試みます。
        /// </summary>
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player == null)
                return;
            if (!player.isLocal)
                return;
            // ネットワーク同期完了を待つために1秒後にチェック
            SendCustomEventDelayedSeconds(nameof(DelayedTeleportCheck), 1f);
        }

        /// <summary>
        /// 同期された退出情報から、再参加許容時間内かどうか判定し、
        /// 自分自身の情報であればテレポートを実行します。
        /// </summary>
        public void DelayedTeleportCheck()
        {
            // 退出情報が存在しなければ何もしない
            if (string.IsNullOrEmpty(_lastPlayerLeftDate))
                return;

            // 自分の名前と記録されている名前が一致する場合のみ
            if (Networking.LocalPlayer.displayName != _lastLeavePlayerName)
                return;

            if (
                DateTime.TryParseExact(
                    _lastPlayerLeftDate,
                    "yyyy-MM-dd-HH-mm-ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime leftTime
                )
            )
            {
                // 再参加許容時間内ならテレポートを実行
                if (DateTime.Now <= leftTime.AddSeconds(rejoinThreshold))
                {
                    Networking.LocalPlayer.TeleportTo(
                        _lastLeavePlayerPosition,
                        _lastLeavePlayerRotation
                    );
                }
            }
        }

        /// <summary>
        /// プレイヤー退出時、オーナーが常に最新の退出情報を記録します。
        /// これにより、オーナー以外のプレイヤーが退出した場合も「最後の１人」の情報として同期されます。
        /// </summary>
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player == null)
                return;
            // この処理はオーナーのみが行います
            if (!Networking.LocalPlayer.IsOwner(gameObject))
                return;

            // 常に最新の退出情報で上書きする
            _lastPlayerLeftDate = DateTime.Now.ToString(
                "yyyy-MM-dd-HH-mm-ss",
                CultureInfo.InvariantCulture
            );
            _lastLeavePlayerName = player.displayName;
            _lastLeavePlayerPosition = player.GetPosition();
            _lastLeavePlayerRotation = player.GetRotation();

            RequestSerialization();
        }
    }
}
