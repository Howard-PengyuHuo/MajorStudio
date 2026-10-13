using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class BlackDoorTransition : MonoBehaviour
{
    public CorridorManager manager;
    public DoorInteraction door;
    [Tooltip("+Z points out of the corridor, into the darkness.")]
    public Transform entranceFrame;
    [Min(0.01f)] public float crossingDistance = 0.06f;
    bool armed;
    readonly HashSet<Collider> occupants = new HashSet<Collider>();
    public BoxCollider Trigger => GetComponent<BoxCollider>();
    public bool IsEntranceArmed => armed;
    void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
    void OnEnable()
    {
        if (!manager) manager = FindFirstObjectByType<CorridorManager>();
        if (manager) manager.RegisterDoor(this);
    }
    void OnDisable() { if (manager) manager.UnregisterDoor(this); occupants.Clear(); armed = false; }
    bool IsPlayer(Collider other) => manager && manager.loop && manager.loop.player && other.transform.IsChildOf(manager.loop.player);
    public float SignedDistance(Vector3 position) => entranceFrame ? Vector3.Dot(position - entranceFrame.position, entranceFrame.forward) : 0;
    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        occupants.Add(other);
        Consider(other);
    }
    void OnTriggerStay(Collider other)
    {
        if (!IsPlayer(other)) return;
        occupants.Add(other);
        Consider(other);
    }
    void Consider(Collider other)
    {
        if (!entranceFrame || !door || !door.IsOpen || !manager.CanEnter(this)) return;
        float side = SignedDistance(manager.loop.player.position);
        if (!armed && side < -0.01f) armed = true;
        if (armed && side >= crossingDistance) { manager.TryBeginTransition(this); armed = false; }
    }
    void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        if (manager.OverlapsDoor(this)) return; // Ignore actor-recreation Exit callbacks while still inside.
        occupants.Remove(other);
        if (occupants.Count == 0) { armed = false; manager.ReleaseArrivalBlock(this); }
    }
    public void Disarm() { armed = false; }
}
