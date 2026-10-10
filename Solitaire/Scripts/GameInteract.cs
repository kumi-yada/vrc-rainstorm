using UdonSharp;
using UnityEngine;
using VRC.Udon;

public class GameInteract : UdonSharpBehaviour
{
    [Header("Callback")]
    [Tooltip("The game behaviour to forward interact presses to. Assign the Solitaire behaviour here.")]
    public UdonBehaviour Callback;

    [Tooltip("Localization manager used to translate the hover prompt. Falls back to a scene search if empty.")]
    [SerializeField] private LocalizationManager localization;

    // Solitaire hands over the entry fee so the hover prompt advertises the
    // price before anyone presses. Text only.
    private int storedEntryFee;
    private bool running;

    void Start()
    {
        if (localization == null)
        {
            localization = GameObject.Find("Localization").GetComponent<LocalizationManager>();
        }
        if (localization != null)
        {
            localization.Register(this);
        }
    }

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
        this.running = running;
        RefreshPrompt();
    }

    public void _OnLanguageChanged()
    {
        RefreshPrompt();
    }

    private void RefreshPrompt()
    {
        InteractionText = running ? _Text("QUIT") : PromptText();
    }

    private string PromptText()
    {
        return storedEntryFee > 0 ? _Text("START") + " (" + storedEntryFee + " chips)" : _Text("START");
    }

    private string _Text(string key)
    {
        if (localization == null) return key;
        return localization.GetText(key);
    }

    // The interactable moves onto this button so only its mesh lights up on
    // hover instead of the whole table. Presses are forwarded to the game.
    public override void Interact()
    {
        if (Callback == null) return;
        Callback.SendCustomEvent("_OnStartPressed");
    }
}
