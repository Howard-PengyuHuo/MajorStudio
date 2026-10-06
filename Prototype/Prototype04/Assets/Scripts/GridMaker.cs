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
    [Tooltip("Manhattan distance: abs(dx) + abs(dy), in grid cells.")]
    [Min(0)] public int watchRange = 3;
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
        SpawnWatcher();
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
        if (watchRange < 0 || float.IsNaN(palmSpawnChance) || palmSpawnChance < 0f || palmSpawnChance > 1f) {
            Debug.LogError("GridMaker: watchRange must be non-negative and palmSpawnChance must be between 0 and 1.", this);
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

    void SpawnWatcher()
    {
        if (debugWatcherGeneration) Debug.Log($"GridMaker: SpawnWatcher after terrain/Palms; watcherPrefab={watcherPrefab}, watchedIndicatorPrefab={watchedIndicatorPrefab}.", this);
        if (!ValidateSpawnPrefab(watcherPrefab, "watcherPrefab")) return;
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
        var candidates = grassAdjacent.Count > 0 ? grassAdjacent : sandAdjacent.Count > 0 ? sandAdjacent : nearbyDry;
        if (candidates.Count == 0) {
            Debug.LogWarning($"GridMaker: No Grass/Sand tile lies within {nearbyRange} cells of Water, including shoreline fallbacks. Cannot place a Watcher without changing this map.", this);
            return;
        }
        TerrainTile watcherTile = candidates[Random.Range(0, candidates.Count)];
        GameObject watcherObject = SpawnOnTile(watcherPrefab, watcherTile, "Watcher");
        GeneratedWatcher = watcherObject.GetComponent<Watcher>();
        if (GeneratedWatcher == null) GeneratedWatcher = watcherObject.AddComponent<Watcher>();
        bool showIndicators = watchedIndicatorPrefab != null &&
            ValidateSpawnPrefab(watchedIndicatorPrefab, "watchedIndicatorPrefab");
        GeneratedWatcher.Initialize(this, watcherTile, watchRange, showIndicators ? watchedIndicatorPrefab : null);
        Debug.Log($"GridMaker: Watcher spawned successfully on {watcherTile.terrainType} at ({watcherTile.gridX}, {watcherTile.gridY}); {GeneratedWatcher.WatchedTileCount} Water tiles watched within Manhattan range {watchRange}.", this);
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
