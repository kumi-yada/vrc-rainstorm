using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace org.kumagee
{
    // Shows the local player's current win streak for one game mode on a label.
    //
    // The streak lives in PlayerData under the same per-mode key Solitaire writes
    // ("Solitaire.Streak.<mode>"), so this reads the value rather than tracking it:
    // it survives sessions and every client sees the same number. Nothing here
    // syncs - PlayerData is replicated by the SDK, and the label is refreshed from
    // OnPlayerDataUpdated whenever the key it watches changes.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class StreakText : UdonSharpBehaviour
    {
        [Header("Mode")]
        [Tooltip("Which game mode's streak this label shows. Place one label per mode, matching the table and leaderboard it belongs to.")]
        public SolitaireMode Mode = SolitaireMode.Klondike;

        [Header("Text")]
        [Tooltip("Localized label the streak is written to. Left empty, the LocalizedText on this GameObject is used.")]
        [SerializeField] private LocalizedText label;

        [Tooltip("Localization key placed before the number, translated through LocalizedText, e.g. \"WIN_STREAK\". Left empty for the bare number.")]
        [SerializeField] private string prefixKey = "WIN_STREAK";

        private void Start()
        {
            if (label == null) label = GetComponent<LocalizedText>();
            Refresh();
        }

        // PlayerData may only be read once this player's data has loaded, and the
        // saved streak is not there before then - so the first real read waits for
        // this rather than showing a zero that never actually happened.
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.IsValid() || !player.isLocal) return;
            Refresh();
        }

        public override void OnPlayerDataUpdated(VRCPlayerApi player, PlayerData.Info[] infos)
        {
            // Only the local player's own streak is shown, and only this mode's key
            // matters - an unrelated write (language, another mode) never repaints.
            if (player == null || !player.IsValid() || !player.isLocal) return;
            if (!_TouchesStreak(infos)) return;
            Refresh();
        }

        // Public so a table can repaint the label directly after a game ends,
        // without waiting for the PlayerData callback to come back around.
        public void _RefreshStreak()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (label == null) return;
            int streak = ReadStreak();
            // No run to show: blank the label rather than displaying a zero streak.
            if (streak <= 0)
            {
                label.SetPrefixKeyAndSuffix("", "", "");
                return;
            }
            // prefixKey is resolved through LocalizedText, so the word before the
            // number follows the player's language; the number and the bracketed
            // multiplier are not translated and are passed as the raw suffix.
            // The multiplier shown is the one the *next* win will pay: winning
            // raises the run to streak + 1, so that is the step quoted here.
            label.SetPrefixKeyAndSuffix("", prefixKey, " " + streak + " (x" + StreakBonusText(streak + 1) + ")");
        }

        // Mirrors Solitaire._StreakWinMultiplier: the payout bonus a run of the
        // given length adds on a win. The first win pays nothing, then it steps up
        // 0.5x, 1x and 2x, plateauing at 4x from the fifth win on. Kept in sync by
        // hand. Callers pass the run length the next win would reach.
        private string StreakBonusText(int winCount)
        {
            if (winCount <= 1) return "0";
            if (winCount == 2) return "0.5";
            if (winCount == 3) return "1";
            if (winCount == 4) return "2";
            return "4";
        }

        private int ReadStreak()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return 0;
            if (PlayerData.TryGetInt(local, StreakKey(), out int value)) return value;
            return 0;
        }

        private bool _TouchesStreak(PlayerData.Info[] infos)
        {
            if (infos == null) return false;
            string key = StreakKey();
            for (int i = 0; i < infos.Length; i++)
            {
                if (infos[i].Key == key) return true;
            }
            return false;
        }

        private string StreakKey()
        {
            return "Solitaire.Streak." + ModeSuffix();
        }

        private string ModeSuffix()
        {
            if (Mode == SolitaireMode.Spider) return "Spider";
            if (Mode == SolitaireMode.Canfield) return "Canfield";
            return "Klondike";
        }
    }
}
