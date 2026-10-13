using System.Collections.Generic;
using UnityEngine;

public class CollectibleManager : MonoBehaviour
{
    public GridMaker gridMaker;
    public PlayerSpawner playerSpawner;
    public GameObject applePrefab;
    [Min(0)] public int appleCount = 5;
    [Tooltip("World-space radius checked for solid obstacles before placing an Apple.")]
    [Min(0.01f)] public float obstacleClearanceRadius = 0.35f;
    public int CollectedCount { get; private set; }
    public int TotalCount { get; private set; }
    public bool HasSpawned { get; private set; }
    public event System.Action CountsChanged;
    readonly HashSet<CollectibleApple> remaining = new HashSet<CollectibleApple>();

    void Awake()
    {
        if (gridMaker == null) gridMaker = GetComponent<GridMaker>();
        if (playerSpawner == null) playerSpawner = GetComponent<PlayerSpawner>();
    }

    void OnEnable()
    {
        if (playerSpawner != null) playerSpawner.PlayerSpawned += SpawnApples;
        TrySpawnAfterPlayer();
    }

    void Start()
    {
        if (gridMaker == null || playerSpawner == null)
            Debug.LogError("CollectibleManager: Assign GridMaker and PlayerSpawner.", this);
        TrySpawnAfterPlayer();
    }

    void OnDisable()
    {
        if (playerSpawner != null) playerSpawner.PlayerSpawned -= SpawnApples;
    }

    void TrySpawnAfterPlayer()
    {
        if (playerSpawner != null && playerSpawner.SpawnedPlayer != null) SpawnApples(playerSpawner);
    }

    public void SpawnApples(PlayerSpawner spawner)
    {
        if (HasSpawned || gridMaker == null || !gridMaker.IsGenerated ||
            spawner == null || spawner.SpawnedPlayer == null || spawner.SpawnTile == null) return;
        if (appleCount > 0 && (applePrefab == null || applePrefab.GetComponent<CollectibleApple>() == null ||
            applePrefab.GetComponent<Collider2D>() == null)) {
            Debug.LogError("CollectibleManager: Assign a Smile Apple prefab with CollectibleApple and a trigger Collider2D.", this);
            return;
        }
        HasSpawned = true;
        Physics2D.SyncTransforms();
        int requested = Mathf.Max(0, appleCount);
        int sectionCount = Mathf.Min(requested, gridMaker.height);
        var sections = new List<List<TerrainTile>>();
        for (int i = 0; i < sectionCount; i++) sections.Add(new List<TerrainTile>());
        int candidateCount = 0;
        if (sectionCount > 0) {
            foreach (TerrainTile tile in gridMaker.Tiles) {
                if (tile == null || (tile.terrainType != TerrainType.Grass && tile.terrainType != TerrainType.Sand) ||
                    tile == spawner.SpawnTile || gridMaker.IsWatcherSpawnTile(tile) || !HasClearance(tile.transform.position)) continue;
                int section = Mathf.Min(sectionCount - 1, tile.gridY * sectionCount / gridMaker.height);
                sections[section].Add(tile);
                candidateCount++;
            }
        }
        // One random candidate per vertical band first; repeat across remaining bands if some are empty.
        // Removing each chosen tile guarantees no duplicate placement, even in the fallback passes.
        while (TotalCount < requested) {
            bool found = false;
            foreach (var section in sections) {
                if (section.Count == 0 || TotalCount >= requested) continue;
                int index = Random.Range(0, section.Count);
                TerrainTile tile = section[index];
                section.RemoveAt(index);
                GameObject instance = Instantiate(applePrefab, tile.transform);
                instance.name = $"Smile Apple ({tile.gridX}, {tile.gridY})";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                CollectibleApple apple = instance.GetComponent<CollectibleApple>();
                apple.Initialize(this, tile);
                SpriteRenderer ground = tile.GetComponent<SpriteRenderer>();
                foreach (var renderer in instance.GetComponentsInChildren<SpriteRenderer>()) {
                    renderer.sortingLayerID = ground.sortingLayerID;
                    renderer.sortingOrder = ground.sortingOrder + 1;
                }
                remaining.Add(apple);
                TotalCount++;
                found = true;
            }
            if (!found) break;
        }
        if (TotalCount < requested)
            Debug.LogWarning($"CollectibleManager: Only {TotalCount} safe tiles available for {requested} Apples. HUD total uses the actual spawned count.", this);
        Debug.Log($"CollectibleManager: Spawned {TotalCount} Apples from {candidateCount} safe candidates across {sectionCount} vertical sections.", this);
        CountsChanged?.Invoke();
    }

    bool HasClearance(Vector2 position)
    {
        foreach (var collider in Physics2D.OverlapCircleAll(position, obstacleClearanceRadius)) {
            if (!collider.isTrigger) return false;
        }
        return true;
    }

    public bool TryCollect(CollectibleApple apple)
    {
        if (apple == null || apple.IsCollected || !remaining.Remove(apple)) return false;
        apple.Consume();
        CollectedCount++;
        CountsChanged?.Invoke();
        return true;
    }
}
