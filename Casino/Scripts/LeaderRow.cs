
using UdonSharp;
using UnityEngine;
using TMPro;

public class LeaderRow : UdonSharpBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI moneyText;

    public void SetEntry(string playerName, string money)
    {
        if (nameText != null)
        {
            nameText.text = playerName;
        }

        if (moneyText != null)
        {
            moneyText.text = money;
        }
    }
}
