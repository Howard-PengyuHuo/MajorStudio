using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    public GridMaker gridMaker;
    public GameObject playerPrefab;
    public Camera spawnCamera;
    [Tooltip("Optional placed Player to reuse at runtime instead of creating a second player.")]
    public GameObject existingPlayer;
    [Range(0f, 0.49f)] public float viewportMargin = 0.1f;
    [Tooltip("World-space clearance radius; should cover the Player's collision footprint.")]
    [Min(0.01f)] public float spawnClearanceRadius = 0.25f;
    public LayerMask blockingLayers = ~0;
    public GameObject SpawnedPlayer { get; private set; }
    public TerrainTile SpawnTile { get; private set; }
    public event System.Action<PlayerSpawner> PlayerSpawned;

    void Awake()
    {
        if (gridMaker == null) gridMaker = GetComponent<GridMaker>();
        if (spawnCamera == null) spawnCamera = Camera.main;
        // The authored transform stays untouched in edit mode; wait for generation at runtime.
        if (existingPlayer != null) existingPlayer.SetActive(false);
    }

    void OnEnable()
    {
        if (gridMaker != null) gridMaker.GenerationCompleted += SpawnPlayer;
    }

    void Start()
    {
        if (gridMaker == null || spawnCamera == null || playerPrefab == null) {
            Debug.LogError("PlayerSpawner: Assign GridMaker, Player prefab and the stationary Main Camera.", this);
            return;
        }
        // Covers a spawner enabled after GridMaker has already finished.
        if (gridMaker.IsGenerated) SpawnPlayer(gridMaker);
    }

    void OnDisable()
    {
        if (gridMaker != null) gridMaker.GenerationCompleted -= SpawnPlayer;
    }

    public void SpawnPlayer(GridMaker grid)
    {
        if (SpawnedPlayer != null || playerPrefab == null || spawnCamera == null || !grid.IsGenerated) return;
        Physics2D.SyncTransforms();
        var preferred = new List<TerrainTile>();
        var visible = new List<TerrainTile>();
        foreach (TerrainTile tile in grid.Tiles) {
            if (tile.terrainType != TerrainType.Grass && tile.terrainType != TerrainType.Sand) continue;
            Vector3 viewport = spawnCamera.WorldToViewportPoint(tile.transform.position);
            if (viewport.z < spawnCamera.nearClipPlane || viewport.z > spawnCamera.farClipPlane ||
                viewport.x <= 0f || viewport.x >= 1f || viewport.y <= 0f || viewport.y >= 1f ||
                (spawnCamera.cullingMask & (1 << tile.gameObject.layer)) == 0) continue;
            if (!HasClearance(tile.transform.position)) continue;
            visible.Add(tile);
            if (viewport.x >= viewportMargin && viewport.x <= 1f - viewportMargin &&
                viewport.y >= viewportMargin && viewport.y <= 1f - viewportMargin) preferred.Add(tile);
        }
        var candidates = preferred.Count > 0 ? preferred : visible;
        if (candidates.Count == 0) {
            Debug.LogError("PlayerSpawner: No clear Grass or Sand tile is visible to Main Camera. No player spawned; the map and camera were left unchanged.", this);
            return;
        }
        SpawnTile = candidates[Random.Range(0, candidates.Count)];
        SpawnedPlayer = existingPlayer != null ? existingPlayer : Instantiate(playerPrefab);
        SpawnedPlayer.name = "Player";
        SpawnedPlayer.transform.position = SpawnTile.transform.position;
        var body = SpawnedPlayer.GetComponent<Rigidbody2D>();
        if (body != null) {
            body.position = SpawnTile.transform.position;
            body.linearVelocity = Vector2.zero;
        }
        var thrower = SpawnedPlayer.GetComponent<EggThrower>();
        if (thrower != null) thrower.Initialize(grid, spawnCamera);
        SpawnedPlayer.SetActive(true);
        var ground = SpawnTile.GetComponent<SpriteRenderer>();
        foreach (var renderer in SpawnedPlayer.GetComponentsInChildren<SpriteRenderer>()) {
            renderer.sortingLayerID = ground.sortingLayerID;
            renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, ground.sortingOrder + 2);
        }
        Debug.Log($"PlayerSpawner: Player spawned on {SpawnTile.terrainType} ({SpawnTile.gridX}, {SpawnTile.gridY}).", this);
        PlayerSpawned?.Invoke(this);
    }

    bool HasClearance(Vector2 position)
    {
        foreach (Collider2D collider in Physics2D.OverlapCircleAll(position, spawnClearanceRadius, blockingLayers)) {
            if (collider.isTrigger) continue;
            if (existingPlayer != null && collider.transform.IsChildOf(existingPlayer.transform)) continue;
            return false;
        }
        return true;
    }
}
