using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using UCS;

// Floating sign above the PoorMoney coin. Shows while the local player's
// balance is below the Klondike entry fee (Solitaire prefab EntryFee = 200),
// pointing broke players at the free payout. Purely local visual: the wallet
// balance is per-player, so no syncing is involved.
// Once the player has taken money from PoorMoney, the sign never shows again.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class PoorMoneySign : UdonSharpBehaviour
{
    [Tooltip("Chips wallet holding the local player's balance. Falls back to a scene search if empty.")]
    [SerializeField] private UdonChips udonChips;

    [Tooltip("Show the sign while the balance is below this amount (the Klondike entry fee).")]
    [SerializeField] private float minimumMoney = 200f;

    [Tooltip("Sign object toggled while the sign is shown. Assign a child object, not this GameObject.")]
    [SerializeField] private GameObject signObject;

    private bool _dataRestored = false;
    private bool _hasTakenMoney = false;

    void Start()
    {
        if (udonChips == null)
        {
            GameObject chips = GameObject.Find("UdonChips");
            if (chips != null)
            {
                udonChips = chips.GetComponent<UdonChips>();
            }
        }
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid() || !player.isLocal)
        {
            return;
        }

        _hasTakenMoney = PlayerData.HasKey(player, PoorMoney.LastTakeKey);
        _dataRestored = true;
    }

    void Update()
    {
        if (udonChips == null || !_dataRestored)
        {
            return;
        }

        bool showSign = !_hasTakenMoney && udonChips.money < minimumMoney;

        if (signObject != null)
        {
            signObject.SetActive(showSign);
        }
    }
}
