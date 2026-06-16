
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

    [Tooltip("Skybox exposure during lightning flash")]
    public float skyboxExposure = 1.5f;
    
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

    [Header("Lightning Bolt Visuals")]
    [Tooltip("Prefab for visual lightning bolt (LightningBolt)")]
    public GameObject boltPrefab;

    [Tooltip("Center of the sky area for bolt spawning")]
    public Transform spawnCenter;

    [Tooltip("Horizontal radius for random bolt spawn positions")]
    public float spawnRadius = 40f;

    [Tooltip("Minimum height for bolt start position")]
    public float minHeight = 30f;

    [Tooltip("Maximum height for bolt start position")]
    public float maxHeight = 60f;

    [Tooltip("Probability of spawning visual bolts on a main strike (0-1)")]
    [Range(0f, 1f)]
    public float boltChance = 0.7f;

    [Tooltip("Maximum number of visual bolts per main strike")]
    public int maxBoltsPerStrike = 2;

    [Header("Silent Strikes (visual only, no flash/thunder)")]
    [Tooltip("Probability of a silent bolt between main strikes (0-1)")]
    [Range(0f, 1f)]
    public float silentBoltChance = 0.4f;

    [Tooltip("Minimum interval between silent bolts (seconds)")]
    public float silentBoltMinInterval = 3f;

    [Tooltip("Maximum interval between silent bolts (seconds)")]
    public float silentBoltMaxInterval = 10f;

    // Private variables for flash animation
    private bool isFlashing = false;
    private float flashTimer = 0f;
    private int flashPhase = 0;
    private float skyboxBaseExposure = 0.6f;
    
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

        // Store original skybox exposure and reset it
        if (RenderSettings.skybox != null)
        {
            skyboxBaseExposure = RenderSettings.skybox.GetFloat("_Exposure");
            RenderSettings.skybox.SetFloat("_Exposure", skyboxBaseExposure);
        }

        // Default spawn center to this object if not set
        if (spawnCenter == null)
        {
            spawnCenter = transform;
        }
        
        // Schedule the first lightning strike
        ScheduleNextStrike();

        // Start silent bolt scheduling
        ScheduleSilentStrike();
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
                    if (RenderSettings.skybox != null)
                        RenderSettings.skybox.SetFloat("_Exposure", skyboxExposure);
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
                    if (RenderSettings.skybox != null)
                        RenderSettings.skybox.SetFloat("_Exposure", skyboxExposure * flash2Intensity);
                }
                else
                {
                    lightningLight.intensity = 0f;
                    if (RenderSettings.skybox != null)
                        RenderSettings.skybox.SetFloat("_Exposure", skyboxBaseExposure);
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

        // Spawn visual lightning bolts
        if (boltPrefab != null && Random.value < boltChance)
        {
            int boltCount = Random.Range(1, maxBoltsPerStrike + 1);
            for (int i = 0; i < boltCount; i++)
            {
                SpawnLightningBolt();
            }
        }
        
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

    public void SpawnLightningBolt()
    {
        if (boltPrefab == null || spawnCenter == null) return;

        Vector3 center = spawnCenter.position;

        // Random horizontal offset within spawn radius
        Vector3 offset = new Vector3(
            Random.Range(-spawnRadius, spawnRadius),
            0f,
            Random.Range(-spawnRadius, spawnRadius)
        );

        Vector3 startPos = new Vector3(
            center.x + offset.x,
            Random.Range(minHeight, maxHeight),
            center.z + offset.z
        );

        // Bolt strikes downward with some horizontal spread
        Vector2 endOffset = new Vector2(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ) * Random.Range(10f, 25f);
        float dropDistance = Random.Range(10f, 30f);

        Vector3 endPos = new Vector3(
            startPos.x + endOffset.x,
            startPos.y - dropDistance,
            startPos.z + endOffset.y
        );

        GameObject bolt = Instantiate(boltPrefab, startPos, Quaternion.identity);
        LightningBolt boltScript = bolt.GetComponent<LightningBolt>();
        if (boltScript != null)
        {
            boltScript.Initialize(startPos, endPos);
        }
    }

    public void DoSilentStrike()
    {
        // Spawn visual bolt without flash or thunder
        if (boltPrefab != null && Random.value < silentBoltChance)
        {
            int boltCount = Random.Range(1, 3);
            for (int i = 0; i < boltCount; i++)
            {
                SpawnLightningBolt();
            }
        }

        ScheduleSilentStrike();
    }

    private void ScheduleSilentStrike()
    {
        float nextSilent = Random.Range(silentBoltMinInterval, silentBoltMaxInterval);
        SendCustomEventDelayedSeconds(nameof(DoSilentStrike), nextSilent);
    }
}
