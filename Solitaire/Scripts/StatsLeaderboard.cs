using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace org.kumagee
{
    // Ranks every player in the instance by a per-mode lifetime stat stored in
    // PlayerData, and reorders their per-player rows to match.
    //
    // Each row is a PlayerObject (see StatsLeaderboardSlot) - VRChat spawns one
    // per player automatically - so this board never has to instantiate anything
    // itself. The order is derived locally from PlayerData, which is identical on
    // every client, so nothing about the ranking has to sync; a resort is just a
    // local reordering of rows whenever a player's records change.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class StatsLeaderboard : UdonSharpBehaviour
    {
        [Header("Mode")]
        [Tooltip("Which game mode's records this board ranks and shows. Place one board per mode.")]
        public SolitaireMode Mode = SolitaireMode.Klondike;

        [Header("Layout")]
        [Tooltip("Scroll View content transform. Must carry a Vertical Layout Group so rows follow hierarchy order. Rows are reparented here on every resort.")]
        public Transform Content;

        private string GamesKey()
        {
            return "Solitaire.Games." + ModeSuffix();
        }

        private string WinsKey()
        {
            return "Solitaire.Wins." + ModeSuffix();
        }

        private string ModeSuffix()
        {
            if (Mode == SolitaireMode.Spider) return "Spider";
            if (Mode == SolitaireMode.Canfield) return "Canfield";
            return "Klondike";
        }

        private void Start()
        {
            _Resort();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            _Resort();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            _Resort();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            _Resort();
        }

        public override void OnPlayerDataUpdated(VRCPlayerApi player, PlayerData.Info[] infos)
        {
            // Only resort when one of the two keys this board ranks by changed,
            // so an unrelated write (language, settings) never triggers a resort.
            if (!_TouchesBoard(infos)) return;
            _Resort();
        }

        private bool _TouchesBoard(PlayerData.Info[] infos)
        {
            if (infos == null) return false;
            string gamesKey = GamesKey();
            string winsKey = WinsKey();
            for (int i = 0; i < infos.Length; i++)
            {
                string key = infos[i].Key;
                if (key == gamesKey || key == winsKey) return true;
            }
            return false;
        }

        private void _Resort()
        {
            int playerCount = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi[] players = new VRCPlayerApi[playerCount];
            VRCPlayerApi.GetPlayers(players);

            VRCPlayerApi[] entryPlayers = new VRCPlayerApi[playerCount];
            StatsLeaderboardSlot[] slots = new StatsLeaderboardSlot[playerCount];
            int[] games = new int[playerCount];
            int[] wins = new int[playerCount];
            int count = 0;

            string gamesKey = GamesKey();
            string winsKey = WinsKey();

            for (int i = 0; i < playerCount; i++)
            {
                VRCPlayerApi player = players[i];
                if (!Utilities.IsValid(player)) continue;

                StatsLeaderboardSlot slot = _FindSlot(player);
                if (slot == null) continue;

                entryPlayers[count] = player;
                slots[count] = slot;
                games[count] = _ReadInt(player, gamesKey);
                wins[count] = _ReadInt(player, winsKey);
                count++;
            }

            int[] order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;

            _Sort(order, entryPlayers, games, wins, count);
            _Apply(order, entryPlayers, slots, games, wins, count);
        }

        private StatsLeaderboardSlot _FindSlot(VRCPlayerApi player)
        {
            GameObject[] objects = Networking.GetPlayerObjects(player);
            if (objects == null) return null;

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null) continue;
                StatsLeaderboardSlot slot = objects[i].GetComponentInChildren<StatsLeaderboardSlot>(true);
                if (slot != null) return slot;
            }
            return null;
        }

        private int _ReadInt(VRCPlayerApi player, string key)
        {
            if (PlayerData.TryGetInt(player, key, out int value)) return value;
            return 0;
        }

        private void _Sort(int[] order, VRCPlayerApi[] players, int[] games, int[] wins, int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                int best = i;
                for (int j = i + 1; j < count; j++)
                {
                    if (_IsBetter(order[j], order[best], players, games, wins))
                    {
                        best = j;
                    }
                }

                if (best != i)
                {
                    int tmp = order[i];
                    order[i] = order[best];
                    order[best] = tmp;
                }
            }
        }

        // True when entry a should rank above entry b: higher win rate first,
        // then more games, then lower player id. The id tie-break keeps the order
        // byte-identical on every client without any of it going over the wire.
        private bool _IsBetter(int a, int b, VRCPlayerApi[] players, int[] games, int[] wins)
        {
            if (games[a] <= 0) return false;
            if (games[b] <= 0) return true;

            long aRate = (long)wins[a] * games[b];
            long bRate = (long)wins[b] * games[a];
            if (aRate != bRate) return aRate > bRate;

            if (games[a] != games[b]) return games[a] > games[b];

            return players[a].playerId < players[b].playerId;
        }

        private void _Apply(int[] order, VRCPlayerApi[] players, StatsLeaderboardSlot[] slots, int[] games, int[] wins, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int index = order[i];
                StatsLeaderboardSlot slot = slots[index];
                if (slot == null) continue;

                Transform row = slot.transform;
                if (Content != null)
                {
                    row.SetParent(Content, false);
                    row.SetSiblingIndex(i);
                }

                slot.SetEntry(i + 1, _PlayerName(players[index]), games[index], wins[index]);
            }
        }

        private string _PlayerName(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player)) return player.displayName;
            return "?";
        }
    }
}
