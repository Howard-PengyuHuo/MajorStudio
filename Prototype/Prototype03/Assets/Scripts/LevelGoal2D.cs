using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class LevelGoal2D : MonoBehaviour
{
    [SerializeField] private GameObject completionPanel;
    [SerializeField] private Button restartButton;
    private bool completed;
    private void Awake()
    {
        completionPanel.SetActive(false);
        restartButton.onClick.AddListener(RestartGame);
    }
    private void OnDestroy() { if (restartButton) restartButton.onClick.RemoveListener(RestartGame); }
    private void OnTriggerEnter2D(Collider2D other) => Touch(other);
    private void OnTriggerStay2D(Collider2D other) => Touch(other);
    private void OnCollisionEnter2D(Collision2D other) => Touch(other.collider);
    private void Touch(Collider2D other)
    {
        var dog = other.GetComponentInParent<DogController2D>();
        if (!dog || dog.GameplayLocked || completed) return;
        completed = true;
        dog.CompleteLevel();
        completionPanel.SetActive(true);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
    }
    public void RestartGame()
    {
        if (!completed) return;
        completed = false;
        // Only the current level is reloaded; persistent objects are not explicitly destroyed.
        SceneManager.LoadScene(gameObject.scene.path);
    }
}
