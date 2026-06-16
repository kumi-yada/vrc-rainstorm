
using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LightningBolt : UdonSharpBehaviour
{
    [Header("Bolt Appearance")]
    public LineRenderer lineRenderer;
    public LineRenderer[] branchRenderers;

    public int segments = 20;
    public float boltWidth = 2f;
    public float branchWidthMultiplier = 0.5f;
    public float jitterAmount = 3f;

    [Header("Color & Fade")]
    public Color boltColor = new Color(0.8f, 0.92f, 1f);
    public float fadeInTime = 0.05f;
    public float holdTime = 0.15f;
    public float fadeOutTime = 0.25f;

    [Header("Branches")]
    [Range(0f, 1f)]
    public float branchChance = 0.25f;
    public int maxBranches = 3;

    private bool isAnimating = false;
    private float animTimer = 0f;
    private float totalDuration = 0f;

    private void Start()
    {
        totalDuration = fadeInTime + holdTime + fadeOutTime;

        if (lineRenderer != null)
        {
            lineRenderer.startWidth = boltWidth;
            lineRenderer.endWidth = boltWidth * 0.3f;
            lineRenderer.startColor = boltColor;
            lineRenderer.endColor = boltColor;
            lineRenderer.enabled = false;
        }

        foreach (LineRenderer br in branchRenderers)
        {
            if (br != null)
            {
                br.startWidth = boltWidth * branchWidthMultiplier;
                br.endWidth = boltWidth * branchWidthMultiplier * 0.3f;
                br.startColor = boltColor;
                br.endColor = boltColor;
                br.enabled = false;
            }
        }
    }

    public void Initialize(Vector3 start, Vector3 end)
    {
        GenerateBolt(start, end);

        isAnimating = true;
        animTimer = 0f;

        SetAlpha(0f);

        if (lineRenderer != null) lineRenderer.enabled = true;
        foreach (LineRenderer br in branchRenderers)
        {
            if (br != null) br.enabled = true;
        }
    }

    private void GenerateBolt(Vector3 start, Vector3 end)
    {
        Vector3 dir = (end - start).normalized;
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.99f)
            up = Vector3.forward;
        Vector3 right = Vector3.Cross(dir, up).normalized;
        Vector3 forward = Vector3.Cross(right, dir).normalized;

        float length = Vector3.Distance(start, end);

        Vector3[] positions = new Vector3[segments + 1];
        positions[0] = start;
        positions[segments] = end;

        for (int i = 1; i < segments; i++)
        {
            float t = (float)i / segments;
            Vector3 basePos = Vector3.Lerp(start, end, t);
            float displacementScale = 4f * t * (1f - t);

            float jitterRight = Random.Range(-jitterAmount, jitterAmount) * displacementScale;
            float jitterForward = Random.Range(-jitterAmount, jitterAmount) * displacementScale;

            positions[i] = basePos + right * jitterRight + forward * jitterForward;
        }

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = segments + 1;
            lineRenderer.SetPositions(positions);
        }

        int branchCount = 0;
        int branchIndex = 0;

        for (int i = 2; i < segments - 1 && branchCount < maxBranches && branchIndex < branchRenderers.Length; i++)
        {
            if (Random.value < branchChance)
            {
                LineRenderer br = branchRenderers[branchIndex];
                if (br != null)
                {
                    Vector3 branchStart = positions[i];

                    Vector3 branchDir = dir + new Vector3(
                        Random.Range(-0.5f, 0.5f),
                        Random.Range(-0.3f, 0.3f),
                        Random.Range(-0.5f, 0.5f)
                    );
                    branchDir.Normalize();

                    float branchLen = Random.Range(0.3f, 0.6f) * length;
                    Vector3 branchEnd = branchStart + branchDir * branchLen;

                    int branchSegs = Mathf.Max(3, segments / 3);
                    Vector3[] branchPositions = new Vector3[branchSegs + 1];
                    branchPositions[0] = branchStart;
                    branchPositions[branchSegs] = branchEnd;

                    for (int j = 1; j < branchSegs; j++)
                    {
                        float t = (float)j / branchSegs;
                        Vector3 basePos = Vector3.Lerp(branchStart, branchEnd, t);
                        float ds = 4f * t * (1f - t);
                        float jr = Random.Range(-jitterAmount * 0.5f, jitterAmount * 0.5f) * ds;
                        float jf = Random.Range(-jitterAmount * 0.5f, jitterAmount * 0.5f) * ds;
                        branchPositions[j] = basePos + right * jr + forward * jf;
                    }

                    br.positionCount = branchSegs + 1;
                    br.SetPositions(branchPositions);

                    branchCount++;
                    branchIndex++;
                }
            }
        }
    }

    private void Update()
    {
        if (!isAnimating) return;

        animTimer += Time.deltaTime;

        if (animTimer >= totalDuration)
        {
            SetAlpha(0f);
            Destroy(gameObject);
            return;
        }

        float alpha;
        if (animTimer < fadeInTime)
        {
            alpha = animTimer / fadeInTime;
        }
        else if (animTimer < fadeInTime + holdTime)
        {
            alpha = 1f;
        }
        else
        {
            float fadeElapsed = animTimer - fadeInTime - holdTime;
            alpha = 1f - (fadeElapsed / fadeOutTime);
        }

        SetAlpha(alpha);
    }

    private void SetAlpha(float alpha)
    {
        Color c = boltColor;
        c.a = alpha;

        if (lineRenderer != null)
        {
            lineRenderer.startColor = c;
            lineRenderer.endColor = c;
        }

        foreach (LineRenderer br in branchRenderers)
        {
            if (br != null)
            {
                br.startColor = c;
                br.endColor = c;
            }
        }
    }
}
