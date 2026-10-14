
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// Local, non-persisted toggle that activates/deactivates a set of game objects.
// Wire the Toggle's OnValueChanged to _OnValueChanged (SendCustomEvent); the
// method reads the current isOn state and applies it to every target object.
public class ObjectToggle : UdonSharpBehaviour
{
    [Tooltip("Game objects activated/deactivated by this toggle.")]
    [SerializeField] private GameObject[] objects;

    [Tooltip("Optional toggle to read state from. Defaults to GetComponent<Toggle>().")]
    [SerializeField] private Toggle toggle;

    private void Start()
    {
        if (toggle == null)
        {
            toggle = GetComponent<Toggle>();
        }

        if (toggle != null)
        {
            SetActive(toggle.isOn);
        }
    }

    public void _OnValueChanged()
    {
        if (toggle == null)
        {
            toggle = GetComponent<Toggle>();
        }

        if (toggle == null)
        {
            return;
        }

        SetActive(toggle.isOn);
    }

    public void SetActive(bool active)
    {
        if (objects == null)
        {
            return;
        }

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
            {
                objects[i].SetActive(active);
            }
        }
    }
}
