using System.Collections.Generic;
using UnityEngine;

public enum TerrainType
{
    Grass,
    Forest,
    Water,
    Mountain,
    Sand,
    EggPlatform
}

[RequireComponent(typeof(SpriteRenderer))]
public class TerrainTile : MonoBehaviour
{
    [SerializeField] TerrainType type;
    [SerializeField] int x;
    [SerializeField] int y;
    [SerializeField] bool watched;
    [SerializeField] Color eggPlatformColor = new Color(1f, 0.95f, 0.7f, 1f);
    [SerializeField] BoxCollider2D terrainBlocker;
    readonly HashSet<Watcher> watchSources = new HashSet<Watcher>();
    bool manualWatch;
    GameObject watchedIndicator;

    public TerrainType terrainType => type;
    public int gridX => x;
    public int gridY => y;
    public bool isWatched => watched;
    public bool IsWalkable => type == TerrainType.Grass || type == TerrainType.Sand ||
        type == TerrainType.Forest || type == TerrainType.EggPlatform;

    public void Initialize(TerrainType terrainType, int gridX, int gridY, Vector2 cellSize = default)
    {
        type = terrainType;
        x = gridX;
        y = gridY;
        watched = false;
        manualWatch = false;
        watchSources.Clear();
        if (watchedIndicator != null) watchedIndicator.SetActive(false);
        if (terrainBlocker == null) terrainBlocker = gameObject.AddComponent<BoxCollider2D>();
        // Account for authored tile scale: the blocker fills the grid cell in container units.
        Vector3 scale = transform.localScale;
        Vector2 size = cellSize == Vector2.zero ? Vector2.one : cellSize;
        terrainBlocker.size = new Vector2(size.x / Mathf.Max(Mathf.Abs(scale.x), 0.001f),
            size.y / Mathf.Max(Mathf.Abs(scale.y), 0.001f));
        terrainBlocker.offset = Vector2.zero;
        terrainBlocker.isTrigger = false;
        terrainBlocker.enabled = !IsWalkable;
    }

    public void SetWatched(bool value)
    {
        manualWatch = value && type == TerrainType.Water;
        RefreshWatchedState(null);
    }

    public void SetWatchSource(Watcher source, bool active, GameObject indicatorPrefab)
    {
        if (source == null) return;
        if (active && type == TerrainType.Water) watchSources.Add(source);
        else watchSources.Remove(source);
        RefreshWatchedState(indicatorPrefab);
    }

    void RefreshWatchedState(GameObject indicatorPrefab)
    {
        watched = type == TerrainType.Water && (manualWatch || watchSources.Count > 0);
        if (watched && watchedIndicator == null && indicatorPrefab != null) {
            watchedIndicator = Instantiate(indicatorPrefab, transform, false);
            watchedIndicator.name = "Watched Water Indicator";
            watchedIndicator.transform.localPosition = Vector3.zero;
            var ground = GetComponent<SpriteRenderer>();
            foreach (var renderer in watchedIndicator.GetComponentsInChildren<SpriteRenderer>()) {
                renderer.sortingLayerID = ground.sortingLayerID;
                renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, ground.sortingOrder + 1);
            }
        }
        if (watchedIndicator != null) watchedIndicator.SetActive(watched);
    }

    public bool TryCreateEggPlatform()
    {
        // Check at impact time, including when another egg has already converted this tile.
        if (type != TerrainType.Water) return false;
        if (watched) {
            Debug.Log("This water is being watched.", this);
            return false;
        }
        type = TerrainType.EggPlatform;
        watched = false;
        manualWatch = false;
        watchSources.Clear();
        if (watchedIndicator != null) watchedIndicator.SetActive(false);
        if (terrainBlocker != null) terrainBlocker.enabled = false;
        GetComponent<SpriteRenderer>().color = eggPlatformColor;
        return true;
    }
}
