using UnityEngine;
using UnityEngine.UI;

public class GameHUD : MonoBehaviour
{
    public CollectibleManager collectibleManager;
    public PlayerSpawner playerSpawner;
    public Text appleText;
    public Text throwableText;
    public Image throwableIcon;
    public Sprite eggIcon;
    public Sprite bobaIcon;
    EggThrower thrower;

    void OnEnable()
    {
        if (collectibleManager != null) collectibleManager.CountsChanged += RefreshApples;
        if (playerSpawner != null) playerSpawner.PlayerSpawned += BindPlayer;
        BindPlayer(playerSpawner);
        RefreshApples();
    }

    void OnDisable()
    {
        if (collectibleManager != null) collectibleManager.CountsChanged -= RefreshApples;
        if (playerSpawner != null) playerSpawner.PlayerSpawned -= BindPlayer;
        if (thrower != null) thrower.SelectionChanged -= RefreshThrowable;
        thrower = null;
    }

    void BindPlayer(PlayerSpawner spawner)
    {
        if (thrower != null) thrower.SelectionChanged -= RefreshThrowable;
        thrower = spawner != null && spawner.SpawnedPlayer != null
            ? spawner.SpawnedPlayer.GetComponent<EggThrower>() : null;
        if (thrower != null) thrower.SelectionChanged += RefreshThrowable;
        RefreshThrowable(thrower != null ? thrower.SelectedItem : ThrowableType.Egg);
    }

    void RefreshApples()
    {
        if (appleText != null) appleText.text = collectibleManager != null
            ? $"Apples: {collectibleManager.CollectedCount} / {collectibleManager.TotalCount}" : "Apples: 0 / 0";
    }

    void RefreshThrowable(ThrowableType item)
    {
        // The thrower owns selection; this callback only displays its current state.
        if (thrower != null) item = thrower.SelectedItem;
        if (throwableText != null) throwableText.text = item == ThrowableType.Egg ? "Egg  [1]" : "Boba  [2]";
        if (throwableIcon != null) {
            throwableIcon.sprite = item == ThrowableType.Egg ? eggIcon : bobaIcon;
            throwableIcon.enabled = throwableIcon.sprite != null;
            throwableIcon.preserveAspect = true;
        }
    }
}
