using UnityEngine;

// Put this on a loaded corridor root or a self-contained corridor prefab root.
public sealed class CorridorDestination : MonoBehaviour
{
    public enum EnvironmentKind { MainLookBack, DoorPrefab }
    public EnvironmentKind kind;
    [Tooltip("Explicit door arrivals; never substituted by the general SpawnPoint.")]
    public DoorExitPoint[] doorExits = new DoorExitPoint[0];
    public Transform spawnPoint;
    public Transform lookBackStart;
    public CorridorAudioProfile audioProfile;
    public Transform Environment => transform;
    public bool HasValidReferences => spawnPoint && spawnPoint.IsChildOf(transform)
        && (!lookBackStart || lookBackStart.IsChildOf(transform));
}
