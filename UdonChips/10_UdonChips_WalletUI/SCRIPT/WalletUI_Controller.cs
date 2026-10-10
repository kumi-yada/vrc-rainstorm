
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System.Collections.Generic;

public class WalletUI_Controller : UdonSharpBehaviour
{
    [HideInInspector] public GameObject[] plateObjects; // プレートのオブジェクト
    [HideInInspector] public bool isWalletUIActive = false; // WalletUIがアクティブかどうか

    private void OnEnable()
    {
        foreach (GameObject plate in plateObjects)
        {
            //すべてをアクティブに
            plate.SetActive(true);
        }
    }

    private void OnDisable()
    {
        foreach (GameObject plate in plateObjects)
        {
            //すべてを非アクティブに
            plate.SetActive(false);
        }
    }

    public void AddPlateObject(GameObject plateObject)
    {
        // プレートオブジェクトを配列に追加
        GameObject[] newPlateObjects = new GameObject[plateObjects.Length + 1];
        for (int i = 0; i < plateObjects.Length; i++)
        {
            newPlateObjects[i] = plateObjects[i];
        }
        newPlateObjects[plateObjects.Length] = plateObject;
        plateObjects = newPlateObjects;

        // 新しいプレートオブジェクトをアクティブにする
        plateObject.SetActive(true);
    }
}
