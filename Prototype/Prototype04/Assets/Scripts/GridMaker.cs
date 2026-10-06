using UnityEngine;

public class GridMaker : MonoBehaviour
{
    // The prefab instantiated in each grid cell; needs a SpriteRenderer on its root.
    public GameObject tilePrefab;

    // Scene object that holds the grid cells.
    public GameObject gridContainer;

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
    int seed;

    void Start()
    {
        // Validate before allocating or instantiating to avoid a partial map.
        if (!ValidateSettings()) return;

        // Get a random number to base our map on every time the scene starts.
        seed = Random.Range(0, 100000);
        tilesInGrid = new GameObject[width, height];
        for (int i = 0; i < width; i++) {
            for (int k = 0; k < height; k++) {
                MakeTile(i, k);
            }
        }
        Debug.Log($"GridMaker: Generated {width} x {height} tiles with seed {seed}.", this);
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

        // Use Perlin noise to smoothly generate a number between 0 and 1.
        float noise = Mathf.Clamp01(Mathf.PerlinNoise((float)(seed + i) / noiseScale, (float)(k + seed) / noiseScale));
        if (noise < 0.3f) {
            spriteRenderer.color = grassColor;
        } else if (noise >= 0.3f && noise < 0.5f) {
            spriteRenderer.color = forestColor;
        } else if (noise >= 0.5f && noise < 0.7f) {
            spriteRenderer.color = waterColor;
        } else if (noise >= 0.7f && noise < 0.8f) {
            spriteRenderer.color = mountainColor;
        } else if (noise >= 0.8f) {
            spriteRenderer.color = sandColor;
        }

        // Square sprites need full row spacing. Keep all tiles on local Z = 0.
        newTile.transform.localPosition = new Vector3(i * xOffset, k * yOffset, 0f);
        tilesInGrid[i, k] = newTile;
    }
}
