
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class NightToggle : UdonSharpBehaviour
{
    [SerializeField] private GameObject lightObject;

    void Start()
    {
        
    }

    public void ToggleNight()
    {
        Debug.Log("Toggling night mode");
        bool isOff = RenderSettings.ambientIntensity < 0.1f;
        RenderSettings.ambientIntensity = isOff ? 0.3f : 0f;
        RenderSettings.skybox.SetFloat("_Exposure", isOff ? 0.3f : 0f);
        lightObject.SetActive(isOff);
    }
}
