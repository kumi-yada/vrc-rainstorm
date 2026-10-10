
using UCS;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class WalletUI : UdonSharpBehaviour
{
    [Header("--------------------------UdonChips--------------------------")]
    [SerializeField] private UdonChips udonChips; //UdonChipsのインスタンス

    [Header("--------------------------Settings / 設定--------------------------")]
    [SerializeField] private float syncTimer = 3.0f; //同期更新の間隔
    [SerializeField] private string format = string.Empty;

    [Header("--------------------------System--------------------------")]
    [HideInInspector][UdonSynced] public float displayMoney;
    [SerializeField] private GameObject playerObject; //Playerのオブジェクト
    [SerializeField] private TextMeshProUGUI moneyText; //TextMeshProのインスタンス

    private VRCPlayerApi playerAPI; //PlayerAPIのインスタンス
    private bool isOwn = false; //オーナーかどうか

    private void Start()
    {
#if UNITY_EDITOR
        return; // エディタでは実行しない
#else
        udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();

        // 初期化時にLocalPlayerが利用可能な場合は所有権を確認
        if (Networking.LocalPlayer.IsOwner(playerObject))
        {
            isOwn = true;
            displayMoney = udonChips.money;

            ApplyText();
            SendCustomEventDelayedSeconds(nameof(UpdateUdonChips), syncTimer);
        }
#endif
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
#if UNITY_EDITOR
        return; // エディタでは実行しない
#else
        // プレイヤーが参加したときに呼ばれる
        if (player.isLocal)
        {
            //自分のやつ
            if (Networking.LocalPlayer.IsOwner(playerObject))
            {
                isOwn = true;

                displayMoney = udonChips.money;
                ApplyText();
                SendCustomEventDelayedSeconds(nameof(UpdateUdonChips), syncTimer);
            }
            else
            {
                // 非オーナーの場合、現在の同期された値でテキストを更新
                moneyText.text = displayMoney.ToString("N0");
            }
        }

        RequestSerialization();
#endif
    }


    public void UpdateUdonChips()
    {
        if (isOwn)
        {
            //UdonChipsの値を更新
            displayMoney = udonChips.money;
            ApplyText();
            RequestSerialization();

            // 次の更新を同期タイマー後に予約
            SendCustomEventDelayedSeconds(nameof(UpdateUdonChips), syncTimer);
        }
    }

    public override void OnDeserialization()
    {
        if (!isOwn)
        {
            // 非オーナーは常に同期された値でテキストを更新
            ApplyText();
        }
    }


    private void ApplyText()
    {
        if (string.IsNullOrEmpty(format))
        {
            format = udonChips.format;
        }

        if (moneyText != null)
        {
            moneyText.text = string.Format(format, displayMoney);
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
            Debug.LogWarning("WalletUI: UdonChipsがシーンに配置されていません");
        }
        else
        {
            udonChips = foundObject.GetComponent<UdonChips>();
            Debug.Log("WalletUI: UdonChipsを自動設定しました");
        }
    }
#endif

}
