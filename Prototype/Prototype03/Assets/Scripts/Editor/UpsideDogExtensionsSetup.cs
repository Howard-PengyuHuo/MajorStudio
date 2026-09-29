using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

public static class UpsideDogExtensionsSetup
{
    [MenuItem("Tools/Upside Dog/Configure Prototype Extensions")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit play mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var dog = objects.Select(t => t.GetComponent<DogController2D>()).First(c => c);
        var camera = objects.Select(t => t.GetComponent<Camera>()).First(c => c && c.CompareTag("MainCamera"));
        var follow = camera.GetComponent<DeadZoneCamera2D>() ?? Undo.AddComponent<DeadZoneCamera2D>(camera.gameObject);
        Assign(follow, "target", dog.transform);
        Assign(dog, "gameplayCamera", camera);
        int traps = 0;
        foreach (var item in objects.Where(t => t.name == "Trap" || t.name.StartsWith("Trap (")))
        {
            if (!item.GetComponent<Trap2D>()) Undo.AddComponent<Trap2D>(item.gameObject);
            AddTrigger(item.gameObject);
            traps++;
        }
        var canvas = objects.Select(t => t.GetComponent<Canvas>()).First(c => c);
        var door = objects.First(t => t.name == "Door").gameObject;
        var goal = door.GetComponent<LevelGoal2D>() ?? Undo.AddComponent<LevelGoal2D>(door);
        AddTrigger(door);
        if (!canvas.transform.Find("CompletionPanel"))
        {
            var panel = UI("CompletionPanel", canvas.transform, new Vector2(380, 190), Vector2.zero);
            var background = Undo.AddComponent<Image>(panel);
            background.color = new Color(0.08f, 0.1f, 0.15f, 0.97f);
            var font = canvas.GetComponentInChildren<TMP_Text>(true).font;
            var title = UI("CompletionTitle", panel.transform, new Vector2(350, 55), new Vector2(0, 40));
            var text = Undo.AddComponent<TextMeshProUGUI>(title);
            text.font = font; text.text = "LEVEL COMPLETE"; text.fontSize = 24; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            var buttonObject = UI("RestartGame", panel.transform, new Vector2(240, 50), new Vector2(0, -40));
            var image = Undo.AddComponent<Image>(buttonObject); image.color = new Color(0.25f, 0.4f, 0.5f);
            var button = Undo.AddComponent<Button>(buttonObject); button.targetGraphic = image;
            var labelObject = UI("Label", buttonObject.transform, new Vector2(230, 45), Vector2.zero);
            var label = Undo.AddComponent<TextMeshProUGUI>(labelObject);
            label.font = font; label.text = "Restart Game"; label.fontSize = 20; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            Assign(goal, "completionPanel", panel); Assign(goal, "restartButton", button);
            panel.SetActive(false);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Upside Dog extensions: reused {traps} traps, Door, Canvas, Main Camera and Dog. Start menu unchanged.");
    }

    private static void AddTrigger(GameObject target)
    {
        if (target.GetComponent<Collider2D>()) return;
        var sprite = target.GetComponent<SpriteRenderer>().sprite;
        var collider = Undo.AddComponent<BoxCollider2D>(target);
        collider.size = sprite.bounds.size;
        collider.offset = sprite.bounds.center;
        collider.isTrigger = true;
    }
    private static GameObject UI(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var item = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(item, "Add completion UI");
        item.layer = 5;
        item.transform.SetParent(parent, false);
        var rect = (RectTransform)item.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size; rect.anchoredPosition = position;
        return item;
    }
    private static void Assign(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }

    [MenuItem("Tools/Upside Dog/Restore Shared Menu Materials")]
    public static void RestoreSharedMenuMaterials()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit play mode first.");
        // Unity inspection can instantiate TMP materials through property getters.
        // Restore the original menu's asset references without changing its design or bindings.
        var gui = GameObject.Find("Canvas").transform.Find("GUI");
        foreach (var label in gui.GetComponentsInChildren<TMP_Text>(true))
        {
            var data = new SerializedObject(label);
            data.FindProperty("m_sharedMaterial").objectReferenceValue = label.font.material;
            data.FindProperty("m_fontMaterial").objectReferenceValue = null;
            data.FindProperty("m_fontMaterials").arraySize = 0;
            data.FindProperty("m_fontSharedMaterials").arraySize = 0;
            data.ApplyModifiedProperties();
        }
        EditorSceneManager.SaveScene(gui.gameObject.scene);
    }
}
