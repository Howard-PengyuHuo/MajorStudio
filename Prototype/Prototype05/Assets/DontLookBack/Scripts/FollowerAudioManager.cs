using System;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(1300)]
public sealed class FollowerAudioManager : MonoBehaviour
{
    public CorridorLoopManager loop;
    public CorridorManager doors;
    public AudioSource footstepSource;
    public GroundCheck groundCheck;
    public FirstPersonAudio playerFootsteps;
    [HideInInspector] public bool usePlayerStepTimingForEcho; // Migrated: cadence is now independent.
    public CorridorAudioProfile fallbackProfile;
    public AudioEchoFilter echoFilter;
    [Min(0)] public float minimumMovingSpeed = .12f;
    [Min(1)] public float teleportDistance = 2;
    [Range(90,175)] public float lookBackAngle = 110;
    [Range(5,70)] public float forwardResumeAngle = 40;
    [Min(0)] public float silenceFadeSeconds = .06f;
    [Min(.05f)] public float maximumStepSeconds = .45f;
    public bool debugLogs;
    public CorridorAudioProfile CurrentProfile { get; private set; }
    public int PendingSteps => following ? 1 : 0;
    public int PlayedFootsteps { get; private set; }
    public bool LookingBack { get; private set; }
    public float MovingSeconds { get; private set; }
    public Vector3 TravelDirection { get; private set; } = Vector3.forward;
    public bool IsFollowing => following;
    public event Action<AudioClip> FootstepPlayed;
    Vector3 previousPosition;
    CorridorDestination environment;
    float clock, nextStep, stopAt = -1, voiceElapsed, lateral, walkingDuration;
    bool following, fading;
    AudioClip previousClip;
    void OnEnable()
    {
        if (!Application.isPlaying || !loop || !doors || !footstepSource) return;
        footstepSource.playOnAwake = false; footstepSource.loop = false;
        footstepSource.spatialBlend = 1; footstepSource.dopplerLevel = 0;
        loop.PlayerTeleported += ApplyCorridor;
        ApplyCorridor(loop.CurrentCorridor);
    }
    public void ApplyCorridor(int index)
    {
        environment = loop.CurrentEnvironment;
        if (!environment && index >= 0 && index < loop.corridors.Length)
            environment = loop.corridors[index].environment.GetComponent<CorridorDestination>();
        // An explicitly registered environment with no clips stays silent; it never
        // inherits another environment's profile merely because its name is similar.
        CurrentProfile = environment ? environment.audioProfile : fallbackProfile;
        TravelDirection = loop.player ? Vector3.ProjectOnPlane(loop.player.forward, Vector3.up).normalized : Vector3.forward;
        ResetTransient();
        if (debugLogs) Debug.Log("Follower profile: " + (CurrentProfile ? CurrentProfile.name : "unassigned (silent)"), this);
    }
    public void ResetTransient()
    {
        following = fading = LookingBack = false; MovingSeconds = walkingDuration = 0; stopAt = -1;
        previousPosition = loop && loop.player ? loop.player.position : Vector3.zero;
        if (footstepSource) { footstepSource.Stop(); footstepSource.clip = null; }
        if (echoFilter) echoFilter.enabled = false;
        nextStep = clock; PositionSource();
    }
    // Legacy timing signal intentionally unused: a 2-second activation delay must
    // not depend on whether the player's current clip has finished playing.
    public void NotifyPlayerStep() { }
    void LateUpdate()
    {
        if (!loop || !loop.player || !loop.view || !doors || !footstepSource) return;
        if (environment != loop.CurrentEnvironment) ApplyCorridor(loop.CurrentCorridor);
        bool grounded = groundCheck ? groundCheck.isGrounded : Physics.Raycast(loop.player.position + Vector3.up * .1f, Vector3.down, .3f, loop.obstructionMask, QueryTriggerInteraction.Ignore);
        SampleMotion(loop.player.position, loop.view.forward, Time.deltaTime, grounded, doors.IsBusy || loop.TransitionSuspended || Time.timeScale <= 0);
    }
    public void SampleMotion(Vector3 position, Vector3 gaze, float deltaTime, bool grounded, bool suspended)
    {
        if (!CurrentProfile || !footstepSource) return;
        clock += Mathf.Max(0, deltaTime);
        Vector3 displacement = Vector3.ProjectOnPlane(position - previousPosition, Vector3.up); previousPosition = position;
        if (suspended || displacement.magnitude > teleportDistance) { ResetTransient(); previousPosition = position; return; }
        if (deltaTime <= 0) return;
        Vector3 forward = loop.InMainCorridor ? loop.corridors[loop.CurrentCorridor].startPoint.forward : TravelDirection;
        Vector3 horizontalGaze = Vector3.ProjectOnPlane(gaze, Vector3.up);
        float angle = horizontalGaze.sqrMagnitude > .01f ? Vector3.Angle(horizontalGaze, forward) : 0;
        if (!LookingBack && angle >= lookBackAngle)
        { LookingBack = true; following = false; MovingSeconds = 0; stopAt = -1; fading = true; if (echoFilter) echoFilter.enabled = false; }
        else if (LookingBack && angle <= forwardResumeAngle)
        { LookingBack = false; MovingSeconds = 0; following = false; stopAt = -1; }
        bool moving = grounded && displacement.magnitude / deltaTime >= minimumMovingSpeed;
        if (moving && !LookingBack) { TravelDirection = displacement.normalized; walkingDuration += deltaTime; }
        PositionSource(); UpdateVoice(deltaTime);
        if (LookingBack) return;
        if (moving)
        {
            stopAt = -1; // Resuming during the grace period cancels the pending stop.
            if (!following)
            {
                MovingSeconds += deltaTime;
                if (MovingSeconds + .0001f >= Mathf.Max(0, CurrentProfile.activationMovingSeconds))
                { following = true; nextStep = clock; }
            }
        }
        else if (following)
        {
            if (stopAt < 0) stopAt = clock + Mathf.Max(0, CurrentProfile.stopDelaySeconds);
            if (clock + .0001f >= stopAt)
            { following = false; MovingSeconds = 0; fading = true; if (echoFilter) echoFilter.enabled = false; }
        }
        else MovingSeconds = 0; // Activation requires consecutive movement.
        if (following && grounded && clock >= nextStep)
        {
            if (!footstepSource.isPlaying && !fading) PlayStep();
            // Never catch up a backlog of missed beats or stack voices.
            float jitter = CurrentProfile.timingJitter;
            nextStep = clock + Mathf.Max(.2f, CurrentProfile.stepInterval + UnityEngine.Random.Range(-jitter, jitter));
        }
    }
    void PositionSource()
    {
        if (!CurrentProfile || !footstepSource || !loop || !loop.player) return;
        float distance = Mathf.Max(.5f, CurrentProfile.distanceBehind);
        if (CurrentProfile.rhythm == CorridorAudioProfile.Rhythm.Pursuit)
            distance = Mathf.Max(CurrentProfile.pursuitMinimumDistance, distance - walkingDuration * CurrentProfile.pursuitApproachPerSecond);
        footstepSource.transform.position = loop.player.position - TravelDirection * distance
            + Vector3.Cross(Vector3.up, TravelDirection).normalized * lateral + Vector3.up * CurrentProfile.height;
    }
    void PlayStep()
    {
        var clips = CurrentProfile.clips; if (clips == null || clips.Length == 0) return;
        int offset = UnityEngine.Random.Range(0, clips.Length); AudioClip clip = null;
        for (int i = 0; i < clips.Length; i++) if (clips[(offset+i)%clips.Length] && clips[(offset+i)%clips.Length] != previousClip) { clip = clips[(offset+i)%clips.Length]; break; }
        if (!clip) foreach (var candidate in clips) if (candidate) { clip = candidate; break; }
        if (!clip) return;
        previousClip = clip; lateral = UnityEngine.Random.Range(CurrentProfile.lateralOffset.x, CurrentProfile.lateralOffset.y); PositionSource();
        if (echoFilter)
        {
            echoFilter.enabled = CurrentProfile.subtleEcho;
            echoFilter.delay = CurrentProfile.reverbDelayMilliseconds; echoFilter.decayRatio = .2f;
            echoFilter.wetMix = CurrentProfile.reverbWetMix; echoFilter.dryMix = 1;
        }
        footstepSource.clip = clip; footstepSource.volume = Mathf.Clamp01(CurrentProfile.volume);
        footstepSource.pitch = UnityEngine.Random.Range(Mathf.Clamp(CurrentProfile.pitchRange.x,.5f,2), Mathf.Clamp(CurrentProfile.pitchRange.y,.5f,2));
        voiceElapsed = 0; fading = false; footstepSource.Play(); PlayedFootsteps++; FootstepPlayed?.Invoke(clip);
    }
    void UpdateVoice(float dt)
    {
        voiceElapsed += dt;
        if (voiceElapsed >= maximumStepSeconds) fading = true;
        if (!fading) return;
        footstepSource.volume = silenceFadeSeconds <= 0 ? 0 : Mathf.MoveTowards(footstepSource.volume,0,dt / silenceFadeSeconds);
        if (footstepSource.volume <= 0) { footstepSource.Stop(); fading = false; }
    }
    void OnDisable()
    {
        if (loop) loop.PlayerTeleported -= ApplyCorridor;
        following = false; if (footstepSource) footstepSource.Stop(); if (echoFilter) echoFilter.enabled = false;
    }
}
