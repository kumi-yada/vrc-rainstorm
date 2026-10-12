using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.Udon;
using org.kumagee;

// Per-player dark mode switch. Toggling it changes only the local player's own
// card backs: the shop previews swap to the *_dark sprite locally, and the local
// player's decks re-read the flag and sync it so every other client renders the
// same dark back. Stored in PlayerData (per-player), so it persists and the decks
// restore it on join the same way they restore the equipped card back.
public class DarkToggle : UdonSharpBehaviour
{
    [Tooltip("Label showing the current mode. Text is managed automatically.")]
    [SerializeField] private LocalizedText label;

    [Tooltip("Supporter list that gates the toggle. Non-supporters cannot flip dark mode.")]
    [SerializeField] private Supporters supporters;

    [Tooltip("Optional button disabled for non-supporters.")]
    [SerializeField] private Button button;

    [Tooltip("Label color shown when the player is not a supporter.")]
    [SerializeField] private Color grayColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    private bool _dataRestored = false;
    private bool _dark = false;
    private Color _normalColor;
    private bool _normalColorCaptured = false;
    private bool _lastSupporter = false;
    private bool _supporterKnown = false;

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid() || !player.isLocal)
        {
            return;
        }

        _dataRestored = true;

        if (PlayerData.TryGetBool(player, CardTheme.DarkKey, out bool dark))
        {
            _dark = dark;
        }

        _Apply();
    }

    public override void Interact()
    {
        _Toggle();
    }

    // Wire a UI Button's onClick here (SendCustomEvent), or leave the object
    // collider-driven and use Interact.
    public void ToggleDark()
    {
        _Toggle();
    }

    private void _Toggle()
    {
        if (!_dataRestored)
        {
            return;
        }

        if (!_IsSupporter())
        {
            return;
        }

        _dark = !_dark;
        PlayerData.SetBool(CardTheme.DarkKey, _dark);
        _Apply();
    }

    private bool _IsSupporter()
    {
        if (supporters == null) return false;
        return supporters.IsSupporter(Networking.LocalPlayer);
    }

    // The supporter list loads async, so the gate is re-checked until it settles.
    private void Update()
    {
        bool supporter = _IsSupporter();
        bool changed = !_supporterKnown || supporter != _lastSupporter;
        _lastSupporter = supporter;
        _supporterKnown = true;

        if (changed)
        {
            _RefreshAccess();
        }

        // Once the list is actually loaded, strip dark mode from non-supporters
        // (e.g. a player who lost supporter status after saving dark). Guarded on
        // IsLoaded so the loading window's false "not supporter" can't erase it.
        if (supporters != null && supporters.IsLoaded() && !supporter && _dark)
        {
            _dark = false;
            PlayerData.SetBool(CardTheme.DarkKey, false);
            _Apply();
        }
    }

    private void _RefreshAccess()
    {
        if (label != null && label.TextElement != null)
        {
            if (!_normalColorCaptured)
            {
                _normalColor = label.TextElement.color;
                _normalColorCaptured = true;
            }
            label.TextElement.color = _lastSupporter ? _normalColor : grayColor;
        }

        if (button != null)
        {
            button.interactable = _lastSupporter;
        }

        _RefreshLabel();
    }

    private void _Apply()
    {
        _ApplyThemes();
        _NotifyDecks();
        _RefreshLabel();
    }

    private void _RefreshLabel()
    {
        if (label == null)
        {
            return;
        }

        string suffixKey = _supporterKnown && !_lastSupporter ? "FOR_SUPPORTERS_ONLY" : "";
        label.SetPrefixKeyAndSuffixKey("", !_dark ? "DARK_MODE" : "LIGHT_MODE", suffixKey);
    }

    // Card themes may sit anywhere under the shared shop root, so search the whole
    // subtree. Swapping the preview sprite is local-only, so only this player's
    // view changes.
    private void _ApplyThemes()
    {
        Transform parent = transform.parent;
        if (parent == null)
        {
            return;
        }

        CardTheme[] themes = parent.GetComponentsInChildren<CardTheme>(true);
        if (themes == null)
        {
            return;
        }

        for (int i = 0; i < themes.Length; i++)
        {
            if (themes[i] != null)
            {
                themes[i]._SetDark(_dark);
            }
        }
    }

    // The dark flag lives on the local player's own deck (a PlayerObject), so
    // re-read it on every deck this player carries. DeckManager._RefreshCardBack
    // reads the freshly written PlayerData and syncs it to the other clients.
    private void _NotifyDecks()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;

        GameObject[] objects = Networking.GetPlayerObjects(local);
        if (objects == null) return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] == null) continue;

            DeckManager[] decks = objects[i].GetComponentsInChildren<DeckManager>(true);
            if (decks == null) continue;

            for (int d = 0; d < decks.Length; d++)
            {
                if (decks[d] != null) decks[d]._RefreshCardBack();
            }
        }
    }
}
