
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class Leaderboard : UdonSharpBehaviour
{
    [SerializeField] private LeaderRow[] rows;
    [SerializeField] private string moneyFormat = "{0:F0}c";
    [SerializeField] private float refreshInterval = 0.25f;

    private float _nextRefreshTime;

    private void Update()
    {
        if (Time.time < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = Time.time + refreshInterval;
        _Refresh();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        _Refresh();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        _Refresh();
    }

    private void _Refresh()
    {
        VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
        VRCPlayerApi.GetPlayers(players);

        int count = players.Length;
        float[] moneys = new float[count];

        for (int i = 0; i < count; i++)
        {
            moneys[i] = _ReadMoney(players[i]);
        }

        _Sort(players, moneys, count);
        _ApplyRows(players, moneys, count);
    }

    private float _ReadMoney(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid())
        {
            return 0f;
        }

        GameObject[] objects = Networking.GetPlayerObjects(player);
        if (objects == null)
        {
            return 0f;
        }

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] == null)
            {
                continue;
            }

            WalletSync wallet = objects[i].GetComponentInChildren<WalletSync>(true);
            if (wallet != null)
            {
                return wallet.money;
            }
        }

        return 0f;
    }

    private void _Sort(VRCPlayerApi[] players, float[] moneys, int count)
    {
        for (int i = 0; i < count - 1; i++)
        {
            int best = i;
            for (int j = i + 1; j < count; j++)
            {
                if (moneys[j] > moneys[best])
                {
                    best = j;
                }
            }

            if (best != i)
            {
                float tmpMoney = moneys[i];
                moneys[i] = moneys[best];
                moneys[best] = tmpMoney;

                VRCPlayerApi tmpPlayer = players[i];
                players[i] = players[best];
                players[best] = tmpPlayer;
            }
        }
    }

    private void _ApplyRows(VRCPlayerApi[] players, float[] moneys, int count)
    {
        if (rows == null)
        {
            return;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] == null)
            {
                continue;
            }

            if (i < count)
            {
                rows[i].gameObject.SetActive(true);
                rows[i].SetEntry(_PlayerName(players[i]), string.Format(moneyFormat, moneys[i]));
            }
            else
            {
                rows[i].gameObject.SetActive(false);
            }
        }
    }

    private string _PlayerName(VRCPlayerApi player)
    {
        if (player != null && player.IsValid())
        {
            return player.displayName;
        }

        return "?";
    }
}
