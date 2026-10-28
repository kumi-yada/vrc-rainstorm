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
    [SerializeField] private float maxMoney = 800f;

    [Tooltip("Higher money cap for supporters. Ignored if no supporter list is set.")]
    [SerializeField] private float supporterMaxMoney = 2000f;

    [Tooltip("Seconds the player must wait before taking money again.")]
    [SerializeField] private float TimeLimit = 60f;

    [Tooltip("Optional supporter list. Supporters bypass the cooldown time limit and use supporterMaxMoney.")]
    [SerializeField] private Supporters supporters;

    [Tooltip("Optional label showing the remaining cooldown time.")]
    [SerializeField] private LocalizedText timeLabel;

    [Tooltip("Localization manager used to translate the interact prompt. Falls back to a scene search if empty.")]
    [SerializeField] private LocalizationManager localization;

    [Tooltip("Shared audio pool the take sound is played through.")]
    [SerializeField] private AudioPool audioPool;

    [Tooltip("Index into audioPool's Clips list of the sound played when the player takes money.")]
    [SerializeField] private int takeClipIndex = 3;

    [Header("Floating Bob")]
    [Tooltip("Object to float up and down. Falls back to this GameObject's transform if empty.")]
    [SerializeField] private Transform floatTarget;

    [Tooltip("Float speed multiplier.")]
    [SerializeField] private float floatSpeed = 1f;

    [Tooltip("How far up and down the object travels, in meters.")]
    [SerializeField] private float floatAmount = 0.1f;

    [Tooltip("Spin speed in degrees per second. Zero disables rotation.")]
    [SerializeField] private float rotateSpeed = 90f;

    [Tooltip("Local axis the object spins around.")]
    [SerializeField] private Vector3 rotateAxis = Vector3.up;

    [Tooltip("Scale the object shrinks to while the cooldown is running.")]
    [SerializeField] private Vector3 stoppedScale = new Vector3(0.5f, 0.5f, 0.5f);

    [Tooltip("How fast the object scales toward its target size.")]
    [SerializeField] private float scaleLerpSpeed = 5f;

    public const string LastTakeKey = "_RAINSTORM/POOR_MONEY_LAST_TAKE";

    private bool _dataRestored = false;
    private long _lastTakeTime = 0;

    private Transform _bobTransform;
    private Vector3 _bobStartLocalPos;
    private Vector3 _bobStartScale;

    void Start()
    {
        if (udonChips == null)
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
        }

        if (localization == null)
        {
            localization = GameObject.Find("Localization").GetComponent<LocalizationManager>();
        }

        _bobTransform = floatTarget != null ? floatTarget : transform;
        _bobStartLocalPos = _bobTransform.localPosition;
        _bobStartScale = _bobTransform.localScale;
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

    private bool _IsLocalSupporter()
    {
        if (supporters == null) return false;
        return supporters.IsSupporter(Networking.LocalPlayer);
    }

    private float _MaxMoney()
    {
        if (_IsLocalSupporter())
        {
            return supporterMaxMoney;
        }
        return maxMoney;
    }

    private float RemainingCooldown()
    {
        if (_IsLocalSupporter())
        {
            return 0f;
        }

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
        float remaining = RemainingCooldown();
        float limit = _MaxMoney();
        bool canGain = udonChips.money < limit;

        bool active = remaining <= 0f && canGain;

        if (_bobTransform != null)
        {
            if (active)
            {
                float y = Mathf.Sin(Time.time * floatSpeed) * floatAmount;
                _bobTransform.localPosition = _bobStartLocalPos + Vector3.up * y;

                if (rotateSpeed != 0f)
                {
                    _bobTransform.Rotate(rotateAxis, rotateSpeed * Time.deltaTime, Space.Self);
                }
            }

            Vector3 targetScale = active ? _bobStartScale : stoppedScale;
            _bobTransform.localScale = Vector3.Lerp(_bobTransform.localScale, targetScale, scaleLerpSpeed * Time.deltaTime);
        }


        if (remaining > 0f)
        {
            string time = _FormatTime(remaining);
            InteractionText = _Text("WAIT") + " " + time;

            if (timeLabel != null)
            {
                timeLabel.SetPrefixKeyAndSuffix(time, "", "");
            }
        }
        else
        {
            if (!canGain)
            {
                InteractionText = _Text("ONLY_FOR_LESS_THAN") + " " + limit;
            }
            else
            {
                InteractionText = _Text("TAKE");
            }

            if (timeLabel != null)
            {
                timeLabel.SetPrefixKeyAndSuffix("", "", "");
            }
        }

    }

    private string _Text(string key)
    {
        if (localization == null) return key;
        return localization.GetText(key);
    }

    private string _FormatTime(float seconds)
    {
        int total = Mathf.CeilToInt(seconds);
        int m = total / 60;
        int s = total % 60;
        return (m < 10 ? "0" + m : m.ToString()) + ":" + (s < 10 ? "0" + s : s.ToString());
    }

    public override void Interact()
    {
        if (!_dataRestored)
        {
            return;
        }

        float limit = _MaxMoney();
        if (udonChips.money >= limit)
        {
            return;
        }

        if (RemainingCooldown() > 0f)
        {
            return;
        }

        udonChips.money += limit - udonChips.money;

        if (audioPool != null)
        {
            audioPool.PlayLocalIndex(takeClipIndex, transform.position);
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _lastTakeTime = now;
        PlayerData.SetInt(LastTakeKey, (int)now);
    }
}
