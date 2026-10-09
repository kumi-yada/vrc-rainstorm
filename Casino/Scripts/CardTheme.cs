
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.Udon;
using org.kumagee;
using UCS;

public class CardTheme : UdonSharpBehaviour
{
    public const string UnlockedKey = "_RAINSTORM/CARD_THEME_UNLOCKED";
    public const string EquippedKey = "_RAINSTORM/CARD_THEME_EQUIPPED";

    [Tooltip("Card back design sold by this theme.")]
    [SerializeField] private CardBack cardBack;

    [Tooltip("Price in chips.")]
    [SerializeField] private int price = 5000;

    [Tooltip("Button the player clicks to buy or equip this card back.")]
    [SerializeField] private Button buyButton;

    [Tooltip("Button label. Text is managed automatically.")]
    [SerializeField] private TextMeshProUGUI buttonLabel;

    [Tooltip("Chips wallet used to pay for the card back.")]
    [SerializeField] private UdonChips udonChips;

    private bool _dataRestored = false;
    private int _unlockedFlags = 1 << (int)CardBack.White;
    private int _equipped = (int)CardBack.White;

    void Start()
    {
        if (udonChips == null)
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
        }

        Refresh();
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

        if (PlayerData.TryGetInt(player, EquippedKey, out int equipped))
        {
            _equipped = equipped;
        }

        _dataRestored = true;
        Refresh();
    }

    public void OnBuyClick()
    {
        if (!_dataRestored)
        {
            return;
        }

        int backIndex = (int)cardBack;
        int mask = 1 << backIndex;

        if ((_unlockedFlags & mask) == 0)
        {
            if (udonChips == null || udonChips.money < price)
            {
                return;
            }

            udonChips.money -= price;
            _unlockedFlags |= mask;
            PlayerData.SetInt(UnlockedKey, _unlockedFlags);
            Refresh();
        }
        else
        {
            PlayerData.SetInt(EquippedKey, backIndex);
            _BroadcastEquipped(backIndex);
            _NotifyDecks();
        }
    }

    // The equipped back lives on the player's own deck (a PlayerObject), so update
    // every deck this player carries. DeckManager._RefreshCardBack reads the freshly
    // written PlayerData and syncs it to the other clients.
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

    private void _BroadcastEquipped(int equipped)
    {
        Transform parent = transform.parent;
        if (parent == null)
        {
            return;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            CardTheme theme = parent.GetChild(i).GetComponent<CardTheme>();
            if (theme != null)
            {
                theme._SetEquipped(equipped);
            }
        }
    }

    public void _SetEquipped(int equipped)
    {
        _equipped = equipped;
        Refresh();
    }

    private void Refresh()
    {
        int mask = 1 << (int)cardBack;
        bool unlocked = (_unlockedFlags & mask) != 0;
        bool equipped = unlocked && _equipped == (int)cardBack;

        if (buyButton != null)
        {
            buyButton.interactable = !equipped;
        }

        if (buttonLabel == null)
        {
            return;
        }

        buttonLabel.color = equipped ? Color.gray : Color.white;

        if (!unlocked)
        {
            buttonLabel.text = "Buy (" + price + ")";
        }
        else if (equipped)
        {
            buttonLabel.text = "Equipped";
        }
        else
        {
            buttonLabel.text = "Use";
        }
    }
}
