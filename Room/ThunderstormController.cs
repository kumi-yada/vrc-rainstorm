
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;


[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ThunderstormController : UdonSharpBehaviour
{
    [Header("Lightning Flash")]
    [Tooltip("The directional light used for lightning flashes")]
    public Light lightningLight;
    
    [Tooltip("Peak intensity of the lightning flash")]
    public float flashIntensity = 8f;
    
    [Header("Timing")]
    [Tooltip("Minimum time between lightning strikes (seconds)")]
    public float minInterval = 8f;
    
    [Tooltip("Maximum time between lightning strikes (seconds)")]
    public float maxInterval = 25f;
    
    [Header("Thunder Sound (Optional)")]
    [Tooltip("AudioSource for thunder sound effects")]
    public AudioSource thunderAudio;
    
    [Tooltip("Array of thunder sound clips to randomly choose from")]
    public AudioClip[] thunderClips;
    
    [Tooltip("Minimum delay between lightning flash and thunder (seconds)")]
    public float minThunderDelay = 0.3f;
    
    [Tooltip("Maximum delay between lightning flash and thunder (seconds)")]
    public float maxThunderDelay = 3.5f;
    
    // Private variables for flash animation
    private bool isFlashing = false;
    private float flashTimer = 0f;
    private int flashPhase = 0;
    
    // Flash timing constants (double-flash pattern)
    private float flash1Duration = 0.1f;      // First bright flash
    private float flash1GapDuration = 0.05f;  // Brief gap
    private float flash2Duration = 0.08f;     // Second dimmer flash
    private float flash2Intensity = 0.4f;     // Multiplier for second flash
    
    void Start()
    {
        // Initialize the lightning light to zero intensity
        if (lightningLight != null)
        {
            lightningLight.intensity = 0f;
        }
        
        // Schedule the first lightning strike
        ScheduleNextStrike();
    }
    
    void Update()
    {
        if (!isFlashing) return;
        
        flashTimer += Time.deltaTime;
        
        // Flash phase state machine
        switch (flashPhase)
        {
            case 0: // First bright flash
                if (flashTimer < flash1Duration)
                {
                    lightningLight.intensity = flashIntensity;
                }
                else
                {
                    lightningLight.intensity = 0f;
                    flashPhase = 1;
                }
                break;
                
            case 1: // Gap between flashes
                if (flashTimer >= flash1Duration + flash1GapDuration)
                {
                    flashPhase = 2;
                }
                break;
                
            case 2: // Second dimmer flash
                if (flashTimer < flash1Duration + flash1GapDuration + flash2Duration)
                {
                    lightningLight.intensity = flashIntensity * flash2Intensity;
                }
                else
                {
                    lightningLight.intensity = 0f;
                    flashPhase = 3;
                    isFlashing = false;
                }
                break;
        }
    }
    
    public void DoLightningStrike()
    {
        // Start the flash animation
        isFlashing = true;
        flashTimer = 0f;
        flashPhase = 0;
        
        // Schedule thunder sound with random delay
        float thunderDelay = Random.Range(minThunderDelay, maxThunderDelay);
        SendCustomEventDelayedSeconds(nameof(PlayThunder), thunderDelay);
        
        // Schedule the next lightning strike
        ScheduleNextStrike();
    }
    
    public void PlayThunder()
    {
        // Only play if we have audio source and clips
        if (thunderAudio != null && thunderClips != null && thunderClips.Length > 0)
        {
            // Pick a random thunder clip
            int clipIndex = Random.Range(0, thunderClips.Length);
            AudioClip clip = thunderClips[clipIndex];
            
            if (clip != null)
            {
                thunderAudio.clip = clip;
                thunderAudio.Play();
            }
        }
    }
    
    private void ScheduleNextStrike()
    {
        // Schedule next lightning strike at random interval
        float nextInterval = Random.Range(minInterval, maxInterval);
        SendCustomEventDelayedSeconds(nameof(DoLightningStrike), nextInterval);
    }
}
