using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum ThrowableType { Egg, Boba }

[RequireComponent(typeof(AudioSource))]
public class EggThrower : MonoBehaviour
{
    public GameObject eggPrefab;
    public GridMaker gridMaker;
    public Camera aimCamera;
    [Tooltip("Maximum 2D world distance from player to tile center.")]
    [Min(0f)] public float throwRange = 4f;
    [Min(0.01f)] public float throwDuration = 0.6f;
    [Min(0f)] public float throwHeight = 1.5f;
    [Header("Boba")]
    public GameObject bobaPrefab;
    [Min(0f)] public float bobaThrowRange = 4f;
    [Min(0.01f)] public float bobaThrowDuration = 0.6f;
    [Min(0f)] public float bobaThrowHeight = 1.5f;
    [Tooltip("Layers queried directly for Watcher Collider2D targets. Includes triggers.")]
    public LayerMask watcherTargetLayers = ~0;
    [Tooltip("Temporary target/throw diagnostics; logs on target changes and clicks.")]
    public bool debugThrows = true;
    [Header("Sound")]
    public AudioClip eggThrowClip;
    [SerializeField] AudioSource audioSource;
    readonly List<Collider2D> watcherHits = new List<Collider2D>();
    GameObject lastDebugObject;
    Component lastDebugComponent;
    [SerializeField] ThrowableType selectedItem = ThrowableType.Egg;
    [SerializeField] Watcher currentWatcherTarget;
    [SerializeField] TerrainTile currentTarget;
    public TerrainTile CurrentTarget => currentTarget;
    public ThrowableType SelectedItem => selectedItem;
    public event System.Action<ThrowableType> SelectionChanged;
    public Watcher CurrentWatcherTarget => currentWatcherTarget;
    public bool TargetInRange => selectedItem == ThrowableType.Egg
        ? CurrentTarget != null && Vector2.Distance(transform.position, CurrentTarget.transform.position) <= throwRange
        : currentWatcherTarget != null && Vector2.Distance(transform.position, currentWatcherTarget.transform.position) <= bobaThrowRange;

    public void Initialize(GridMaker grid, Camera camera)
    {
        gridMaker = grid;
        aimCamera = camera;
    }

    void Awake()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource != null) {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
        }
    }

    void Start()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (eggPrefab == null || eggPrefab.GetComponent<EggProjectile>() == null) {
            Debug.LogError("EggThrower: Assign an Egg prefab with EggProjectile and its visual child.", this);
            enabled = false;
        }
        Debug.Log($"Selected throwable: {selectedItem} (1 = Egg, 2 = Boba).", this);
    }

    public void SelectItem(ThrowableType item)
    {
        if (selectedItem == item) return;
        selectedItem = item;
        currentTarget = null;
        currentWatcherTarget = null;
        Debug.Log($"Selected throwable: {selectedItem}.", this);
        SelectionChanged?.Invoke(selectedItem);
    }

    void Update()
    {
        currentTarget = null;
        currentWatcherTarget = null;
        if (Keyboard.current != null) {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SelectItem(ThrowableType.Egg);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) SelectItem(ThrowableType.Boba);
        }
        bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (Mouse.current == null || aimCamera == null) {
            if (clicked) RejectThrow("Mouse or aimCamera is missing");
            return;
        }
        Vector2 screenPosition = Mouse.current.position.ReadValue();
        if (!aimCamera.pixelRect.Contains(screenPosition)) {
            if (clicked) RejectThrow("Mouse is outside the camera viewport");
            return;
        }
        // Boba resolves a Collider2D/Watcher directly, before any TerrainTile lookup.
        if (selectedItem == ThrowableType.Boba) {
            currentWatcherTarget = FindWatcherTarget(screenPosition, out GameObject detectedObject);
            TraceTarget(detectedObject, currentWatcherTarget, clicked);
            if (clicked) TryThrowAt(currentWatcherTarget);
            return;
        }
        if (gridMaker == null || !gridMaker.IsGenerated) {
            if (clicked) RejectThrow("Terrain generation is not ready for Egg targeting");
            return;
        }
        // Intersect the mouse ray with the grid plane rather than an arbitrary world position.
        Transform container = gridMaker.gridContainer.transform;
        var plane = new Plane(container.forward, container.position);
        Ray ray = aimCamera.ScreenPointToRay(screenPosition);
        if (!plane.Raycast(ray, out float distance)) {
            if (clicked) RejectThrow("Mouse ray does not intersect the map plane");
            return;
        }
        Vector3 point = ray.GetPoint(distance);
        TerrainTile tile = gridMaker.GetTileAtWorldPosition(point);
        TraceTarget(tile != null ? tile.gameObject : null, tile, clicked);
        if (tile == null || tile.terrainType != TerrainType.Water) {
            if (clicked) RejectThrow("Egg requires a generated Water TerrainTile");
            return;
        }
        currentTarget = tile;
        if (clicked) TryThrowAt(CurrentTarget);
    }

    public Watcher FindWatcherTarget(Vector2 screenPosition, out GameObject detectedObject)
    {
        detectedObject = null;
        if (aimCamera == null) return null;
        Ray ray = aimCamera.ScreenPointToRay(screenPosition);
        float z = gridMaker != null && gridMaker.gridContainer != null ? gridMaker.gridContainer.transform.position.z : 0f;
        var plane = new Plane(Vector3.forward, new Vector3(0, 0, z));
        if (!plane.Raycast(ray, out float distance)) return null;
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(watcherTargetLayers);
        watcherHits.Clear();
        Physics2D.OverlapPoint(ray.GetPoint(distance), filter, watcherHits);
        // Water/Palm colliders may overlap the same point. Search all hits for Watcher.
        foreach (Collider2D hit in watcherHits) {
            if (detectedObject == null) detectedObject = hit.gameObject;
            Watcher watcher = hit.GetComponentInParent<Watcher>();
            if (watcher == null || !watcher.isActiveAndEnabled) continue;
            detectedObject = hit.gameObject;
            return watcher;
        }
        return null;
    }

    void TraceTarget(GameObject detectedObject, Component component, bool clicked)
    {
        if (!debugThrows || (!clicked && detectedObject == lastDebugObject && component == lastDebugComponent)) return;
        lastDebugObject = detectedObject;
        lastDebugComponent = component;
        string targetDistance = component != null ? Vector2.Distance(transform.position, component.transform.position).ToString("0.##") : "n/a";
        Debug.Log($"EggThrower: selected={selectedItem}, object under mouse={(detectedObject != null ? detectedObject.name : "none")}, target component={(component != null ? component.GetType().Name : "none")}, distance={targetDistance}, Watcher mask={watcherTargetLayers.value}, trigger queries={Physics2D.queriesHitTriggers}.", this);
    }

    bool RejectThrow(string reason)
    {
        if (debugThrows) Debug.Log($"EggThrower: {selectedItem} throw rejected: {reason}.", this);
        return false;
    }

    public bool TryThrowAt(TerrainTile tile)
    {
        if (tile == null || gridMaker == null || !gridMaker.IsGenerated || gridMaker.GetTile(tile.gridX, tile.gridY) != tile || tile.terrainType != TerrainType.Water)
            return RejectThrow("Egg target is not generated Water");
        if (Vector2.Distance(transform.position, tile.transform.position) > throwRange) return RejectThrow($"Egg distance exceeds range {throwRange}");
        if (eggPrefab == null || throwDuration <= 0f) return RejectThrow("Egg prefab missing or duration invalid");
        EggProjectile projectile = CreateProjectile(eggPrefab, tile.GetComponent<SpriteRenderer>());
        if (projectile == null) return false;
        projectile.Launch(transform.position, tile, throwDuration, throwHeight);
        if (projectile.IsFlying && eggThrowClip != null && audioSource != null && audioSource.isActiveAndEnabled)
            audioSource.PlayOneShot(eggThrowClip);
        if (debugThrows) Debug.Log($"EggThrower: Egg projectile launched toward {tile.name}.", this);
        return true;
    }

    public bool TryThrowAt(Watcher watcher)
    {
        if (watcher == null) return RejectThrow("No Watcher component detected under mouse; check its Collider2D, Watcher Target Layers and Physics2D trigger queries");
        if (!watcher.isActiveAndEnabled) return RejectThrow("Watcher is inactive or disabled");
        float distance = Vector2.Distance(transform.position, watcher.transform.position);
        if (debugThrows) Debug.Log($"EggThrower: Boba target={watcher.name}, distance={distance:0.##}, range={bobaThrowRange}.", this);
        if (distance > bobaThrowRange) return RejectThrow($"Watcher distance {distance:0.##} exceeds Boba range {bobaThrowRange}");
        if (bobaThrowDuration <= 0f) return RejectThrow("Boba duration must be positive");
        EggProjectile projectile = CreateProjectile(bobaPrefab, watcher.GetComponentInChildren<SpriteRenderer>());
        if (projectile == null) return false;
        projectile.LaunchBoba(transform.position, watcher, bobaThrowDuration, bobaThrowHeight);
        if (debugThrows) Debug.Log($"EggThrower: Boba projectile launched toward Watcher {watcher.name} using the shared arc.", this);
        return true;
    }

    EggProjectile CreateProjectile(GameObject prefab, SpriteRenderer ground)
    {
        if (prefab == null) {
            Debug.LogWarning("EggThrower: Assign the selected throwable's prefab in the Inspector.", this);
            return null;
        }
        GameObject instance = Instantiate(prefab, transform.position, Quaternion.identity);
        EggProjectile projectile = instance.GetComponent<EggProjectile>();
        if (projectile == null) {
            Debug.LogError("EggThrower: Both Egg and Boba prefabs need the shared EggProjectile component.", this);
            Destroy(instance);
            return null;
        }
        foreach (var renderer in instance.GetComponentsInChildren<SpriteRenderer>()) {
            if (ground == null) continue;
            renderer.sortingLayerID = ground.sortingLayerID;
            renderer.sortingOrder = ground.sortingOrder + 3;
        }
        return projectile;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, selectedItem == ThrowableType.Egg ? throwRange : bobaThrowRange);
        Transform target = selectedItem == ThrowableType.Egg ? currentTarget?.transform : currentWatcherTarget?.transform;
        if (target != null) {
            Gizmos.color = TargetInRange ? Color.green : Color.red;
            Gizmos.DrawWireCube(target.position, Vector3.one);
        }
    }
}
