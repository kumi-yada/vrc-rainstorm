
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.Udon;

// Local-player achievement store.
//
// Achievements are unlocked once and live forever, so they belong in PlayerData
// rather than on a synced behaviour: only the owning player ever needs to read
// them, they change rarely, and they must survive across sessions. All unlocks
// are packed into one int bitmask, one bit per Achievement value, mirroring the
// unlocked-flags pattern in CardTheme.UnlockedKey.
//
// The bitmask is a signed int, so bit 31 is the sign bit and cannot be used. The
// supported range is bits 0..30 (Achievements.MaxAchievements entries). If the
// world ever needs more, add a second key and a second mask rather than widening
// this one.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class Achievements : UdonSharpBehaviour
{
    // PlayerData key holding the local player's unlocked-achievement bitmask.
    public const string UnlockedKey = "_RAINSTORM/ACHIEVEMENTS";

    // Highest usable bit index. Bit 31 is the sign bit of the int mask.
    public const int MaxAchievements = 31;

    [Tooltip("Popup shown to the local player when an achievement is unlocked. Optional.")]
    [SerializeField] private Notification notification;

    [Tooltip("Text placed before the achievement name in the unlock popup.")]
    [SerializeField] private string notificationPrefix = "";

    [Tooltip("Text placed after the achievement name in the unlock popup.")]
    [SerializeField] private string notificationSuffix = "";

    [Tooltip("Localization keys for each Achievement, indexed by the same zero-based order as the enum. " +
             "Empty entries skip the unlock popup. The manager skips localization lookup when a key is blank.")]
    [SerializeField] private string[] nameKeys;

    // PlayerData may only be touched after OnPlayerRestored. Nothing reads or
    // writes the mask until this is true, so an unlock attempted during scene
    // load cannot clobber the saved value with a zero-initialized mask.
    private bool _dataRestored = false;

    // Unlocked-achievement bitmask, bit n = Achievement n.
    private int _unlockedFlags = 0;

    private LocalizationManager _localization;

    private void Start()
    {
        if (notification == null)
        {
            GameObject found = GameObject.Find("Notification");
            if (found != null) notification = found.GetComponent<Notification>();
        }

        GameObject loc = GameObject.Find("Localization");
        if (loc != null) _localization = loc.GetComponent<LocalizationManager>();
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid() || !player.isLocal)
        {
            return;
        }

        if (PlayerData.TryGetInt(player, UnlockedKey, out int flags))
        {
            _unlockedFlags = flags;
        }

        _dataRestored = true;
    }

    // True once the local player's saved achievements have been loaded. Callers
    // that cache a result should wait for this before reading.
    public bool _IsDataRestored()
    {
        return _dataRestored;
    }

    // Raw bitmask. Useful for UI that renders every achievement at once.
    public int _GetUnlockedFlags()
    {
        return _unlockedFlags;
    }

    public bool IsUnlocked(Achievement achievement)
    {
        return (_unlockedFlags & _Mask(achievement)) != 0;
    }

    // Grants an achievement to the local player. Returns true only when the
    // achievement was newly unlocked, so callers can report the same win more
    // than once without double-counting. A no-op before the player's data has
    // been restored.
    public bool Unlock(Achievement achievement)
    {
        if (!_dataRestored)
        {
            return false;
        }

        int mask = _Mask(achievement);
        if ((_unlockedFlags & mask) != 0)
        {
            return false;
        }

        _unlockedFlags |= mask;
        PlayerData.SetInt(UnlockedKey, _unlockedFlags);
        _Announce(achievement);
        return true;
    }

    // int overload so other behaviours can grant an achievement through
    // SendCustomEvent, which cannot carry an enum parameter.
    public bool UnlockById(int achievementId)
    {
        if (achievementId < 0 || achievementId > MaxAchievements)
        {
            return false;
        }

        return Unlock((Achievement)achievementId);
    }

    public bool IsUnlockedById(int achievementId)
    {
        if (achievementId < 0 || achievementId > MaxAchievements)
        {
            return false;
        }

        return IsUnlocked((Achievement)achievementId);
    }

    private int _Mask(Achievement achievement)
    {
        return 1 << (int)achievement;
    }

    private void _Announce(Achievement achievement)
    {
        if (notification == null)
        {
            return;
        }

        string key = _NameKey(achievement);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        string name = _localization != null ? _localization.GetText(key) : key;
        notification.Notify(notificationPrefix + name + notificationSuffix);
    }

    private string _NameKey(Achievement achievement)
    {
        int index = (int)achievement;
        if (nameKeys == null || index < 0 || index >= nameKeys.Length)
        {
            return "";
        }

        return nameKeys[index];
    }
}
