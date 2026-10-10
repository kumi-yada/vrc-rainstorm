
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class WalletUI_Tracker : UdonSharpBehaviour
{
    [Header("SystemReference")]
    [SerializeField] private GameObject playerObject; //Playerのオブジェクト
    [SerializeField] private GameObject rotateObject; // 回転させるオブジェクト
    [SerializeField] private WalletUI_Controller walletUI_Controller; // プレートコントローラーの参照
    [SerializeField] public GameObject uiObject;

    [Header("HeightSettings")]
    [SerializeField] private float heightOffset;

    private VRCPlayerApi playerAPI; //PlayerAPIのインスタンス
    private bool isOwn = false; //オーナーかどうか
    private VRCPlayerApi owner;
    private Vector3 headPosition;
    private Vector3 adjustedOffset;


    void Start()
    {
        if (walletUI_Controller != null)
        {
            //オンオフできるように自身を配列に追加
            walletUI_Controller.AddPlateObject(uiObject);

            if (walletUI_Controller != null && walletUI_Controller.isWalletUIActive == false)
            {
                uiObject.SetActive(false);
            }
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
#if UNITY_EDITOR
        return; // エディタでは実行しない
#else
        // LocalPlayerがnullでないことを確認
        if (Networking.LocalPlayer.IsOwner(playerObject))
        {
            isOwn = true;
            owner = Networking.GetOwner(playerObject);
        }
#endif
    }

    public void Update()
    {
#if UNITY_EDITOR
        return; // エディタでは実行しない
#else
        // LocalPlayerがnullでないことを確認
        if (Networking.LocalPlayer == null) return;

        if (isOwn)
        {
            // 自分のオブジェクトの位置を更新
            headPosition = owner.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            // EyeHeightに比例した高さオフセットを計算
            float eyeHeight = headPosition.y - owner.GetPosition().y;
            adjustedOffset = new Vector3(
                0,
                heightOffset * eyeHeight,
                0
            );

            playerObject.transform.position = headPosition + adjustedOffset;

            Vector3 playerRotation = owner.GetRotation().eulerAngles;
            rotateObject.transform.rotation = Quaternion.Euler(playerRotation.x + 90, playerRotation.y + 180, playerRotation.z);
        }
        else
        {
            // ローカルプレイヤーのカメラ位置を取得
            Vector3 headPos = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            // プレイヤーオブジェクトの回転を更新
            rotateObject.transform.LookAt(headPos);
        }
#endif
    }
}


