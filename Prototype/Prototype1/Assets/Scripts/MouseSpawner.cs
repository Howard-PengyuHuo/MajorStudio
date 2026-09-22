using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public sealed class MouseSpawner : MonoBehaviour
{
    public enum Region { Top, Bottom, Left, Right }

    public Transform targetCenter;
    public Camera spawnCamera;
    public GameObject mouseTop;
    public GameObject mouseRight;
    public GameManager gameManager;
    [Min(0.01f)] public float outsideSpawnOffset = 1f;
    [Min(0.01f)] public float mousePathRadius = 0.5f;
    [Min(1)] public int maxSpawnAttempts = 64;
    [Tooltip("Additional obstacle layers. Explicit Blocks below are always checked.")]
    public LayerMask blockLayer;
    public Transform[] blocks;
    public Transform eggFormation;
    [Header("Difficulty")]
    [FormerlySerializedAs("moveSpeed")]
    [Min(0.01f)] public float startMouseSpeed = 1.5f;
    [Min(0.01f)] public float maxMouseSpeed = 2.7f;
    [FormerlySerializedAs("spawnInterval")]
    [Min(0.01f)] public float startSpawnInterval = 1.8f;
    [Min(0.01f)] public float minSpawnInterval = 0.6f;
    public AnimationCurve difficultyCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Header("Current Difficulty (runtime values)")]
    [SerializeField] private float currentMouseSpeed = 1.5f;
    [SerializeField] private float currentSpawnInterval = 1.8f;
    [SerializeField] private float currentDifficulty;
    public float mouseTopRotationOffset = 90f;
    public float mouseRightRotationOffset = 180f;

    readonly HashSet<Collider2D> blockColliders = new HashSet<Collider2D>();
    readonly HashSet<Collider2D> eggColliders = new HashSet<Collider2D>();
    readonly List<RaycastHit2D> hits = new List<RaycastHit2D>();
    readonly List<StraightLineMouse> mice = new List<StraightLineMouse>();
    float spawnTimer;
    bool gameplayEnded;
    public bool GameplayActive => !gameplayEnded && (!gameManager || gameManager.IsPlaying);
    public StraightLineMouse[] GetActiveMiceSnapshot() => mice.ToArray();
    public float CurrentMouseSpeed => currentMouseSpeed;
    public float CurrentSpawnInterval => currentSpawnInterval;
    public float CurrentDifficulty => currentDifficulty;

    void Awake()
    {
        ValidateDifficultySettings();
        ApplyDifficulty(0f);
        // Preserve the existing immediate first spawn.
        spawnTimer = currentSpawnInterval;
        if (!targetCenter || !spawnCamera || !mouseTop || !mouseRight || !spawnCamera.orthographic)
        {
            Debug.LogError("MouseSpawner needs its target, orthographic camera and both mouse prefabs.", this);
            enabled = false;
            return;
        }
        
        if (blocks != null)
            foreach (Transform block in blocks) RegisterColliders(block, blockColliders);
        RegisterColliders(eggFormation, eggColliders);
        Physics2D.SyncTransforms();
    }

    static void RegisterColliders(Transform root, HashSet<Collider2D> destination)
    {
        if (!root) return;
        foreach (SpriteRenderer visual in root.GetComponentsInChildren<SpriteRenderer>(true))
            if (visual.sprite && !visual.GetComponent<Collider2D>())
                visual.gameObject.AddComponent<PolygonCollider2D>();
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
            destination.Add(collider);
    }

    void Update()
    {
        if (!GameplayActive) return;
        RefreshDifficulty();
        spawnTimer += Time.deltaTime;
        if (spawnTimer < currentSpawnInterval) return;
        spawnTimer = 0f;
        TrySpawnMouse();
    }

    void RefreshDifficulty()
    {
        float progress = 0f;
        if (gameManager)
        {
            float duration = gameManager.gameDuration;
            float elapsedTime = duration - gameManager.RemainingTime;
            progress = duration > 0f ? Mathf.Clamp01(elapsedTime / duration) : 1f;
        }
        ApplyDifficulty(progress);
    }

    void ApplyDifficulty(float progress)
    {
        ValidateDifficultySettings();
        currentDifficulty = Mathf.Clamp01(difficultyCurve != null && difficultyCurve.length > 0
            ? difficultyCurve.Evaluate(progress) : progress);
        currentMouseSpeed = Mathf.Lerp(startMouseSpeed, maxMouseSpeed, currentDifficulty);
        currentSpawnInterval = Mathf.Lerp(startSpawnInterval, minSpawnInterval, currentDifficulty);
    }

    void ValidateDifficultySettings()
    {
        startMouseSpeed = Mathf.Max(0.01f, startMouseSpeed);
        maxMouseSpeed = Mathf.Max(startMouseSpeed, maxMouseSpeed);
        startSpawnInterval = Mathf.Max(0.01f, startSpawnInterval);
        minSpawnInterval = Mathf.Clamp(minSpawnInterval, 0.01f, startSpawnInterval);
    }

    void OnValidate() => ValidateDifficultySettings();

    public static Region Classify(Vector2 directionToCenter)
    {
        if (Mathf.Abs(directionToCenter.y) >= Mathf.Abs(directionToCenter.x))
            return directionToCenter.y <= 0 ? Region.Top : Region.Bottom;
        return directionToCenter.x <= 0 ? Region.Right : Region.Left;
    }

    
    public static bool TryGetSpawnPosition(Camera camera, Vector3 center, Vector2 outward,
        float offset, float radius, out Vector3 position)
    {
        position = default;
        if (!camera || !camera.orthographic || Mathf.Abs(camera.transform.forward.z) < 0.9999f)
            return false;
        Vector3 origin = camera.transform.InverseTransformPoint(center);
        if (origin.z <= camera.nearClipPlane || origin.z >= camera.farClipPlane) return false;
        Vector3 ray = camera.transform.InverseTransformDirection(outward.normalized);
        float height = camera.orthographicSize;
        float width = height * camera.aspect;
        if (Mathf.Abs(origin.x) >= width || Mathf.Abs(origin.y) >= height) return false;
        
        width += radius;
        height += radius;
        float tx = Mathf.Abs(ray.x) < 0.000001f ? float.PositiveInfinity
            : ((ray.x > 0 ? width : -width) - origin.x) / ray.x;
        float ty = Mathf.Abs(ray.y) < 0.000001f ? float.PositiveInfinity
            : ((ray.y > 0 ? height : -height) - origin.y) / ray.y;
        if (float.IsInfinity(Mathf.Min(tx, ty))) return false;
        position = center + (Vector3)outward.normalized * (Mathf.Min(tx, ty) + Mathf.Max(0.01f, offset));
        return true;
    }

    static float VisualRadius(GameObject prefab)
    {
        SpriteRenderer renderer = prefab.GetComponent<SpriteRenderer>();
        if (!renderer || !renderer.sprite) return 0f;
        Bounds bounds = renderer.sprite.bounds;
        Vector3 extent = bounds.extents;
        
        extent.x += Mathf.Abs(bounds.center.x);
        extent.y += Mathf.Abs(bounds.center.y);
        return Vector2.Scale(extent, prefab.transform.localScale).magnitude;
    }

    public bool TrySpawnMouse()
    {
        if (!isActiveAndEnabled || !GameplayActive || !targetCenter || !spawnCamera || !mouseTop || !mouseRight)
            return false;
        RefreshDifficulty();
        Physics2D.SyncTransforms();
        for (int attempt = 0; attempt < Mathf.Max(1, maxSpawnAttempts); attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 outward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Region region = Classify(-outward);
            bool vertical = region == Region.Top || region == Region.Bottom;
            GameObject prefab = vertical ? mouseTop : mouseRight;
            float radius = Mathf.Max(mousePathRadius, VisualRadius(prefab));
            if (!TryGetSpawnPosition(spawnCamera, targetCenter.position, outward,
                outsideSpawnOffset, radius, out Vector3 spawn)) continue;
            Vector2 toCenter = (Vector2)(targetCenter.position - spawn);
            if (PathBlocked(spawn, toCenter.normalized, toCenter.magnitude, radius)) continue;

            GameObject instance = Instantiate(prefab, spawn, GetMouseRotation(region, toCenter));
            StraightLineMouse mouse = instance.AddComponent<StraightLineMouse>();
            mouse.Initialize(this, toCenter.normalized, currentMouseSpeed, radius);
            mice.Add(mouse);
            return true;
        }
        
        return false;
    }

    public Quaternion GetMouseRotation(Region region, Vector2 direction)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (region == Region.Left)
            
            return Quaternion.Euler(0f, -180f, 180f - (angle + mouseRightRotationOffset));
        bool vertical = region == Region.Top || region == Region.Bottom;
        return Quaternion.Euler(0f, 0f, angle + (vertical ? mouseTopRotationOffset : mouseRightRotationOffset));
    }

    bool PathBlocked(Vector2 origin, Vector2 direction, float distance, float radius)
    {
        var filter = new ContactFilter2D { useTriggers = true };
        Physics2D.CircleCast(origin, radius, direction, filter, hits, distance);
        foreach (RaycastHit2D hit in hits)
            if (blockColliders.Contains(hit.collider) || (blockLayer.value & (1 << hit.collider.gameObject.layer)) != 0)
                return true;
        return false;
    }

    public Egg FindEggHit(Vector2 origin, Vector2 direction, float distance, float radius)
    {
        if (!GameplayActive) return null;
        var filter = new ContactFilter2D { useTriggers = true };
        Physics2D.CircleCast(origin, radius, direction, filter, hits, distance);
        Egg nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (RaycastHit2D hit in hits)
        {
            if (!eggColliders.Contains(hit.collider)) continue;
            Egg egg = hit.collider.GetComponentInParent<Egg>();
            if (egg && !egg.IsDestroyed && hit.distance < nearestDistance)
            {
                nearest = egg;
                nearestDistance = hit.distance;
            }
        }
        return nearest;
    }

    public void EndGameplay()
    {
        if (gameplayEnded) return;
        // Capture the timer's final progress once, then freeze these values too.
        RefreshDifficulty();
        gameplayEnded = true;
        foreach (StraightLineMouse mouse in mice)
            if (mouse) mouse.Stop();
    }

    internal void Forget(StraightLineMouse mouse) => mice.Remove(mouse);
    void OnDisable() => EndGameplay();
}
