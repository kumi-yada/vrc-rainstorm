using UdonSharp;
using UnityEngine;
using VRC.Udon;

public class GameInteract : UdonSharpBehaviour
{
    [Header("Callback")]
    [Tooltip("The game behaviour to forward interact presses to. Assign the Solitaire behaviour here.")]
    public UdonBehaviour Callback;

    // The interactable moves onto this button so only its mesh lights up on
    // hover instead of the whole table. Presses are forwarded to the game.
    public override void Interact()
    {
        if (Callback == null) return;
        Callback.SendCustomEvent("_OnStartPressed");
    }
}