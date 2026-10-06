using UnityEngine;

[DefaultExecutionOrder(-150)]
public class LevelBoundary : MonoBehaviour
{
    public GridMaker gridMaker;
    [Min(0.1f)] public float wallThickness = 1f;
    public Transform GeneratedWalls { get; private set; }

    void Awake() { if (gridMaker == null) gridMaker = GetComponent<GridMaker>(); }
    void OnEnable() { if (gridMaker != null) gridMaker.GenerationCompleted += Build; }
    void Start() { if (gridMaker != null && gridMaker.IsGenerated) Build(gridMaker); }
    void OnDisable() { if (gridMaker != null) gridMaker.GenerationCompleted -= Build; }

    public void Build(GridMaker grid)
    {
        if (GeneratedWalls != null) return;
        Bounds bounds = grid.GeneratedBounds;
        GeneratedWalls = new GameObject("Generated Map Boundaries").transform;
        // World-aligned colliders independent of scaled scene parents.
        GeneratedWalls.SetParent(transform, true);
        float t = Mathf.Max(0.1f, wallThickness);
        MakeWall("Left Boundary", new Vector2(bounds.min.x - t / 2, bounds.center.y), new Vector2(t, bounds.size.y + t * 2));
        MakeWall("Right Boundary", new Vector2(bounds.max.x + t / 2, bounds.center.y), new Vector2(t, bounds.size.y + t * 2));
        MakeWall("Bottom Boundary", new Vector2(bounds.center.x, bounds.min.y - t / 2), new Vector2(bounds.size.x, t));
        MakeWall("Top Boundary", new Vector2(bounds.center.x, bounds.max.y + t / 2), new Vector2(bounds.size.x, t));
    }

    void MakeWall(string wallName, Vector2 position, Vector2 size)
    {
        var wall = new GameObject(wallName);
        wall.transform.SetParent(GeneratedWalls, false);
        wall.transform.position = position;
        var collider = wall.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = false;
    }
}
