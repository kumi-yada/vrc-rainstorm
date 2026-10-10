
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UCS;

public class UdonChipsInteractGain : UdonSharpBehaviour
{
    [Header("----------------------System-------------------------")]
    [SerializeField] private UdonChips udonChips;
    [SerializeField] private AudioSource audioSource_ButtonHit;
    [Space(20)]
    [Header("----------------------Reward-------------------------")]
    [SerializeField] private float moneyReward = 0.2f;


    void Start()
    {
        udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
    }

    public override void Interact()
    {
        ButtonPush();
    }


    private void ButtonPush()
    {
        udonChips.money = udonChips.money + moneyReward;

        if (audioSource_ButtonHit != null)
        {
            audioSource_ButtonHit.Play();
        }
    }


#if !COMPILER_UDONSHARP && UNITY_EDITOR
    public void Reset()
    {
        UdonChipsAutoSet();
    }

    public void OnValidate()
    {
        UdonChipsAutoSet();
    }

    public void UdonChipsAutoSet()
    {
        var foundObject = GameObject.Find("UdonChips");
        if (foundObject == null || foundObject.Equals(null))
        {
            Debug.LogWarning("UdonChipsInteractGain: UdonChipsがシーンに配置されていません");
        }
        else
        {
            udonChips = foundObject.GetComponent<UdonChips>();
            Debug.Log("UdonChipsInteractGain: UdonChipsを自動設定しました");
        }
    }
#endif
}
