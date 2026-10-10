
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using UCS;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class WalletSync : UdonSharpBehaviour
{
    [UdonSynced] public float money;

    private UdonChips _udonChips;
    private float _lastMoney;
    private bool _first = true;

    private void Start()
    {
        GameObject chips = GameObject.Find("UdonChips");
        if (chips != null)
        {
            _udonChips = chips.GetComponent<UdonChips>();
        }
    }

    private void Update()
    {
        if (_udonChips == null)
        {
            return;
        }

        if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
        {
            return;
        }

        float current = _udonChips.money;
        if (_first || current != _lastMoney)
        {
            _first = false;
            _lastMoney = current;
            money = current;
            RequestSerialization();
        }
    }
}
