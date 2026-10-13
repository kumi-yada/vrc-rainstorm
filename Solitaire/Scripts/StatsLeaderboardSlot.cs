using UdonSharp;
using UnityEngine;
using TMPro;

namespace org.kumagee
{
    // One row of the stats leaderboard. This is a PlayerObject (add VRCPlayerObject
    // to the RectTransform in the Inspector), so VRChat spawns one per player and
    // the owning StatsLeaderboard can find each player's row via
    // Networking.GetPlayerObjects. The board reads every player's PlayerData and
    // pushes the rank, name, win rate and game count into this row - it holds no
    // state of its own, so it needs neither synced variables nor persistence.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class StatsLeaderboardSlot : UdonSharpBehaviour
    {
        [Header("Row Text")]
        [SerializeField] private TextMeshProUGUI rankText;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI rateText;
        [SerializeField] private TextMeshProUGUI gamesText;

        public void SetEntry(int rank, string playerName, int games, int wins)
        {
            if (rankText != null) rankText.text = rank.ToString();
            if (nameText != null) nameText.text = playerName;
            if (gamesText != null) gamesText.text = games.ToString();
            if (rateText != null) rateText.text = BuildRate(games, wins);
        }

        private string BuildRate(int games, int wins)
        {
            if (games <= 0) return "—";
            int percent = Mathf.RoundToInt((float)wins / games * 100f);
            return percent + "%";
        }
    }
}
