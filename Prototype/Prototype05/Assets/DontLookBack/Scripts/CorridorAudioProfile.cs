using UnityEngine;

[CreateAssetMenu(menuName = "Don't Look Back/Corridor Audio Profile")]
public sealed class CorridorAudioProfile : ScriptableObject
{
    public enum Rhythm { Echo, Mismatch, Pursuit }
    public Rhythm rhythm;
    public AudioClip[] clips = new AudioClip[0];
    [Range(0, 1)] public float volume = .85f;
    public Vector2 pitchRange = new Vector2(.85f, .95f);
    [Min(0)] public float activationMovingSeconds = 2;
    [Min(0)] public float stopDelaySeconds = 1;
    [Min(.15f)] public float stepInterval = .55f;
    public bool subtleEcho;
    [Range(10, 500)] public float reverbDelayMilliseconds = 120;
    [Range(0, .5f)] public float reverbWetMix = .16f;
    [HideInInspector] public float strideMeters = 1;
    [HideInInspector] public float echoDelay = .5f;
    [Min(0)] public float timingJitter = .08f;
    [HideInInspector] public Vector2 resumeDelay = new Vector2(.7f, 1.4f);
    [Min(.5f)] public float distanceBehind = 2.2f;
    public Vector2 lateralOffset = new Vector2(-.25f, .25f);
    [Min(0)] public float height = .15f;
    [HideInInspector] public float extraStepsChance = .6f;
    [HideInInspector] public Vector2Int extraSteps = new Vector2Int(1, 3);
    [HideInInspector] public float extraStepInterval = .55f;
    [Min(.5f)] public float pursuitMinimumDistance = 1.5f;
    [Min(0)] public float pursuitApproachPerSecond = .08f;
}
