using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit, additive editor command. Never runs automatically or edits animation assets.
public static class UpsideDogSetup
{
    [MenuItem("Tools/Upside Dog/Add Mechanics Test Area")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var dog = scene.GetRootGameObjects().Single(g => g.name == "Dog");
        if (scene.GetRootGameObjects().Any(g => g.name == "MechanicsTestLevel"))
            throw new InvalidOperationException("MechanicsTestLevel already exists; no changes made.");
        var sprite = dog.GetComponent<SpriteRenderer>();
        if (!sprite || !dog.GetComponent<Animator>()) throw new InvalidOperationException("Expected existing Dog visuals.");
        int layer = LayerMask.NameToLayer("Ground");
        if (layer < 0)
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                { layer = i; layers.GetArrayElementAtIndex(i).stringValue = "Ground"; break; }
            if (layer < 0) throw new InvalidOperationException("No free terrain layer.");
            tags.ApplyModifiedProperties();
        }
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Add Upside Dog mechanics");
        int group = Undo.GetCurrentGroup();
        var body = dog.GetComponent<Rigidbody2D>();
        if (!body)
        {
            body = Undo.AddComponent<Rigidbody2D>(dog);
            body.gravityScale = 3;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }
        if (!dog.GetComponent<Collider2D>())
        {
            var box = Undo.AddComponent<BoxCollider2D>(dog);
            // Centered, fixed body shape: animation frames and 180-degree release cannot shift it.
            box.offset = Vector2.zero;
            box.size = new Vector2(0.48f, 0.4f);
        }
        if (!dog.GetComponent<BoxCollider2D>()) throw new InvalidOperationException("Review existing non-box Dog collider before setup.");
        var controller = dog.GetComponent<DogController2D>() ?? Undo.AddComponent<DogController2D>(dog);
        var settings = new SerializedObject(controller);
        settings.FindProperty("groundLayer").intValue = 1 << layer;
        settings.ApplyModifiedProperties();
        var parent = new GameObject("MechanicsTestLevel");
        Undo.RegisterCreatedObjectUndo(parent, "Create test level");
        float top = dog.transform.position.y - 0.3f - 0.04f;
        // All upper sections share their underside, so the tall block can be bypassed underneath.
        AddBox(parent, layer, "A_B_Start_Flat", -5.2f, top - 0.3f, 6.4f, 0.6f);
        AddBox(parent, layer, "C_High_Block", -0.8f, top + 0.85f, 2.4f, 2.9f);
        AddBox(parent, layer, "D_Pit_Left_Lip", 1.25f, top - 0.3f, 1.7f, 0.6f);
        AddBox(parent, layer, "D_Pit_Right_Lip", 5.6f, top - 0.3f, 2.6f, 0.6f);
        AddBox(parent, layer, "D_Pit_Left_Wall", 1.9f, top - 1.05f, 0.4f, 1.5f);
        AddBox(parent, layer, "D_Pit_Right_Wall", 4.5f, top - 1.05f, 0.4f, 1.5f);
        AddBox(parent, layer, "D_Pit_Floor", 3.2f, top - 1.65f, 3f, 0.3f);
        AddBox(parent, layer, "E_Release_Landing", -3.4f, top - 3.25f, 9.8f, 0.5f);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Upside Dog: existing Dog configured and MechanicsTestLevel saved. Animation assets unchanged.");
    }

    private static void AddBox(GameObject parent, int layer, string name, float x, float y, float width, float height)
    {
        Selection.activeGameObject = null;
        if (!EditorApplication.ExecuteMenuItem("GameObject/2D Object/Sprites/Square"))
            throw new InvalidOperationException("Unity built-in Square command unavailable.");
        var box = Selection.activeGameObject;
        box.name = name;
        Undo.SetTransformParent(box.transform, parent.transform, "Parent test rectangle");
        box.transform.position = new Vector3(x, y, 0);
        var renderer = box.GetComponent<SpriteRenderer>();
        var size = renderer.sprite.bounds.size;
        box.transform.localScale = new Vector3(width / size.x, height / size.y, 1);
        renderer.color = new Color(0.45f, 0.58f, 0.65f);
        box.layer = layer;
        Undo.AddComponent<BoxCollider2D>(box);
    }

    [MenuItem("Tools/Upside Dog/Verify Mechanics")]
    public static void Verify()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run validation in Edit mode.");
        var log = new System.Collections.Generic.List<string>();
        GameObject dog = null, terrain = null, obstacle = null;
        try
        {
            var original = GameObject.Find("Dog");
            dog = UnityEngine.Object.Instantiate(original);
            dog.name = "Temporary mechanics validation";
            dog.hideFlags = HideFlags.HideAndDontSave;
            dog.transform.position = new Vector3(10000, 10001.325f, 0);
            var control = dog.GetComponent<DogController2D>();
            var rb = dog.GetComponent<Rigidbody2D>();
            var shape = dog.GetComponent<BoxCollider2D>();
            terrain = new GameObject("Temporary support", typeof(BoxCollider2D));
            terrain.hideFlags = HideFlags.HideAndDontSave;
            terrain.layer = LayerMask.NameToLayer("Ground");
            terrain.transform.position = new Vector3(10000, 10000, 0);
            terrain.GetComponent<BoxCollider2D>().size = new Vector2(8, 2);
            Invoke(control, "Awake");
            Physics2D.SyncTransforms();
            Invoke(control, "DetectSurface");
            Check(control.IsGrounded, "Normal support cast", log);
            Check((bool)Invoke(control, "TryFlip"), "Normal to inverted Flip", log);
            Physics2D.SyncTransforms();
            Check(control.Mode == DogController2D.SurfaceMode.Inverted && rb.gravityScale < 0 && shape.bounds.max.y < terrain.GetComponent<Collider2D>().bounds.min.y, "Underside placement without penetration", log);
            Check(!(bool)Invoke(control, "TryFlip"), "Flip cooldown rejects spam", log);
            rb.linearVelocity = new Vector2(4, 0);
            Invoke(control, "LateUpdate");
            Check(dog.GetComponent<SpriteRenderer>().flipX, "Inverted right-facing compensation", log);
            Set(control, "horizontal", 1f);
            Invoke(control, "FixedUpdate");
            Check(rb.linearVelocity.x > 0, "Inverted D remains world right", log);
            Set(control, "jumpRequested", true);
            Invoke(control, "FixedUpdate");
            Check(rb.linearVelocity.y < 0 && !control.IsGrounded, "Inverted jump moves away", log);
            Invoke(control, "DetectSurface");
            Check(!control.IsGrounded && !(bool)Invoke(control, "TryFlip"), "Airborne Flip rejected", log);
            var before = rb.position;
            Invoke(control, "Release");
            Check(rb.position == before && rb.gravityScale > 0 && control.Mode == DogController2D.SurfaceMode.Released, "Release restores gravity without teleport", log);
            rb.position = new Vector2(10000, 10001.325f);
            rb.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            Invoke(control, "DetectSurface");
            Check(control.Mode == DogController2D.SurfaceMode.Normal && control.IsGrounded, "Release landing restores Normal", log);
            Set(control, "nextFlipTime", -1f);
            obstacle = new GameObject("Temporary blocker", typeof(BoxCollider2D));
            obstacle.hideFlags = HideFlags.HideAndDontSave;
            obstacle.transform.position = new Vector3(10000, 9998.7f, 0);
            Physics2D.SyncTransforms();
            before = rb.position;
            Check(!(bool)Invoke(control, "TryFlip") && rb.position == before, "Non-Ground obstacle rejects Flip without moving", log);
            Set(control, "jumpRequested", true);
            Invoke(control, "FixedUpdate");
            Check(rb.linearVelocity.y > 0, "Normal jump moves up", log);
            float velocity = rb.linearVelocity.y;
            Set(control, "jumpRequested", true);
            Invoke(control, "FixedUpdate");
            Check(rb.linearVelocity.y == velocity && !control.IsGrounded, "No airborne double jump", log);
            Invoke(control, "LateUpdate");
            var animator = dog.GetComponent<Animator>();
            animator.Update(0);
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("New Layer.Jump"), "Existing Jump state selected", log);
        }
        catch (Exception exception) { log.Add("FAIL: " + exception); throw; }
        finally
        {
            if (dog) UnityEngine.Object.DestroyImmediate(dog);
            if (terrain) UnityEngine.Object.DestroyImmediate(terrain);
            if (obstacle) UnityEngine.Object.DestroyImmediate(obstacle);
            Directory.CreateDirectory("Temp");
            File.WriteAllLines("Temp/UpsideDogVerification.txt", log);
            Debug.Log(string.Join("\n", log));
        }
    }

    private static object Invoke(DogController2D target, string method) => typeof(DogController2D).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private static void Set(DogController2D target, string field, object value) => typeof(DogController2D).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Check(bool condition, string message, System.Collections.Generic.List<string> log)
    {
        if (!condition) throw new Exception(message);
        log.Add("PASS: " + message);
    }
}
