using System;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public sealed class CorridorLoopManager : MonoBehaviour
{
    [Serializable] public class Corridor
    {
        public string name;
        public Transform environment;
        [Tooltip("Ground-level player pivot and forward direction; also the local-space transfer frame.")]
        public Transform startPoint;
    }
    public Corridor[] corridors = new Corridor[2];
    public Transform player;
    public Transform view;
    public FirstPersonLook mouseLook;
    public int initialCorridor;
    public bool spawnAtStart = true;
    public bool debugLogs;
    [Range(100, 175)] public float backwardAngle = 140;
    [Range(5, 70)] public float forwardAngle = 35;
    [Min(0)] public float backwardHoldSeconds = 0.12f;
    [Min(0)] public float forwardHoldSeconds = 0.08f;
    [Min(0)] public float teleportCooldown = 0.4f;
    public LayerMask obstructionMask = ~0;
    public int CurrentCorridor { get; private set; }
    public CorridorDestination CurrentEnvironment { get; private set; }
    public bool InMainCorridor => CurrentCorridor >= 0 && CurrentCorridor < corridors.Length;
    public int LookBackCount { get; private set; }
    public bool AwaitingReturn => awaitingReturn;
    public bool TransitionSuspended { get; private set; }
    public event Action<int> PlayerTeleported;
    bool awaitingReturn, ready, armed;
    float backwardTime, forwardTime, cooldown;
    Rigidbody body;
    CharacterController controller;
    CapsuleCollider capsule;

    void Start() => Initialize(spawnAtStart);
    public void Initialize(bool spawn)
    {
        ready = player && view && corridors != null && corridors.Length == 2;
        if (ready) foreach (var corridor in corridors) ready &= corridor != null && corridor.environment && corridor.startPoint;
        if (ready) ready = corridors[0].environment != corridors[1].environment && Array.TrueForAll(corridors,
            c => !c.environment.GetComponent<CorridorDestination>() || c.environment.GetComponent<CorridorDestination>().kind == CorridorDestination.EnvironmentKind.MainLookBack);
        if (!ready) { Debug.LogError("CorridorLoopManager: assign player, view, corridors and start points.", this); enabled = false; return; }
        body = player.GetComponent<Rigidbody>();
        controller = player.GetComponent<CharacterController>();
        capsule = player.GetComponent<CapsuleCollider>();
        CurrentCorridor = Mathf.Clamp(initialCorridor, 0, corridors.Length - 1);
        CurrentEnvironment = corridors[CurrentCorridor].environment.GetComponent<CorridorDestination>();
        LookBackCount = 0;
        cooldown = 0;
        ResetDetection();
        if (spawn && !MovePlayer(corridors[CurrentCorridor].startPoint.position, corridors[CurrentCorridor].startPoint.rotation))
            Debug.LogError("CorridorLoopManager: start point is obstructed. Move it onto clear floor.", this);
        else if (spawn) PlayerTeleported?.Invoke(CurrentCorridor);
    }
    void LateUpdate()
    {
        if (ready) SampleLook(view.forward, Time.deltaTime);
    }
    // Separate sampling allows deterministic validation without synthesizing input.
    public void SampleLook(Vector3 direction, float deltaTime)
    {
        if (!ready || TransitionSuspended || !InMainCorridor) return;
        if (cooldown > 0) { cooldown -= deltaTime; return; }
        var frame = corridors[CurrentCorridor].startPoint;
        var horizontal = Vector3.ProjectOnPlane(direction, frame.up);
        if (horizontal.sqrMagnitude < 0.04f) { backwardTime = forwardTime = 0; return; } // Vertical gaze is not a yaw turn.
        float angle = Vector3.Angle(frame.forward, horizontal);
        if (!armed)
        {
            forwardTime = angle <= forwardAngle ? forwardTime + deltaTime : 0;
            if (forwardTime >= forwardHoldSeconds) { armed = true; forwardTime = 0; }
            return;
        }
        if (!awaitingReturn)
        {
            backwardTime = angle >= backwardAngle ? backwardTime + deltaTime : 0;
            if (backwardTime >= backwardHoldSeconds) { awaitingReturn = true; backwardTime = 0; }
            return;
        }
        forwardTime = angle <= forwardAngle ? forwardTime + deltaTime : 0;
        if (forwardTime < forwardHoldSeconds) return;
        int nextCount = LookBackCount + 1;
        int next = nextCount == 1 ? CurrentCorridor : (CurrentCorridor + 1) % corridors.Length;
        var destination = corridors[next].startPoint;
        Vector3 position = nextCount == 1 ? frame.position : destination.TransformPoint(frame.InverseTransformPoint(player.position));
        Quaternion rotation = nextCount == 1 ? player.rotation : destination.rotation * Quaternion.Inverse(frame.rotation) * player.rotation;
        if (!MovePlayer(position, rotation))
        {
            // Do not silently lose relative position or put the player through a wall.
            if (debugLogs) Debug.LogWarning("Look-back transfer blocked; check the matching corridor footprint.", this);
            ResetDetection();
            cooldown = teleportCooldown;
            return;
        }
        LookBackCount = nextCount;
        if (next != CurrentCorridor) { CurrentCorridor = next; LookBackCount = 0; }
        CurrentEnvironment = corridors[CurrentCorridor].environment.GetComponent<CorridorDestination>();
        if (debugLogs) Debug.Log($"Don't Look Back: {corridors[CurrentCorridor].name}, completed look-backs={LookBackCount}", this);
        ResetDetection();
        cooldown = teleportCooldown;
        PlayerTeleported?.Invoke(CurrentCorridor);
    }
    void ResetDetection() { awaitingReturn = false; armed = false; backwardTime = forwardTime = 0; }
    public void SuspendForDoor(bool suspend)
    {
        TransitionSuspended = suspend;
        ResetDetection();
        cooldown = teleportCooldown;
    }
    public int RegisterCorridor(Transform environment, Transform startPoint, string corridorName)
    {
        for (int i = 0; i < corridors.Length; i++) if (corridors[i].environment == environment) return i;
        return -1; // Door environments must never extend the main look-back loop.
    }
    public bool EnterFromDoor(int corridorIndex, Transform spawnPoint)
    {
        if (!ready || !spawnPoint || corridorIndex < 0 || corridorIndex >= corridors.Length) return false;
        if (!MovePlayer(spawnPoint.position, spawnPoint.rotation)) return false;
        if (CurrentCorridor != corridorIndex) LookBackCount = 0;
        CurrentCorridor = corridorIndex;
        CurrentEnvironment = corridors[corridorIndex].environment.GetComponent<CorridorDestination>();
        // Arrival always faces the spawn frame; suppress accumulated camera pitch/yaw.
        if (mouseLook) { mouseLook.transform.localRotation = Quaternion.identity; mouseLook.SynchronizeAfterTeleport(); }
        ResetDetection(); cooldown = teleportCooldown;
        if (debugLogs) Debug.Log("Door arrival: " + corridors[CurrentCorridor].name, this);
        PlayerTeleported?.Invoke(CurrentCorridor);
        return true;
    }
    public bool EnterDoorExit(DoorExitPoint exit)
    {
        if (!ready || !exit || !exit.IsValid(this)) return false;
        int mainIndex = exit.environment.kind == CorridorDestination.EnvironmentKind.MainLookBack
            ? Array.FindIndex(corridors, c => c.environment == exit.environment.transform) : -1;
        if (exit.environment.kind == CorridorDestination.EnvironmentKind.MainLookBack && mainIndex < 0) return false;
        if (!MovePlayer(exit.transform.position, exit.transform.rotation)) return false;
        CurrentEnvironment = exit.environment;
        CurrentCorridor = mainIndex; // Restore the main loop only for its exact registered instance.
        LookBackCount = 0;
        if (mouseLook) { mouseLook.transform.localRotation = Quaternion.identity; mouseLook.SynchronizeAfterTeleport(); }
        ResetDetection(); cooldown = teleportCooldown;
        PlayerTeleported?.Invoke(CurrentCorridor);
        return true;
    }
    public bool IsClear(Vector3 position, Quaternion rotation)
    {
        Vector3 center = controller ? controller.center : capsule ? capsule.center : Vector3.up * 0.9f;
        float radius = controller ? controller.radius : capsule ? capsule.radius : 0.3f;
        float height = controller ? controller.height : capsule ? capsule.height : 1.8f;
        float scale = Mathf.Max(Mathf.Abs(player.lossyScale.x), Mathf.Abs(player.lossyScale.z));
        radius *= scale;
        height *= Mathf.Abs(player.lossyScale.y);
        Vector3 worldCenter = position + rotation * Vector3.Scale(center, player.lossyScale);
        Vector3 up = rotation * Vector3.up;
        float segment = Mathf.Max(0, height * 0.5f - radius);
        foreach (var hit in Physics.OverlapCapsule(worldCenter - up * segment, worldCenter + up * segment,
            Mathf.Max(0.01f, radius - 0.025f), obstructionMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(player) || hit.transform == player) continue;
            Collider actor = controller ? (Collider)controller : capsule;
            if (actor && Physics.GetIgnoreCollision(actor, hit)) continue;
            return false;
        }
        return Physics.Raycast(position + up * 0.15f, -up, out var floor, 0.4f, obstructionMask, QueryTriggerInteraction.Ignore)
            && !floor.transform.IsChildOf(player) && Vector3.Dot(floor.normal, up) > 0.7f;
    }
    bool MovePlayer(Vector3 position, Quaternion rotation)
    {
        Physics.SyncTransforms();
        if (!IsClear(position, rotation)) return false;
        bool controllerEnabled = controller && controller.enabled;
        if (controllerEnabled) controller.enabled = false;
        player.SetPositionAndRotation(position, rotation);
        if (body)
        {
            body.position = position;
            body.rotation = rotation;
            if (Application.isPlaying && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
        Physics.SyncTransforms();
        if (controllerEnabled) controller.enabled = true;
        if (mouseLook) mouseLook.SynchronizeAfterTeleport();
        var playerAudio = player.GetComponentInChildren<FirstPersonAudio>();
        if (playerAudio) playerAudio.ResetMotionAudio();
        return true;
    }
}
