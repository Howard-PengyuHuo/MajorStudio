using System.Collections.Generic;
using UnityEngine;

public class GridMaker : MonoBehaviour
{
    // The prefab instantiated in each grid cell; needs a SpriteRenderer on its root.
    public GameObject tilePrefab;

    // Scene object that holds the grid cells.
    public GameObject gridContainer;

    [Header("Forest obstacles")]
    public GameObject palmTreePrefab;
    [Range(0f, 1f)] public float palmSpawnChance = 0.3f;

    [Header("Watcher")]
    public GameObject watcherPrefab;
    [Min(0)] public int watcherCount = 3;
    [Tooltip("Preferred minimum Manhattan distance between Watcher spawn tiles. Relaxed only if there are not enough spaced candidates.")]
    [Min(0)] public int watcherMinSpacing = 6;
    [Tooltip("Manhattan distance: abs(dx) + abs(dy), in grid cells.")]
    [Min(0)] public int watchRange = 5;
    public GameObject watchedIndicatorPrefab;
    [Tooltip("Temporary diagnostics for Watcher candidates, references and spawning.")]
    public bool debugWatcherGeneration = true;

    // Size of the grid.
    [Min(1)] public int width = 10;
    [Min(1)] public int height = 10;

    // Space between tile centers in container-local units.
    [Min(0.001f)] public float yOffset = 1f;
    [Min(0.001f)] public float xOffset = 1f;

    [Tooltip("Larger values produce broader, smoother terrain regions.")]
    [SerializeField, Min(0.001f)] float noiseScale = 10f;

    // Colors of each cell type. Defaults are opaque so new components are visible.
    [SerializeField] Color grassColor = new Color(0.4f, 0.8f, 0.2f, 1f);
    [SerializeField] Color mountainColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [SerializeField] Color sandColor = new Color(0.9f, 0.8f, 0.4f, 1f);
    [SerializeField] Color waterColor = new Color(0.1f, 0.5f, 0.9f, 1f);
    [SerializeField] Color forestColor = new Color(0.1f, 0.4f, 0.15f, 1f);

    // Array that holds all our cells.
    GameObject[,] tilesInGrid;
    TerrainTile[,] terrainInGrid;
    int seed;

    public bool IsGenerated { get; private set; }
    public Bounds GeneratedBounds { get; private set; }
    public Watcher GeneratedWatcher { get; private set; }
    public IReadOnlyList<Watcher> GeneratedWatchers => generatedWatchers;
    readonly List<Watcher> generatedWatchers = new List<Watcher>();
    readonly HashSet<TerrainTile> watcherSpawnTiles = new HashSet<TerrainTile>();
    public bool IsWatcherSpawnTile(TerrainTile tile) => tile != null && watcherSpawnTiles.Contains(tile);
    public event System.Action<GridMaker> GenerationCompleted;

    public IEnumerable<TerrainTile> Tiles
    {
        get {
            if (terrainInGrid == null) yield break;
            foreach (TerrainTile tile in terrainInGrid) yield return tile;
        }
    }

    public TerrainTile GetTile(int x, int y)
    {
        return terrainInGrid != null && x >= 0 && x < width && y >= 0 && y < height
            ? terrainInGrid[x, y] : null;
    }

    // Resolve a world point against this generated grid, including its container transform.
    public TerrainTile GetTileAtWorldPosition(Vector3 position)
    {
        if (!IsGenerated || gridContainer == null) return null;
        Vector3 local = gridContainer.transform.InverseTransformPoint(position);
        return GetTile(Mathf.FloorToInt(local.x / xOffset + 0.5f), Mathf.FloorToInt(local.y / yOffset + 0.5f));
    }

    void Start()
    {
        // Validate before allocating or instantiating to avoid a partial map.
        if (!ValidateSettings()) return;

        // Get a random number to base our map on every time the scene starts.
        seed = Random.Range(0, 100000);
        tilesInGrid = new GameObject[width, height];
        terrainInGrid = new TerrainTile[width, height];
        for (int i = 0; i < width; i++) {
            for (int k = 0; k < height; k++) {
                MakeTile(i, k);
            }
        }
        CalculateGeneratedBounds();
        SpawnPalms();
        SpawnWatchers();
        IsGenerated = true;
        Debug.Log($"GridMaker: Generated {width} x {height} tiles with seed {seed}.", this);
        GenerationCompleted?.Invoke(this);
    }

    void CalculateGeneratedBounds()
    {
        bool first = true;
        Bounds bounds = default;
        foreach (TerrainTile tile in terrainInGrid) {
            // Ground only: Palm/Watcher art must not enlarge the playable map.
            Bounds ground = tile.GetComponent<SpriteRenderer>().bounds;
            if (first) { bounds = ground; first = false; }
            else bounds.Encapsulate(ground);
        }
        GeneratedBounds = bounds;
    }

    bool ValidateSettings()
    {
        if (tilePrefab == null) {
            Debug.LogError("GridMaker: Assign tilePrefab in the Inspector.", this);
            return false;
        }
        if (gridContainer == null) {
            Debug.LogError("GridMaker: Assign a scene GameObject to gridContainer in the Inspector.", this);
            return false;
        }
        if (!tilePrefab.TryGetComponent<SpriteRenderer>(out var spriteRenderer)) {
            Debug.LogError("GridMaker: tilePrefab needs a SpriteRenderer on its root GameObject.", this);
            return false;
        }
        if (spriteRenderer.sprite == null || !spriteRenderer.enabled || !tilePrefab.activeSelf) {
            Debug.LogError("GridMaker: tilePrefab must be active with an enabled SpriteRenderer and an assigned sprite.", this);
            return false;
        }
        if (!gridContainer.scene.IsValid() || !gridContainer.activeInHierarchy) {
            Debug.LogError("GridMaker: gridContainer must be an active GameObject in the scene.", this);
            return false;
        }
        if (width <= 0 || height <= 0 || !IsPositiveFinite(xOffset) ||
            !IsPositiveFinite(yOffset) || !IsPositiveFinite(noiseScale)) {
            Debug.LogError("GridMaker: width and height must be positive; xOffset, yOffset and noiseScale must be positive finite values.", this);
            return false;
        }
        if (grassColor.a <= 0f || forestColor.a <= 0f || waterColor.a <= 0f ||
            mountainColor.a <= 0f || sandColor.a <= 0f) {
            Debug.LogWarning("GridMaker: A terrain color has zero alpha and will be invisible. Set each terrain color's alpha to 1 in the Inspector.", this);
        }
        if (watchRange < 0 || watcherCount < 0 || watcherMinSpacing < 0 || float.IsNaN(palmSpawnChance) || palmSpawnChance < 0f || palmSpawnChance > 1f) {
            Debug.LogError("GridMaker: watchRange, watcherCount and watcherMinSpacing must be non-negative; palmSpawnChance must be between 0 and 1.", this);
            return false;
        }
        return true;
    }

    static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    // Creates one cell.
    void MakeTile(int i, int k)
    {
        // Parent on creation so position, rotation and scale follow the container.
        GameObject newTile = Instantiate(tilePrefab, gridContainer.transform, false);
        newTile.name = $"Tile ({i}, {k})";
        SpriteRenderer spriteRenderer = newTile.GetComponent<SpriteRenderer>();
        TerrainType terrainType;

        // Use Perlin noise to smoothly generate a number between 0 and 1.
        float noise = Mathf.Clamp01(Mathf.PerlinNoise((float)(seed + i) / noiseScale, (float)(k + seed) / noiseScale));
        if (noise < 0.3f) {
            terrainType = TerrainType.Grass;
            spriteRenderer.color = grassColor;
        } else if (noise >= 0.3f && noise < 0.5f) {
            terrainType = TerrainType.Forest;
            spriteRenderer.color = forestColor;
        } else if (noise >= 0.5f && noise < 0.7f) {
            terrainType = TerrainType.Water;
            spriteRenderer.color = waterColor;
        } else if (noise >= 0.7f && noise < 0.8f) {
            terrainType = TerrainType.Mountain;
            spriteRenderer.color = mountainColor;
        } else {
            terrainType = TerrainType.Sand;
            spriteRenderer.color = sandColor;
        }

        // Square sprites need full row spacing. Keep all tiles on local Z = 0.
        newTile.transform.localPosition = new Vector3(i * xOffset, k * yOffset, 0f);
        tilesInGrid[i, k] = newTile;
        TerrainTile tile = newTile.GetComponent<TerrainTile>();
        if (tile == null) tile = newTile.AddComponent<TerrainTile>();
        tile.Initialize(terrainType, i, k, new Vector2(xOffset, yOffset));
        terrainInGrid[i, k] = tile;
    }

    void SpawnPalms()
    {
        if (palmSpawnChance <= 0f) return;
        if (!ValidateSpawnPrefab(palmTreePrefab, "palmTreePrefab")) return;
        bool hasSolidCollider = false;
        foreach (Collider2D collider in palmTreePrefab.GetComponentsInChildren<Collider2D>()) {
            if (collider.enabled && !collider.isTrigger && collider.gameObject.activeSelf) {
                hasSolidCollider = true;
                break;
            }
        }
        if (!hasSolidCollider) {
            Debug.LogWarning("GridMaker: palmTreePrefab needs an enabled, non-trigger Collider2D. Palm spawning skipped.", this);
            return;
        }
        foreach (TerrainTile tile in terrainInGrid) {
            if (tile.terrainType == TerrainType.Forest && (palmSpawnChance >= 1f || Random.value < palmSpawnChance)) {
                SpawnOnTile(palmTreePrefab, tile, "Palm Tree");
            }
        }
    }

    void SpawnWatchers()
    {
        if (debugWatcherGeneration) Debug.Log($"GridMaker: SpawnWatchers after terrain/Palms; requested={watcherCount}, range={watchRange}, preferred spacing={watcherMinSpacing}; watcherPrefab={watcherPrefab}, watchedIndicatorPrefab={watchedIndicatorPrefab}.", this);
        if (watcherCount == 0) {
            if (debugWatcherGeneration) Debug.Log("GridMaker: Spawned 0/0 Watchers.", this);
            return;
        }
        if (!ValidateSpawnPrefab(watcherPrefab, "watcherPrefab")) {
            Debug.LogWarning($"GridMaker: Spawned 0/{watcherCount} Watchers; Watcher prefab is unavailable.", this);
            return;
        }
        var grassAdjacent = new List<TerrainTile>();
        var sandAdjacent = new List<TerrainTile>();
        var nearbyDry = new List<TerrainTile>();
        // Keep fallback Water inside the Watcher's existing watch area.
        int nearbyRange = Mathf.Min(3, Mathf.Max(1, watchRange));
        foreach (TerrainTile tile in terrainInGrid) {
            if (tile.terrainType != TerrainType.Grass && tile.terrainType != TerrainType.Sand) continue;
            if (HasAdjacentWater(tile.gridX, tile.gridY)) {
                if (tile.terrainType == TerrainType.Grass) grassAdjacent.Add(tile);
                else sandAdjacent.Add(tile);
            } else if (HasWaterWithinDistance(tile.gridX, tile.gridY, nearbyRange)) nearbyDry.Add(tile);
        }
        if (debugWatcherGeneration) Debug.Log($"GridMaker: Watcher candidates: Grass adjacent={grassAdjacent.Count}, Sand adjacent={sandAdjacent.Count}, Grass/Sand within {nearbyRange} cells={nearbyDry.Count}.", this);
        var tiers = new List<TerrainTile>[] { grassAdjacent, sandAdjacent, nearbyDry };
        if (grassAdjacent.Count + sandAdjacent.Count + nearbyDry.Count == 0) {
            Debug.LogWarning($"GridMaker: No Grass/Sand tile lies within {nearbyRange} cells of Water, including shoreline fallbacks. Cannot place a Watcher without changing this map.", this);
            Debug.LogWarning($"GridMaker: Spawned 0/{watcherCount} Watchers; Perlin terrain was preserved.", this);
            return;
        }
        bool showIndicators = watchedIndicatorPrefab != null &&
            ValidateSpawnPrefab(watchedIndicatorPrefab, "watchedIndicatorPrefab");
        for (int i = 0; i < watcherCount; i++) {
            TerrainTile watcherTile = ChooseWatcherTile(tiers, true);
            if (watcherTile == null) {
                watcherTile = ChooseWatcherTile(tiers, false);
                if (watcherTile != null && debugWatcherGeneration)
                    Debug.Log($"GridMaker: Relaxing preferred Watcher spacing for spawn {i + 1}; no spaced candidate remains. Tile reuse is still prohibited.", this);
            }
            if (watcherTile == null) break;
            watcherSpawnTiles.Add(watcherTile);
            GameObject watcherObject = SpawnOnTile(watcherPrefab, watcherTile, $"Watcher {i + 1}");
            Watcher watcher = watcherObject.GetComponent<Watcher>();
            if (watcher == null) watcher = watcherObject.AddComponent<Watcher>();
            generatedWatchers.Add(watcher);
            // Retain the original first-Watcher accessor for existing integrations.
            if (GeneratedWatcher == null) GeneratedWatcher = watcher;
            watcher.Initialize(this, watcherTile, watchRange, showIndicators ? watchedIndicatorPrefab : null);
            if (debugWatcherGeneration)
                Debug.Log($"GridMaker: {watcher.name} spawned on {watcherTile.terrainType} ({watcherTile.gridX}, {watcherTile.gridY}), world {watcher.transform.position}; watching {watcher.WatchedTileCount} Water tiles within range {watchRange}.", watcher);
        }
        if (generatedWatchers.Count < watcherCount)
            Debug.LogWarning($"GridMaker: Only {generatedWatchers.Count}/{watcherCount} Watchers could spawn on distinct valid tiles. Perlin terrain was preserved.", this);
        if (debugWatcherGeneration) {
            int overlapping = 0;
            foreach (var tile in terrainInGrid) {
                if (tile.ActiveWatcherCount <= 1) continue;
                overlapping++;
                Debug.Log($"GridMaker: Overlapping Water ({tile.gridX}, {tile.gridY}) activeWatcherCount={tile.ActiveWatcherCount}.", tile);
            }
            Debug.Log($"GridMaker: Spawned {generatedWatchers.Count}/{watcherCount} Watchers; {overlapping} Water tiles watched by multiple Watchers.", this);
        }
    }

    TerrainTile ChooseWatcherTile(List<TerrainTile>[] tiers, bool requireSpacing)
    {
        foreach (var tier in tiers) {
            var candidates = new List<TerrainTile>();
            foreach (var tile in tier) {
                if (watcherSpawnTiles.Contains(tile)) continue;
                if (requireSpacing && NearestWatcherDistance(tile) < watcherMinSpacing) continue;
                candidates.Add(tile);
            }
            if (candidates.Count == 0) continue;
            // Start randomly; subsequent spawns spread across the available shoreline.
            if (watcherSpawnTiles.Count == 0) return candidates[Random.Range(0, candidates.Count)];
            int farthest = -1;
            var spread = new List<TerrainTile>();
            foreach (var tile in candidates) {
                int distance = NearestWatcherDistance(tile);
                if (distance > farthest) { farthest = distance; spread.Clear(); }
                if (distance == farthest) spread.Add(tile);
            }
            return spread[Random.Range(0, spread.Count)];
        }
        return null;
    }

    int NearestWatcherDistance(TerrainTile tile)
    {
        int distance = int.MaxValue;
        foreach (var occupied in watcherSpawnTiles)
            distance = Mathf.Min(distance, Mathf.Abs(tile.gridX - occupied.gridX) + Mathf.Abs(tile.gridY - occupied.gridY));
        return distance;
    }

    bool HasWaterWithinDistance(int x, int y, int distance)
    {
        for (int dx = -distance; dx <= distance; dx++) {
            int verticalRange = distance - Mathf.Abs(dx);
            for (int dy = -verticalRange; dy <= verticalRange; dy++) {
                if (IsWater(x + dx, y + dy)) return true;
            }
        }
        return false;
    }

    bool HasAdjacentWater(int x, int y)
    {
        return IsWater(x - 1, y) || IsWater(x + 1, y) || IsWater(x, y - 1) || IsWater(x, y + 1);
    }

    bool IsWater(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height &&
            terrainInGrid[x, y].terrainType == TerrainType.Water;
    }

    bool ValidateSpawnPrefab(GameObject prefab, string fieldName)
    {
        if (prefab == null) {
            Debug.LogWarning($"GridMaker: Assign {fieldName} in the Inspector to enable this feature.", this);
            return false;
        }
        SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>();
        if (!prefab.activeSelf || renderer == null || !renderer.enabled || renderer.sprite == null) {
            Debug.LogWarning($"GridMaker: {fieldName} must be active with an enabled SpriteRenderer and an assigned sprite. Spawning skipped.", this);
            return false;
        }
        return true;
    }

    GameObject SpawnOnTile(GameObject prefab, TerrainTile tile, string objectName)
    {
        GameObject instance = Instantiate(prefab, tile.transform, false);
        instance.name = objectName;
        instance.transform.localPosition = Vector3.zero;
        // Render decorations above their ground without depending on prefab Z offsets.
        SpriteRenderer ground = tile.GetComponent<SpriteRenderer>();
        foreach (SpriteRenderer renderer in instance.GetComponentsInChildren<SpriteRenderer>()) {
            renderer.sortingLayerID = ground.sortingLayerID;
            renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, ground.sortingOrder + 1);
        }
        return instance;
    }
}
