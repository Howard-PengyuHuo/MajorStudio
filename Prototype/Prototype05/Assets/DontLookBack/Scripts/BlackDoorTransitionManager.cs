using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Destination selection only. The existing CorridorManager owns interaction,
// fades, physics locks and trigger cooldowns so no duplicate input handler exists.
[DisallowMultipleComponent]
public sealed class BlackDoorTransitionManager : MonoBehaviour
{
    [Serializable] public class Group
    {
        public string label;
        public CorridorDestination instance;
        public GameObject prefab;
        [Min(0)] public float weight = 1;
    }
    public CorridorManager owner;
    public Group[] groups = Array.Empty<Group>();
    public Vector3 stagingOrigin = new Vector3(600, 0, 0);
    [Min(100)] public float spacing = 200;
    int spawnedCount;
    public DoorExitPoint LastSelectedExit { get; private set; }
    public IEnumerable<CorridorDestination> LoadedEnvironments => groups.Where(g => g != null && g.instance && g.instance.gameObject.scene.IsValid()).Select(g => g.instance);
    CorridorDestination Resolve(Group group)
    {
        if (group.instance && group.instance.gameObject.scene.IsValid()) return group.instance;
        var prefab = group.prefab;
        if (!prefab || prefab.scene.IsValid()) return null;
        var marker = prefab.GetComponent<CorridorDestination>();
        if (!marker || marker.kind != CorridorDestination.EnvironmentKind.DoorPrefab || marker.doorExits.Length == 0
            || prefab.GetComponentsInChildren<Camera>(true).Length > 0 || prefab.GetComponentsInChildren<AudioListener>(true).Length > 0
            || prefab.GetComponentsInChildren<FirstPersonMovement>(true).Length > 0) return null;
        var clone = Instantiate(prefab, stagingOrigin + Vector3.right * spacing * spawnedCount++, prefab.transform.rotation);
        group.instance = clone.GetComponent<CorridorDestination>();
        foreach (var door in clone.GetComponentsInChildren<BlackDoorTransition>(true))
        { if (door.manager) door.manager.UnregisterDoor(door); door.manager = owner; if (door.isActiveAndEnabled) owner.RegisterDoor(door); }
        Physics.SyncTransforms();
        return group.instance;
    }
    public bool TrySelect(out DoorExitPoint selected)
        => TrySelectFrom(owner && owner.loop ? owner.loop.CurrentEnvironment : null, out selected);
    public bool TrySelectFrom(CorridorDestination source, out DoorExitPoint selected)
    {
        selected = null;
        if (!owner || !owner.loop || groups == null || !source || !source.gameObject.scene.IsValid()) return false;
        var destinationKind = source.kind == CorridorDestination.EnvironmentKind.MainLookBack
            ? CorridorDestination.EnvironmentKind.DoorPrefab : CorridorDestination.EnvironmentKind.MainLookBack;
        var valid = new List<(Group group, DoorExitPoint[] exits)>();
        foreach (var group in groups)
        {
            if (group == null || group.weight <= 0 || float.IsNaN(group.weight) || float.IsInfinity(group.weight)) continue;
            var reference = group.instance ? group.instance : group.prefab ? group.prefab.GetComponent<CorridorDestination>() : null;
            if (!reference || reference.kind != destinationKind) continue;
            var marker = Resolve(group);
            if (!marker || marker.kind != destinationKind || !marker.gameObject.activeInHierarchy) continue;
            if (marker.kind == CorridorDestination.EnvironmentKind.MainLookBack
                && !owner.loop.corridors.Any(c => c.environment == marker.transform)) continue;
            // Explicit references determine identity; names and SpawnPoints play no role.
            var exits = marker.doorExits.Where(e => e && e.environment == marker && e.IsValid(owner.loop)).Distinct().ToArray();
            if (exits.Length > 0) valid.Add((group, exits));
        }
        if (valid.Count == 0) return false;
        float roll = UnityEngine.Random.value * valid.Sum(v => v.group.weight);
        var chosen = valid[valid.Count - 1];
        foreach (var entry in valid) { roll -= entry.group.weight; if (roll <= 0) { chosen = entry; break; } }
        roll = UnityEngine.Random.value * chosen.exits.Sum(e => e.weight);
        selected = chosen.exits[chosen.exits.Length - 1];
        foreach (var exit in chosen.exits) { roll -= exit.weight; if (roll <= 0) { selected = exit; break; } }
        LastSelectedExit = selected;
        return true;
    }
}
