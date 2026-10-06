using UnityEngine;

public class EggProjectile : MonoBehaviour
{
    [Tooltip("Sprite child; only this transform moves vertically for the fake arc.")]
    public Transform visual;
    Vector3 start;
    Vector3 visualOrigin;
    TerrainTile target;
    Watcher targetWatcher;
    Transform landingTarget;
    float duration;
    float height;
    float elapsed;
    bool flying;

    public void Launch(Vector3 origin, TerrainTile targetTile, float throwDuration, float throwHeight)
    {
        target = targetTile;
        targetWatcher = null;
        BeginFlight(origin, targetTile != null ? targetTile.transform : null, throwDuration, throwHeight);
    }

    public void LaunchBoba(Vector3 origin, Watcher watcher, float throwDuration, float throwHeight)
    {
        target = null;
        targetWatcher = watcher;
        BeginFlight(origin, watcher != null ? watcher.transform : null, throwDuration, throwHeight);
    }

    void BeginFlight(Vector3 origin, Transform destination, float throwDuration, float throwHeight)
    {
        if (visual == null || visual == transform || destination == null || throwDuration <= 0f) {
            Debug.LogError("EggProjectile: Assign a visual child, a target tile and a positive duration.", this);
            Destroy(gameObject);
            return;
        }
        // No projectile colliders or physics body participate in the flight.
        foreach (var collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        foreach (var body in GetComponentsInChildren<Rigidbody2D>()) body.simulated = false;
        start = origin;
        landingTarget = destination;
        duration = throwDuration;
        height = Mathf.Max(0f, throwHeight);
        elapsed = 0f;
        visualOrigin = visual.localPosition;
        transform.position = start;
        flying = true;
    }

    void Update()
    {
        if (!flying) return;
        if (landingTarget == null) { Destroy(gameObject); return; }
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        transform.position = Vector3.Lerp(start, landingTarget.position, t);
        // World-space height avoids scaling the arc by the authored Egg prefab scale.
        visual.localPosition = visualOrigin;
        visual.position += Vector3.up * (Mathf.Sin(t * Mathf.PI) * height);
        if (t < 1f) return;
        flying = false;
        if (target != null) target.TryCreateEggPlatform();
        else if (targetWatcher != null) targetWatcher.Blind(targetWatcher.blindDuration);
        Destroy(gameObject);
    }
}
