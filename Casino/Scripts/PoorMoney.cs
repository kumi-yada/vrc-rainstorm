using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UCS;

public class PoorMoney : UdonSharpBehaviour
{
    [SerializeField] private UdonChips udonChips;
    [SerializeField] private float maxMoney = 1000f;

    void Start()
    {
        if (udonChips == null)
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
        }
    }

    void Update()
    {
        bool canGain = udonChips.money < maxMoney;
        InteractionText = canGain ? "Take Money" : "Only for the poor (< " + maxMoney + ")";
    }

    public override void Interact()
    {
        if (udonChips.money >= maxMoney)
        {
            return;
        }

        udonChips.money += maxMoney - udonChips.money;
    }
}
