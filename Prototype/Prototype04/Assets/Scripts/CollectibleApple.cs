using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class CollectibleApple : MonoBehaviour
{
    CollectibleManager manager;
    public TerrainTile SpawnTile { get; private set; }
    public bool IsCollected { get; private set; }

    public void Initialize(CollectibleManager owner, TerrainTile tile)
    {
        manager = owner;
        SpawnTile = tile;
        IsCollected = false;
        GetComponent<CircleCollider2D>().isTrigger = true;
    }

    void Reset() { GetComponent<CircleCollider2D>().isTrigger = true; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsCollected || manager == null || manager.playerSpawner == null) return;
        GameObject player = manager.playerSpawner.SpawnedPlayer;
        if (player == null || !other.transform.IsChildOf(player.transform)) return;
        manager.TryCollect(this);
    }

    public void Consume()
    {
        if (IsCollected) return;
        IsCollected = true;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
