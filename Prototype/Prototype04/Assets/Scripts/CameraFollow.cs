using UnityEngine;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    public GridMaker gridMaker;
    public PlayerSpawner playerSpawner;
    public Transform target;
    [Tooltip("Full dead-zone width in world units.")]
    [Min(0f)] public float deadZoneWidth = 8f;
    [Tooltip("Full dead-zone height in world units.")]
    [Min(0f)] public float deadZoneHeight = 6f;
    [Tooltip("SmoothDamp response time in seconds; smaller values follow faster.")]
    [Min(0.01f)] public float followSmoothness = 0.25f;
    [Tooltip("Horizontal following is used only when the map is wider than the viewport; otherwise X stays centered.")]
    public bool followHorizontally = true;
    Camera view;
    Vector2 velocity;
    Rect originalRect;
    bool letterboxed;
    float renderedHalfWidth;
    float renderedHalfHeight;
    float edgeInset = 0.02f;

    void Awake()
    {
        view = GetComponent<Camera>();
        originalRect = view.rect;
        renderedHalfWidth = view.orthographicSize * view.aspect;
        renderedHalfHeight = view.orthographicSize;
        if (!view.orthographic) {
            Debug.LogError("CameraFollow requires an orthographic Camera.", this);
            enabled = false;
        }
    }
    void OnEnable() { if (gridMaker != null) gridMaker.GenerationCompleted += InitializeBounds; }
    void Start() { if (gridMaker != null && gridMaker.IsGenerated) InitializeBounds(gridMaker); }
    void OnDisable()
    {
        if (gridMaker != null) gridMaker.GenerationCompleted -= InitializeBounds;
        if (view != null) { view.rect = originalRect; view.ResetAspect(); view.ResetProjectionMatrix(); }
    }

    public void InitializeBounds(GridMaker grid)
    {
        UpdateViewport(grid.GeneratedBounds);
        transform.position = ClampCenter(transform.position, grid.GeneratedBounds);
        if (!followHorizontally) transform.position = new Vector3(grid.GeneratedBounds.center.x, transform.position.y, transform.position.z);
        velocity = Vector2.zero;
    }

    void LateUpdate()
    {
        if (gridMaker == null || !gridMaker.IsGenerated) return;
        Bounds bounds = gridMaker.GeneratedBounds;
        UpdateViewport(bounds);
        Vector3 position = ClampCenter(transform.position, bounds);
        if (playerSpawner != null && playerSpawner.SpawnedPlayer != null) target = playerSpawner.SpawnedPlayer.transform;
        if (target != null && target.gameObject.activeInHierarchy) {
            float halfWidth = Mathf.Min(deadZoneWidth / 2f, renderedHalfWidth * 0.95f);
            float halfHeight = Mathf.Min(deadZoneHeight / 2f, renderedHalfHeight * 0.95f);
            if (followHorizontally && InsetBounds(bounds).size.x > renderedHalfWidth * 2f + 0.001f)
                position.x = FollowAxis(position.x, target.position.x, halfWidth, ref velocity.x);
            else { position.x = bounds.center.x; velocity.x = 0; }
            position.y = FollowAxis(position.y, target.position.y, halfHeight, ref velocity.y);
        }
        Vector3 clamped = ClampCenter(position, bounds);
        if (clamped.x != position.x) velocity.x = 0;
        if (clamped.y != position.y) velocity.y = 0;
        transform.position = clamped;
    }

    float FollowAxis(float cameraPosition, float playerPosition, float halfZone, ref float axisVelocity)
    {
        float delta = playerPosition - cameraPosition;
        // Clear residual smoothing so the camera is completely still inside the buffer.
        if (Mathf.Abs(delta) <= halfZone) { axisVelocity = 0; return cameraPosition; }
        float desired = playerPosition - Mathf.Sign(delta) * halfZone;
        return Mathf.SmoothDamp(cameraPosition, desired, ref axisVelocity, Mathf.Max(0.01f, followSmoothness));
    }

    public Vector3 ClampCenter(Vector3 center, Bounds bounds)
    {
        bounds = InsetBounds(bounds);
        float halfHeight = renderedHalfHeight;
        float halfWidth = renderedHalfWidth;
        center.x = bounds.size.x <= halfWidth * 2f ? bounds.center.x : Mathf.Clamp(center.x, bounds.min.x + halfWidth, bounds.max.x - halfWidth);
        center.y = bounds.size.y <= halfHeight * 2f ? bounds.center.y : Mathf.Clamp(center.y, bounds.min.y + halfHeight, bounds.max.y - halfHeight);
        return center;
    }

    void UpdateViewport(Bounds bounds)
    {
        float screenWidth = view.targetTexture != null ? view.targetTexture.width : Screen.width;
        float screenHeight = view.targetTexture != null ? view.targetTexture.height : Screen.height;
        if (screenWidth <= 0 || screenHeight <= 0) return;
        edgeInset = Mathf.Max(0.02f, view.orthographicSize * 2f / (screenHeight * originalRect.height));
        bounds = InsetBounds(bounds);
        float fullAspect = screenWidth * originalRect.width / (screenHeight * originalRect.height);
        float fullHeight = view.orthographicSize * 2f;
        float fullWidth = fullHeight * fullAspect;
        float width = Mathf.Min(bounds.size.x, fullWidth);
        float height = Mathf.Min(bounds.size.y, fullHeight);
        renderedHalfWidth = width / 2f;
        renderedHalfHeight = height / 2f;
        Rect rect = originalRect;
        rect.width *= Mathf.Max(0.001f, width / fullWidth);
        rect.height *= Mathf.Max(0.001f, height / fullHeight);
        rect.x += (originalRect.width - rect.width) / 2f;
        rect.y += (originalRect.height - rect.height) / 2f;
        letterboxed = rect != originalRect;
        view.rect = rect;
        // Explicit aspect also works when a RenderTexture or editor Game view has
        // not refreshed its automatic aspect after a viewport change.
        view.aspect = width / height;
        // Crop projection and viewport together for short maps. Pixels per world unit
        // stay constant, so this is letterboxing rather than a zoom change.
        if (height < fullHeight) view.projectionMatrix = Matrix4x4.Ortho(-width / 2f, width / 2f,
            -height / 2f, height / 2f, view.nearClipPlane, view.farClipPlane);
        else view.ResetProjectionMatrix();
    }

    Bounds InsetBounds(Bounds bounds)
    {
        // Keep pixel rounding/filtering from exposing a sliver beyond the ground.
        float insetX = Mathf.Min(edgeInset, bounds.size.x * 0.1f);
        float insetY = Mathf.Min(edgeInset, bounds.size.y * 0.1f);
        bounds.Expand(new Vector3(-insetX * 2f, -insetY * 2f, 0));
        return bounds;
    }

    void OnGUI()
    {
        if (!letterboxed || Event.current.type != EventType.Repaint) return;
        Color previous = GUI.color;
        GUI.color = Color.black;
        float top = (1f - originalRect.yMax) * Screen.height;
        float fullHeight = originalRect.height * Screen.height;
        GUI.DrawTexture(new Rect(originalRect.x * Screen.width, top, (view.rect.x - originalRect.x) * Screen.width, fullHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(view.rect.xMax * Screen.width, top, (originalRect.xMax - view.rect.xMax) * Screen.width, fullHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(view.rect.x * Screen.width, top, view.rect.width * Screen.width, (originalRect.yMax - view.rect.yMax) * Screen.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(view.rect.x * Screen.width, (1f - view.rect.y) * Screen.height, view.rect.width * Screen.width, (view.rect.y - originalRect.y) * Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position + Vector3.forward * 10f, new Vector3(deadZoneWidth, deadZoneHeight, 0));
    }
}
