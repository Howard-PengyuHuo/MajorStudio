using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class GameManager : MonoBehaviour
{
    public enum EggCountMode { PhysicallyPresent, IntactOnly }
    [Min(0f)] public float gameDuration = 60f;
    [SerializeField] private int brokenEggPenalty = 50;
    [SerializeField] private float allEggsMultiplier = 1.5f;
    public MouseSpawner mouseSpawner;
    [FormerlySerializedAs("scoreText")] public TMP_Text playerScoreText;
    public TMP_Text mouseScoreText;
    [Min(0)] public int firstEggHitScore = 80;
    [Min(0f)] public float secondHitMultiplier = 2f;
    public TMP_Text timerText;
    public TMP_Text eggCountText;
    public TMP_Text finalScoreText;
    public EggCountMode eggCountMode = EggCountMode.PhysicallyPresent;

    [SerializeField] private int score;
    [SerializeField] private int mouseScore;
    [SerializeField] private int totalEggs;
    [SerializeField] private int remainingEggs;
    [SerializeField] private int intactEggs;
    [SerializeField] private int brokenEggs;
    [SerializeField] private int destroyedEggs;
    [SerializeField] private float remainingTime;
    [SerializeField] private bool gameEnded;
    [SerializeField] private int finalScore;
    readonly Dictionary<Egg, Egg.EggState> eggStates = new Dictionary<Egg, Egg.EggState>();
    bool started;

    public bool IsPlaying => started && !gameEnded && remainingTime > 0f;
    public int Score => score;
    public int MouseScore => mouseScore;
    public int TotalEggs => totalEggs;
    public int RemainingEggs => remainingEggs;
    public int BrokenEggs => brokenEggs;
    public int IntactEggs => intactEggs;
    public int DestroyedEggs => destroyedEggs;
    public float RemainingTime => remainingTime;
    public bool GameEnded => gameEnded;
    public int FinalScore => finalScore;

    void Start()
    {
        score = 0;
        mouseScore = 0;
        totalEggs = remainingEggs = intactEggs = brokenEggs = destroyedEggs = 0;
        eggStates.Clear();
        foreach (Egg egg in FindObjectsByType<Egg>(FindObjectsSortMode.None))
        {
            egg.Initialize(this);
            totalEggs++;
            eggStates.Add(egg, egg.State);
            if (egg.IsBroken) brokenEggs++;
            else if (egg.IsDestroyed) destroyedEggs++;
            else intactEggs++;
        }
        remainingEggs = intactEggs + brokenEggs;
        remainingTime = Mathf.Max(0f, gameDuration);
        gameEnded = false;
        started = true;
        UpdateScoreUI();
        UpdateMouseScoreUI();
        UpdateEggUI();
        UpdateTimerUI();
        if (finalScoreText) finalScoreText.text = string.Empty;
        if (remainingTime <= 0f) EndGame();
    }

    void Update() => AdvanceTimer(Time.deltaTime);

    public void AdvanceTimer(float elapsedSeconds)
    {
        if (!started || gameEnded) return;
        remainingTime = Mathf.Max(0f, remainingTime - Mathf.Max(0f, elapsedSeconds));
        UpdateTimerUI();
        if (remainingTime <= 0f) EndGame();
    }

    public void AddScore(int points)
    {
        if (!IsPlaying) return;
        score += points;
        UpdateScoreUI();
    }

    public bool CanDamageEgg(Egg egg)
    {
        return IsPlaying && egg && !egg.IsDestroyed && eggStates.TryGetValue(egg, out var tracked) && tracked == egg.State;
    }

    public void AddMouseScore(int amount)
    {
        if (!IsPlaying) return;
        mouseScore += amount;
        UpdateMouseScoreUI();
    }

    public void RecordEggDamage(Egg egg, Egg.EggState previous)
    {
        if (!IsPlaying || !egg || !eggStates.TryGetValue(egg, out var tracked) || tracked != previous) return;
        int earnedPoints;
        if (previous == Egg.EggState.Intact && egg.State == Egg.EggState.Broken)
        {
            intactEggs--;
            brokenEggs++;
            earnedPoints = firstEggHitScore;
        }
        else if (previous == Egg.EggState.Broken && egg.State == Egg.EggState.Destroyed)
        {
            brokenEggs--;
            destroyedEggs++;
            earnedPoints = Mathf.RoundToInt(firstEggHitScore * secondHitMultiplier);
        }
        else return;
        eggStates[egg] = egg.State;
        remainingEggs = intactEggs + brokenEggs;
        AddMouseScore(earnedPoints);
        UpdateEggUI();
    }

    public static int CalculateFinalScore(int gameplayScore, int brokenCount, int penalty, float multiplier)
        => brokenCount == 0 ? Mathf.RoundToInt(gameplayScore * multiplier) : gameplayScore - brokenCount * penalty;

    public void EndGame()
    {
        if (!started || gameEnded) return;
        gameEnded = true;
        remainingTime = 0f;
        if (mouseSpawner) mouseSpawner.EndGameplay();
        finalScore = CalculateFinalScore(score, brokenEggs + destroyedEggs, brokenEggPenalty, allEggsMultiplier);
        UpdateTimerUI();
        if (finalScoreText) finalScoreText.text = finalScore.ToString();
        Debug.Log($"Final Score: {finalScore}", this);
    }

    void UpdateScoreUI() { if (playerScoreText) playerScoreText.text = score.ToString(); }
    void UpdateMouseScoreUI() { if (mouseScoreText) mouseScoreText.text = mouseScore.ToString(); }
    void UpdateEggUI()
    {
        int displayedCount = eggCountMode == EggCountMode.IntactOnly ? intactEggs : remainingEggs;
        if (eggCountText) eggCountText.text = $"{displayedCount}/{totalEggs}";
    }
    void UpdateTimerUI() { if (timerText) timerText.text = Mathf.CeilToInt(remainingTime).ToString(); }
}
