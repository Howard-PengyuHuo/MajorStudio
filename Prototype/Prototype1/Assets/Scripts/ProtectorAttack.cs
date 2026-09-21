using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class ProtectorAttack : MonoBehaviour
{
    [Min(0f)] public float minRadius = 0.5f;
    [Min(0f)] public float maxRadius = 12f;
    [Min(0f)] public float startingRadius = 5f;
    [Min(0f)] public float scrollSensitivity = 0.5f;
    [Min(0f)] public float perfectTolerance = 0.25f;
    [Tooltip("Extra world-space contact allowance; does not widen the Perfect scoring band.")]
    [Min(0f)] public float hitPadding = 0.2f;
    [Min(0f)] public float dangerRadius = 3f;
    public LineRenderer lineRenderer;
    [Min(3)] public int circleSegments = 128;
    [FormerlySerializedAs("lineWidth")]
    [Min(0.001f)] public float previewLineWidth = 0.08f;
    [Min(0.001f)] public float attackLineWidth = 0.13f;
    [Min(0.01f)] public float dashLength = 0.3f;
    [Min(0.01f)] public float gapLength = 0.2f;
    [Min(0f)] public float attackFlashDuration = 0.12f;
    [Tooltip("Editable dashed material. The supplied shader supports _DashCount and _DashRatio.")]
    public Material previewMaterial;
    [FormerlySerializedAs("circleMaterial")] public Material attackMaterial;
    public ParticleSystem mouseKillVFX;
    public ParticleSystem perfectKillVFX;
    public GameManager gameManager;
    public MouseSpawner mouseSpawner;
    public UnityEvent<Vector3> onPerfectHit = new UnityEvent<Vector3>();
    [SerializeField] private float currentRadius;
    public float CurrentRadius => currentRadius;
    Vector3[] circlePoints;
    MaterialPropertyBlock lineProperties;
    float flashRadius;
    float flashEndsAt;
    public bool IsAttackFlashing => Time.unscaledTime < flashEndsAt;
    static readonly int DashCount = Shader.PropertyToID("_DashCount");
    static readonly int DashRatio = Shader.PropertyToID("_DashRatio");

    void Awake()
    {
        ValidateSettings();
        if (!lineRenderer) lineRenderer = GetComponent<LineRenderer>();
        if (!lineRenderer) lineRenderer = gameObject.AddComponent<LineRenderer>();
        currentRadius = Mathf.Clamp(startingRadius, minRadius, maxRadius);
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineProperties = new MaterialPropertyBlock();
        DrawCircle();
    }

    void Update()
    {
        if (!gameManager || !gameManager.IsPlaying) return;
        AdjustRadius(Input.mouseScrollDelta.y);
        if (Input.GetMouseButtonDown(2)) ReleaseAttack();
    }

    void LateUpdate() => DrawCircle();

    public void AdjustRadius(float wheelDelta)
    {
        if (!gameManager || !gameManager.IsPlaying) return;
        currentRadius = Mathf.Clamp(currentRadius + wheelDelta * scrollSensitivity, minRadius, maxRadius);
        // A new scroll gesture must show the radius being aimed, not the old flash.
        if (wheelDelta != 0f) flashEndsAt = 0f;
        DrawCircle();
    }

    void DrawCircle()
    {
        if (!lineRenderer) return;
        int segments = Mathf.Max(3, circleSegments);
        bool flashing = IsAttackFlashing;
        float displayRadius = flashing ? flashRadius : currentRadius;
        if (circlePoints == null || circlePoints.Length != segments) circlePoints = new Vector3[segments];
        
        Vector3 center = transform.position;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            circlePoints[i] = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * displayRadius;
        }
        
        lineRenderer.positionCount = segments;
        lineRenderer.widthMultiplier = flashing ? attackLineWidth : previewLineWidth;
        Material material = flashing ? attackMaterial : previewMaterial;
        if (material) lineRenderer.sharedMaterial = material;
        // Whole dash cycles close the seam. World-length settings keep the pattern
        // readable as the circumference changes; material assets are never mutated.
        int dashCount = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.PI * displayRadius / (dashLength + gapLength)));
        lineRenderer.GetPropertyBlock(lineProperties);
        lineProperties.SetFloat(DashCount, dashCount);
        lineProperties.SetFloat(DashRatio, flashing ? 1f : dashLength / (dashLength + gapLength));
        lineRenderer.SetPropertyBlock(lineProperties);
        lineRenderer.SetPositions(circlePoints);
    }

    public static int EvaluateHit(float distance, float radius, float tolerance, float danger, out bool perfect,
        float padding = 0f, float mouseHitRadius = 0f)
    {
        perfect = Mathf.Abs(distance - radius) <= tolerance;
        float contactDistance = Mathf.Max(0f, distance - Mathf.Max(0f, mouseHitRadius));
        bool insideAttack = contactDistance <= radius + Mathf.Max(0f, padding);
        if (distance <= danger)
            return perfect ? 40 : insideAttack ? 20 : 0;
        if (perfect) return 60;
        if (!insideAttack) return 0;
        float outerBoundary = radius * 0.6f;
        // Keep the inclusive 60% boundary stable across Mono/IL2CPP precision.
        return distance >= outerBoundary || Mathf.Approximately(distance, outerBoundary) ? 40 : 20;
    }

    public int ReleaseAttack()
    {
        if (!gameManager || !gameManager.IsPlaying || !mouseSpawner) return 0;
        int total = 0;
        float releasedRadius = currentRadius;
        flashRadius = releasedRadius;
        flashEndsAt = Time.unscaledTime + attackFlashDuration;
        DrawCircle();
        Physics2D.SyncTransforms();
        
        foreach (StraightLineMouse mouse in mouseSpawner.GetActiveMiceSnapshot())
        {
            if (!mouse || !mouse.IsAlive) continue;
            Vector3 killPosition = mouse.transform.position;
            float distance = Vector2.Distance(transform.position, killPosition);
            int points = EvaluateHit(distance, releasedRadius, perfectTolerance, dangerRadius, out bool perfect,
                hitPadding, mouse.AttackHitRadius);
            if (points == 0) continue;
            total += points;
            gameManager.AddScore(points);
            PlayKillFeedback(killPosition, perfect);
            mouse.Kill();
            if (perfect) OnPerfectHit(killPosition);
        }
        currentRadius = minRadius;
        DrawCircle();
        return total;
    }

    void PlayKillFeedback(Vector3 position, bool perfect)
    {
        PlayBurst(mouseKillVFX, position);
        if (perfect) PlayBurst(perfectKillVFX, position);
    }

    static void PlayBurst(ParticleSystem prefab, Vector3 position)
    {
        if (!prefab) return;
        ParticleSystem effect = Instantiate(prefab, position, prefab.transform.rotation);
        var main = effect.main;
        main.loop = false;
        main.stopAction = ParticleSystemStopAction.Destroy;
        effect.Play(true);
    }

    void OnPerfectHit(Vector3 mousePosition) => onPerfectHit.Invoke(mousePosition);

    void ValidateSettings()
    {
        minRadius = Mathf.Max(0f, minRadius);
        maxRadius = Mathf.Max(minRadius, maxRadius);
        startingRadius = Mathf.Clamp(startingRadius, minRadius, maxRadius);
        scrollSensitivity = Mathf.Max(0f, scrollSensitivity);
        perfectTolerance = Mathf.Max(0f, perfectTolerance);
        hitPadding = Mathf.Max(0f, hitPadding);
        dangerRadius = Mathf.Max(0f, dangerRadius);
        circleSegments = Mathf.Max(3, circleSegments);
        previewLineWidth = Mathf.Max(0.001f, previewLineWidth);
        attackLineWidth = Mathf.Max(0.001f, attackLineWidth);
        dashLength = Mathf.Max(0.01f, dashLength);
        gapLength = Mathf.Max(0.01f, gapLength);
        attackFlashDuration = Mathf.Max(0f, attackFlashDuration);
    }

    void OnValidate() => ValidateSettings();
}
