
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class NightToggle : UdonSharpBehaviour
{
    [SerializeField] private GameObject lightObject;

    void Start()
    {
        SetNight(true);
    }

    public void ToggleNight()
    {
        Debug.Log("Toggling night mode");
        bool isOff = RenderSettings.ambientIntensity < 0.1f;
        SetNight(!isOff);
    }

    private void SetNight(bool isNight)
    {
        RenderSettings.ambientIntensity = isNight ? 0f : 0.3f;
        RenderSettings.skybox.SetFloat("_Exposure", isNight ? 0f : 0.3f);
        lightObject.SetActive(!isNight);
    }
}
