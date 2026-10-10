
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDKBase.Editor.Attributes;

public class WalletUI_Trigger : UdonSharpBehaviour
{
    [HelpBox("日本語：\nこのオブジェクトがアクティブの時はUIを表示、非アクティブの時は非表示になります \n\nEnglish：\nWhen this object is active, the UI will be displayed, and when it is inactive, it will be hidden.")]
    [SerializeField] private WalletUI_Controller walletUI_Controller; // プレートコントローラーの参照

    private void OnEnable()
    {
        if (walletUI_Controller != null && walletUI_Controller.plateObjects != null)
        {
            foreach (GameObject plate in walletUI_Controller.plateObjects)
            {
                if (plate != null)
                {
                    //すべてをアクティブに
                    plate.SetActive(true);
                }
            }
            walletUI_Controller.isWalletUIActive = true;
        }
    }

    private void OnDisable()
    {
        if (walletUI_Controller != null && walletUI_Controller.plateObjects != null)
        {
            foreach (GameObject plate in walletUI_Controller.plateObjects)
            {
                if (plate != null)
                {
                    plate.SetActive(false);
                }
            }
            walletUI_Controller.isWalletUIActive = false;
        }

    }
}
