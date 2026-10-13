using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class Watcher : MonoBehaviour
{
    [Min(0.01f)] public float blindDuration = 5f;
    [Header("Sound")]
    public AudioClip closedEyeClip;
    [SerializeField] AudioSource audioSource;
    [SerializeField] bool isBlinded;
    GridMaker grid;
    TerrainTile origin;
    int range;
    GameObject indicatorPrefab;
    float recoverAt;
    readonly List<TerrainTile> watchedTiles = new List<TerrainTile>();

    public bool IsBlinded => isBlinded;
    public int WatchedTileCount => watchedTiles.Count;
    public GridMaker Grid => grid;
    public TerrainTile OriginTile => origin;

    void Awake()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource != null) {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
        }
    }

    public void Initialize(GridMaker gridMaker, TerrainTile originTile, int watchRange, GameObject indicator)
    {
        ReleaseWatching();
        grid = gridMaker;
        origin = originTile;
        range = watchRange;
        indicatorPrefab = indicator;
        isBlinded = false;
        RefreshWatching();
    }

    public void Blind(float duration)
    {
        if (duration <= 0f || float.IsNaN(duration) || float.IsInfinity(duration)) return;
        bool enteredBlindState = !isBlinded;
        recoverAt = Mathf.Max(recoverAt, Time.time + duration);
        isBlinded = true;
        ReleaseWatching();
        if (enteredBlindState && closedEyeClip != null && audioSource != null && audioSource.isActiveAndEnabled)
            audioSource.PlayOneShot(closedEyeClip);
        Debug.Log($"{name}: Watcher blinded for {duration:0.##} seconds; its watch registrations were removed.", this);
    }

    void Update()
    {
        if (!isBlinded || Time.time < recoverAt) return;
        isBlinded = false;
        RefreshWatching();
        Debug.Log($"{name}: Watcher recovered; watching {WatchedTileCount} remaining Water tiles.", this);
    }

    void OnEnable()
    {
        if (isBlinded && Time.time >= recoverAt) isBlinded = false;
        RefreshWatching();
    }

    void OnDisable() { ReleaseWatching(); }

    public void RefreshWatching()
    {
        ReleaseWatching();
        if (isBlinded || !isActiveAndEnabled || grid == null || origin == null) return;
        foreach (TerrainTile tile in grid.Tiles) {
            int distance = Mathf.Abs(tile.gridX - origin.gridX) + Mathf.Abs(tile.gridY - origin.gridY);
            if (tile.terrainType != TerrainType.Water || distance > range) continue;
            watchedTiles.Add(tile);
            tile.SetWatchSource(this, true, indicatorPrefab);
        }
    }

    void ReleaseWatching()
    {
        foreach (TerrainTile tile in watchedTiles) {
            if (tile != null) tile.SetWatchSource(this, false, null);
        }
        watchedTiles.Clear();
    }

    public bool ContainsWorldPoint(Vector3 point)
    {
        foreach (var renderer in GetComponentsInChildren<SpriteRenderer>()) {
            Bounds bounds = renderer.bounds;
            if (renderer.enabled && point.x >= bounds.min.x && point.x <= bounds.max.x &&
                point.y >= bounds.min.y && point.y <= bounds.max.y) return true;
        }
        return false;
    }
}
