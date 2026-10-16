using UdonSharp;
using UnityEngine;
using VRC.Udon;

// A generic interact target. A press is forwarded to a callback behaviour, and an
// optional confirmation canvas can be put in front of that press. The hover text
// is owned by the callback, which builds it and hands it over through
// _SetInteractionText; this component only stores and displays it.
public class GameInteract : UdonSharpBehaviour
{
    [Header("Callback")]
    [Tooltip("The behaviour a confirmed press is forwarded to. Assign the Solitaire behaviour here.")]
    public UdonBehaviour Callback;

    [Tooltip("Custom event sent to Callback when a press is confirmed (or immediately, when there is no confirmation canvas).")]
    public string CallbackEvent = "";

    [Header("Confirmation")]
    [Tooltip("Optional canvas shown in place of the action. The callback says when it is needed via _SetConfirm; while it is up a press just toggles it, and its own buttons drive the confirm/cancel flow. Leave unassigned to press straight through.")]
    public GameObject ConfirmCanvas;

    [Header("Visibility")]
    [Tooltip("Optional child that carries the trigger's label, hidden together with the collider by _SetVisible. Leave unassigned to use the first child, which is the label canvas on the Reshuffle prefab.")]
    public GameObject Visual;

    // Whether the next press has to go through ConfirmCanvas first. Owned by the
    // callback, because only it knows whether the current action is reversible.
    private bool confirmRequired;

    // The collider and the label _SetVisible toggles. Resolved together on first
    // use, because a hidden trigger may be asked to show itself before its own
    // Start has run.
    private Collider interactCollider;
    private bool visualResolved;

    // Sets the hover prompt. The callback builds the text, so this only stores it.
    public void _SetInteractionText(string text)
    {
        InteractionText = text;
    }

    // Shows or hides the whole trigger. Only the collider and the label are touched,
    // never this behaviour's own GameObject: deactivating it would also stop
    // _SetVisible from running again to bring it back.
    public void _SetVisible(bool visible)
    {
        ResolveVisual();
        if (interactCollider != null) interactCollider.enabled = visible;
        if (Visual != null) Visual.SetActive(visible);
    }

    private void ResolveVisual()
    {
        if (visualResolved) return;
        visualResolved = true;
        interactCollider = GetComponent<Collider>();
        if (Visual == null && transform.childCount > 0) Visual = transform.GetChild(0).gameObject;
    }

    // Tells the interact whether a press opens ConfirmCanvas. Clears the canvas
    // whenever confirmation is no longer wanted, so it can never be left up over
    // an action that no longer needs it.
    public void _SetConfirm(bool required)
    {
        confirmRequired = required;
        if (!required && ConfirmCanvas != null) ConfirmCanvas.SetActive(false);
    }

    // The interactable moves onto this button so only its mesh lights up on hover
    // instead of the whole table.
    public override void Interact()
    {
        if (confirmRequired && ConfirmCanvas != null)
        {
            ConfirmCanvas.SetActive(!ConfirmCanvas.activeSelf);
            return;
        }
        Press();
    }

    private void Press()
    {
        if (Callback == null) return;
        Callback.SendCustomEvent(CallbackEvent);
    }
}
