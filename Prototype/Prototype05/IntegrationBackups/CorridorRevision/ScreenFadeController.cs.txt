using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed class ScreenFadeController : MonoBehaviour
{
    public CanvasGroup overlay;
    public float Alpha => overlay ? overlay.alpha : 0;
    void Awake() { EnsureOverlay(); SetImmediate(0); }
    public void EnsureOverlay()
    {
        if (overlay) return;
        var canvasObject = new GameObject("Black Door Screen Fade", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32767;
        overlay = canvasObject.GetComponent<CanvasGroup>();
        overlay.blocksRaycasts = false; overlay.interactable = false; overlay.alpha = 0;
        var imageObject = new GameObject("Black", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(canvasObject.transform, false);
        var rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = imageObject.GetComponent<Image>(); image.color = Color.black; image.raycastTarget = false;
    }
    public void SetImmediate(float alpha) { if (overlay) overlay.alpha = Mathf.Clamp01(alpha); }
    public IEnumerator FadeTo(float alpha, float duration)
    {
        EnsureOverlay();
        float initial = Alpha;
        if (duration > 0) for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        { SetImmediate(Mathf.Lerp(initial, alpha, Mathf.SmoothStep(0, 1, elapsed / duration))); yield return null; }
        SetImmediate(alpha);
    }
}
