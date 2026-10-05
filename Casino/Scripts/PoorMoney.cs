using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using VRC.Udon;
using UCS;

public class PoorMoney : UdonSharpBehaviour
{
    [SerializeField] private UdonChips udonChips;
    [SerializeField] private float maxMoney = 1000f;

    [Tooltip("Seconds the player must wait before taking money again.")]
    [SerializeField] private float TimeLimit = 60f;

    [Tooltip("Optional label showing the remaining cooldown time.")]
    [SerializeField] private UnityEngine.UI.Text timeLabel;

    private const string LastTakeKey = "_RAINSTORM/POOR_MONEY_LAST_TAKE";

    private bool _dataRestored = false;
    private long _lastTakeTime = 0;

    void Start()
    {
        if (udonChips == null)
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
        }
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid() || !player.isLocal)
        {
            return;
        }

        if (PlayerData.TryGetInt(player, LastTakeKey, out int savedTime))
        {
            _lastTakeTime = savedTime;
        }

        _dataRestored = true;
    }

    private float RemainingCooldown()
    {
        if (_lastTakeTime <= 0)
        {
            return 0f;
        }

        long elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _lastTakeTime;
        float remaining = TimeLimit - elapsed;
        return remaining > 0f ? remaining : 0f;
    }

    void Update()
    {
        bool canGain = udonChips.money < maxMoney;

        if (!canGain)
        {
            InteractionText = "Only for the poor (< " + maxMoney + ")";
            return;
        }

        float remaining = RemainingCooldown();

        if (remaining > 0f)
        {
            int seconds = Mathf.CeilToInt(remaining);
            InteractionText = "Wait " + seconds + "s";

            if (timeLabel != null)
            {
                timeLabel.text = seconds + "s";
            }
        }
        else
        {
            InteractionText = "Take Money";

            if (timeLabel != null)
            {
                timeLabel.text = "";
            }
        }
    }

    public override void Interact()
    {
        if (!_dataRestored)
        {
            return;
        }

        if (udonChips.money >= maxMoney)
        {
            return;
        }

        if (RemainingCooldown() > 0f)
        {
            return;
        }

        udonChips.money += maxMoney - udonChips.money;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _lastTakeTime = now;
        PlayerData.SetInt(LastTakeKey, (int)now);
    }
}
