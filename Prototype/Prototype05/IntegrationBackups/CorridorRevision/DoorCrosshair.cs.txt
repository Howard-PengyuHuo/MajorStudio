using UnityEngine;
using UnityEngine.UI;

// Presentation only. CorridorManager remains the sole door input handler.
[DefaultExecutionOrder(1200)]
public sealed class DoorCrosshair : MonoBehaviour
{
    public CorridorManager manager;
    public Canvas canvas;
    public Image crosshair;
    public Image crossbar;
    public Text prompt;
    public string interactionPrompt = "[E] Open Door";
    public Color idleColor = Color.white;
    public Color targetColor = new Color(0.65f, 1, 0.75f);
    void Awake() { EnsureUI(); }
    public void EnsureUI()
    {
        if (!canvas)
        {
            var go = new GameObject("Door Crosshair UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32766;
            var group = go.GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
        }
        if (!crosshair)
        {
            var go = new GameObject("Center Dot", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = Vector2.zero;
            crosshair = go.GetComponent<Image>(); crosshair.color = idleColor; crosshair.raycastTarget = false;
        }
        crosshair.rectTransform.sizeDelta = new Vector2(2, 8);
        if (!crossbar)
        {
            var go = new GameObject("Crossbar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(crosshair.transform, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(8, 2);
            crossbar = go.GetComponent<Image>(); crossbar.color = idleColor; crossbar.raycastTarget = false;
        }
        if (!prompt)
        {
            var go = new GameObject("Door Prompt", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(canvas.transform, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = new Vector2(0, -32); rect.sizeDelta = new Vector2(300, 32);
            prompt = go.GetComponent<Text>(); prompt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            prompt.fontSize = 18; prompt.alignment = TextAnchor.MiddleCenter; prompt.color = Color.white; prompt.raycastTarget = false;
            prompt.text = interactionPrompt; prompt.enabled = false;
        }
    }
    void LateUpdate()
    {
        bool targeted = manager && !manager.IsBusy && manager.TargetDoor && manager.TargetDoor.CanInteract;
        if (crosshair) { crosshair.enabled = !manager || !manager.IsBusy; crosshair.color = targeted ? targetColor : idleColor; }
        if (crossbar) { crossbar.enabled = crosshair && crosshair.enabled; crossbar.color = crosshair ? crosshair.color : idleColor; }
        if (prompt) { prompt.text = interactionPrompt; prompt.enabled = targeted; }
    }
}
