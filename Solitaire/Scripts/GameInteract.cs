using UdonSharp;
using UnityEngine;
using VRC.Udon;

public class GameInteract : UdonSharpBehaviour
{
    [Header("Callback")]
    [Tooltip("The game behaviour to forward interact presses to. Assign the Solitaire behaviour here.")]
    public UdonBehaviour Callback;

    // Solitaire hands over the entry fee so the hover prompt advertises the
    // price before anyone presses. Text only.
    public void _SetEntryFee(int entryFee)
    {
        InteractionText = entryFee > 0 ? $"Start ({entryFee} coins)" : "Start";
    }

    // The interactable moves onto this button so only its mesh lights up on
    // hover instead of the whole table. Presses are forwarded to the game.
    public override void Interact()
    {
        if (Callback == null) return;
        Callback.SendCustomEvent("_OnStartPressed");
    }
}
