
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UCS;

public class UdonChipsGate : UdonSharpBehaviour
{
    [Header("----------------------System-------------------------")]
    [SerializeField] private UdonChips udonChips;
    [SerializeField] private Animator animator;
    [SerializeField] AudioSource audioSourcePass;
    [SerializeField] AudioSource audioSourceError;
    [SerializeField] GameObject colliderObject;

    [Space(20)]
    [Header("----------------------Money-------------------------")]
    [SerializeField] private float fee = 100;
    [SerializeField] private bool firstTimeOnly = false;
    private bool isFirstTime = true;

    void Start()
    {
        udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (Networking.LocalPlayer.Equals(player))
        {
            if (firstTimeOnly)
            {
                if (isFirstTime)
                {
                    EnterGate();
                    if (udonChips.money >= fee)
                    {
                        isFirstTime = false;
                    }
                }
                return;
            }
            else
            {
                EnterGate();
            }

        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (Networking.LocalPlayer.Equals(player))
        {
            if (!firstTimeOnly)
            {
                colliderObject.SetActive(true);
            }
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "GateEnd");
        }
    }

    public void EnterGate()
    {
        if (udonChips.money < fee)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "GateError");
        }

        if (udonChips.money >= fee)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "GatePass");
            udonChips.money = udonChips.money - fee;
            colliderObject.SetActive(false);
        }
    }

    public void GateError()
    {
        if (animator != null)
        {
            animator.SetInteger("GateParm", 2);
        }

        if (audioSourceError != null)
        {
            audioSourceError.Play();
        }
    }

    public void GatePass()
    {
        if (animator != null)
        {
            animator.SetInteger("GateParm", 1);
        }

        if (audioSourcePass != null)
        {
            audioSourcePass.Play();
        }
    }

    public void GateEnd()
    {
        if (animator != null)
        {
            animator.SetInteger("GateParm", 0);
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
            Debug.LogWarning("UdonChipsGate: UdonChipsがシーンに配置されていません");
        }
        else
        {
            udonChips = foundObject.GetComponent<UdonChips>();
            Debug.Log("UdonChipsGate: UdonChipsを自動設定しました");
        }
    }
#endif

}
