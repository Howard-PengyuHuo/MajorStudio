using UnityEngine;

[DisallowMultipleComponent]
public sealed class DoorExitPoint : MonoBehaviour
{
    public CorridorDestination environment;
    public BlackDoorTransition portal;
    [Min(0)] public float weight = 1;
    public bool IsValid(CorridorLoopManager loop) => environment && environment.gameObject.scene.IsValid()
        && environment.gameObject.activeInHierarchy && isActiveAndEnabled && weight > 0 && !float.IsInfinity(weight) && !float.IsNaN(weight)
        && transform.IsChildOf(environment.transform) && portal && portal.isActiveAndEnabled
        && portal.door && portal.entranceFrame && portal.door.hinge && portal.door.blackVoid
        && portal.SignedDistance(transform.position) < -.1f
        && Vector3.Dot(transform.forward, portal.entranceFrame.forward) < -.8f
        && loop.IsClear(transform.position, transform.rotation);
}
