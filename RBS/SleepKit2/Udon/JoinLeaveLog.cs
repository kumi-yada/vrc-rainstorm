using System; // DateTime用
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace RBS.SleepKit2.Udon
{
    public class JoinLeaveLog : UdonSharpBehaviour
    {
        [Header("ログのPrefab（TMP_Text を含むもの）")]
        [HideInInspector]
        public GameObject logPrefab;

        [Header("Prefabの生成先となる親オブジェクト")]
        [HideInInspector]
        public Transform logParent;

        [Header("ログの表示色設定")]
        public Color joinColor = Color.green;
        public Color leaveColor = Color.red;
        public Color rejoinColor = Color.yellow;

        // 最大同時参加人数（適宜調整してください）
        private const int MAX_RECORDS = 80;

        // プレイヤーの退出記録用：PlayerName と退出時刻
        private string[] leftPlayerNames = new string[MAX_RECORDS];
        private float[] leftTimes = new float[MAX_RECORDS];

        void Start()
        {
            // 配列を初期化（null を未使用の印として設定）
            for (int i = 0; i < MAX_RECORDS; i++)
            {
                leftPlayerNames[i] = null;
                leftTimes[i] = 0f;
            }
        }

        /// <summary>
        /// プレイヤー参加時のコールバック
        /// </summary>
        /// <param name="player">参加したプレイヤー</param>
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player == null)
                return;

            string playerName = player.displayName;
            bool isRejoin = false;

            // 退出記録にプレイヤー名があるかチェック
            for (int i = 0; i < MAX_RECORDS; i++)
            {
                if (leftPlayerNames[i] != null && leftPlayerNames[i] == playerName)
                {
                    // 退出からの再参加か（60秒以内なら再参加）
                    if (Time.time - leftTimes[i] < 60f)
                    {
                        isRejoin = true;
                    }

                    // 使用済みの記録はクリア
                    leftPlayerNames[i] = null;
                    leftTimes[i] = 0f;
                    break;
                }
            }

            // 時刻の取得（HH:mm:ss形式）
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            // ログ用のPrefabを生成し、親に設定
            GameObject instance = Instantiate(logPrefab);
            instance.transform.SetParent(logParent, false);

            // TMP_Text を取得してテキストと色を更新
            TMP_Text tmp = instance.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                if (isRejoin)
                {
                    tmp.text = $"{timestamp} [rejoin] {playerName}";
                    tmp.color = rejoinColor;
                }
                else
                {
                    tmp.text = $"{timestamp} [join] {playerName}";
                    tmp.color = joinColor;
                }
            }
        }

        /// <summary>
        /// プレイヤー退出時のコールバック
        /// </summary>
        /// <param name="player">退出したプレイヤー</param>
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player == null)
                return;

            string playerName = player.displayName;

            // 時刻の取得（HH:mm:ss形式）
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            // ログ用のPrefabを生成し、親に設定
            GameObject instance = Instantiate(logPrefab);
            instance.transform.SetParent(logParent, false);

            // TMP_Text を取得してテキストと色を更新
            TMP_Text tmp = instance.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = $"{timestamp} [leave] {playerName}";
                tmp.color = leaveColor;
            }

            // 退出記録として、空いているスロットに記録を残す
            for (int i = 0; i < MAX_RECORDS; i++)
            {
                if (leftPlayerNames[i] == null)
                {
                    leftPlayerNames[i] = playerName;
                    leftTimes[i] = Time.time;
                    break;
                }
            }
        }
    }
}
