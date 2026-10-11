using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using VRC.Udon;

public class Settings : UdonSharpBehaviour
{
    public GameObject bgm;
    public GameObject sfx;
    public GameObject postProcessing;

    public Toggle bgmToggle;
    public Toggle sfxToggle;
    public Toggle postProcessingToggle;

    private bool dataReady = false;

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!player.isLocal) return;

        dataReady = true;

        if (PlayerData.TryGetBool(player, "bgm", out bool bgmOn))
        {
            ApplyBGM(bgmOn);
        }
        if (PlayerData.TryGetBool(player, "sfx", out bool sfxOn))
        {
            ApplySFX(sfxOn);
        }
        if (PlayerData.TryGetBool(player, "postProcessing", out bool ppOn))
        {
            ApplyPostProcessing(ppOn);
        }
    }

    public void OnBGMChanged()
    {
        if (bgmToggle == null) return;
        ApplyBGM(bgmToggle.isOn);
        SaveBGM();
    }

    public void OnSFXChanged()
    {
        if (sfxToggle == null) return;
        ApplySFX(sfxToggle.isOn);
        SaveSFX();
    }

    public void OnPostProcessingChanged()
    {
        if (postProcessingToggle == null) return;
        ApplyPostProcessing(postProcessingToggle.isOn);
        SavePostProcessing();
    }

    private void ApplyBGM(bool on)
    {
        if (bgmToggle != null) bgmToggle.SetIsOnWithoutNotify(on);
        if (bgm != null) bgm.SetActive(on);
    }

    private void ApplySFX(bool on)
    {
        if (sfxToggle != null) sfxToggle.SetIsOnWithoutNotify(on);
        if (sfx != null) sfx.SetActive(on);
    }

    private void ApplyPostProcessing(bool on)
    {
        if (postProcessingToggle != null) postProcessingToggle.SetIsOnWithoutNotify(on);
        if (postProcessing != null) postProcessing.SetActive(on);
    }

    private void SaveBGM()
    {
        if (!dataReady || bgmToggle == null) return;
        PlayerData.SetBool("bgm", bgmToggle.isOn);
    }

    private void SaveSFX()
    {
        if (!dataReady || sfxToggle == null) return;
        PlayerData.SetBool("sfx", sfxToggle.isOn);
    }

    private void SavePostProcessing()
    {
        if (!dataReady || postProcessingToggle == null) return;
        PlayerData.SetBool("postProcessing", postProcessingToggle.isOn);
    }
}
