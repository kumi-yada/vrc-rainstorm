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
    private int storedEntryFee;

    public void _SetEntryFee(int entryFee)
    {
        storedEntryFee = entryFee;
        RefreshPrompt();
    }

    // Flips the hover prompt between the priced "Start" and "Quit", mirroring the
    // start/quit button label. Solitaire calls this on every refresh of that
    // state so the prompt never advertises a start once a game is underway.
    public void _SetRunning(bool running)
    {
        InteractionText = running ? "Quit" : PromptText();
    }

    private void RefreshPrompt()
    {
        InteractionText = PromptText();
    }

    private string PromptText()
    {
        return storedEntryFee > 0 ? $"Start ({storedEntryFee} coins)" : "Start";
    }

    // The interactable moves onto this button so only its mesh lights up on
    // hover instead of the whole table. Presses are forwarded to the game.
    public override void Interact()
    {
        if (Callback == null) return;
        Callback.SendCustomEvent("_OnStartPressed");
    }
}
