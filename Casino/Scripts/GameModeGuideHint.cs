using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

// Floating hint above each game table pointing at the game mode guide toggle.
// Shows while the local player is inside the table's play area, and keeps
// showing on subsequent visits until the player opens the game mode guide.
// Purely local visual: persisted per-player via PlayerData, no syncing.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class GameModeGuideHint : UdonSharpBehaviour
{
    [Tooltip("Hint object toggled while the hint is shown. Assign a child object, not this GameObject.")]
    [SerializeField] private GameObject hintObject;

    [Tooltip("Unique identifier for this table (e.g. \"Klondike\", \"Spider\", \"Canfield\"). Used to build the per-table persistence key.")]
    [SerializeField] private string tableId = "Table";

    [Tooltip("The game mode guide canvas. When this becomes active (player opened the guide), the hint is permanently dismissed for this table.")]
    [SerializeField] private GameObject guideCanvas;

    private bool _dataRestored = false;
    private bool _hasSeenHint = false;
    private bool _playerInArea = false;

    private string PersistenceKey => "_RAINSTORM/GAME_MODE_GUIDE_HINT_SEEN_" + tableId;

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid() || !player.isLocal)
        {
            return;
        }

        _hasSeenHint = PlayerData.HasKey(player, PersistenceKey);
        _dataRestored = true;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid() || !player.isLocal)
        {
            return;
        }

        _playerInArea = true;
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid() || !player.isLocal)
        {
            return;
        }

        _playerInArea = false;
    }

    void Update()
    {
        if (!_dataRestored)
        {
            return;
        }

        // Once the player opens the game guide, permanently dismiss the hint
        if (!_hasSeenHint && guideCanvas != null && guideCanvas.activeSelf)
        {
            PlayerData.SetBool(PersistenceKey, true);
            _hasSeenHint = true;
        }

        bool showHint = _playerInArea && !_hasSeenHint;

        if (hintObject != null)
        {
            hintObject.SetActive(showHint);
        }
    }
}
