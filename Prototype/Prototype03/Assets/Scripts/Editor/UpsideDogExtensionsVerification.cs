using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Play-mode physics regression checks use temporary objects far from the authored level.
public static class UpsideDogExtensionsVerification
{
    private static readonly List<GameObject> temporary = new List<GameObject>();
    private static readonly List<string> results = new List<string>();
    private static DogController2D dog;
    private static Rigidbody2D body;
    private static BoxCollider2D shape;
    private static BoxCollider2D floor;
    private const float X = 10000;
    private static Vector2 Offset => Vector2.Scale(shape.offset, dog.transform.lossyScale);

    [MenuItem("Tools/Upside Dog/Verify Prototype Extensions (Play Mode)")]
    public static void Verify()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        results.Clear();
        var oldMode = Physics2D.simulationMode;
        try
        {
            var original = UnityEngine.Object.FindFirstObjectByType<DogController2D>(FindObjectsInactive.Include);
            var clone = UnityEngine.Object.Instantiate(original.gameObject);
            temporary.Add(clone); clone.name = "Extension verification Dog";
            clone.transform.position = new Vector3(X, 2, 0);
            clone.SetActive(true);
            dog = clone.GetComponent<DogController2D>(); dog.enabled = false;
            body = clone.GetComponent<Rigidbody2D>(); shape = clone.GetComponent<BoxCollider2D>();
            Set(dog, "gameplayCamera", null);
            Set(dog, "levelStart", new Vector2(X, 3));
            Set(dog, "lastSafePosition", new Vector2(X + 2, 3));
            Physics2D.simulationMode = SimulationMode2D.Script;
            floor = Box("Support", new Vector2(X, 0), new Vector2(20, 1), 8);
            Place(new Vector2(X, floor.bounds.max.y + shape.bounds.extents.y + 0.02f));
            Step(10);
            Check(dog.IsGrounded, "02 normal landing on top");
            Set(dog, "horizontal", 1f); Step(8); Check(body.linearVelocity.x > 0, "01 run right");
            Set(dog, "horizontal", -1f); Step(8); Check(body.linearVelocity.x < 0, "01 run left");
            Set(dog, "horizontal", 0f); Set(dog, "jumpRequested", true); Step(1);
            Check(body.linearVelocity.y > 0, "02 normal jump ascent"); Step(100);
            Check(dog.IsGrounded && dog.Mode == DogController2D.SurfaceMode.Normal, "02 normal jump returns to top");
            Set(dog, "nextFlipTime", -1f);
            Check((bool)Call(dog, "TryFlip"), "07 top to bottom Flip"); Step(10);
            Check(dog.IsGrounded && dog.Mode == DogController2D.SurfaceMode.Inverted, "07 underside attachment");
            Set(dog, "horizontal", 1f); Step(8); Check(body.linearVelocity.x > 0, "03 inverted run stays world right");
            Set(dog, "horizontal", 0f); Set(dog, "jumpRequested", true); Step(1);
            Check(body.linearVelocity.y < 0, "04 inverted jump away"); Step(100);
            Check(dog.IsGrounded && dog.Mode == DogController2D.SurfaceMode.Inverted, "04 inverted jump returns to underside");
            Set(dog, "nextFlipTime", -1f); Check((bool)Call(dog, "TryFlip"), "08 bottom to top Flip"); Step(8);
            TestWall(false); TestWall(true);
            Check(dog.Mode == DogController2D.SurfaceMode.Inverted, "09 begins inverted"); Step(10);
            var lower = Box("Release landing", new Vector2(X, -3), new Vector2(20, 0.5f), 8);
            Vector2 before = body.position; Call(dog, "Release");
            Check(body.position == before && dog.Mode == DogController2D.SurfaceMode.Released && body.gravityScale > 0, "09 Release without teleport");
            Step(80);
            Check(dog.IsGrounded && dog.AttachedSurface == lower && dog.Mode == DogController2D.SurfaceMode.Normal && !dog.GameplayLocked, "10/11 nearby Release landing survives upright");
            float separation = Physics2D.Distance(shape, lower).distance;
            Check(separation >= -2 * Physics2D.defaultContactOffset, $"10 upright landing within physics contact tolerance ({separation:F4})");
            // Stable normal checkpoint, then prove airborne movement cannot overwrite it.
            Set(dog, "nextFlipTime", -1f); Set(dog, "stableSince", Time.time - 1); Call(dog, "UpdateSurvival");
            var safe = Get<Vector2>(dog, "lastSafePosition");
            Check(Vector2.Distance(safe, body.position) < 0.15f, "12 stable top-surface checkpoint recorded");
            Place(new Vector2(X + 30, 20)); Set(dog, "trackingFall", true); Set(dog, "fallPeak", body.position.y + 8);
            Call(dog, "UpdateSurvival");
            Check(dog.GameplayLocked && Get<Vector2>(dog, "lastSafePosition") == safe, "12 long fall dies without overwriting checkpoint");
            Call(dog, "FinishRespawn");
            Check(!dog.GameplayLocked && Vector2.Distance(body.position, safe) < 0.15f && body.linearVelocity == Vector2.zero, $"12 safe respawn clears physics (locked={dog.GameplayLocked}, actual={body.position}, safe={safe}, velocity={body.linearVelocity})");
            var camObject = New("Validation camera"); var cam = camObject.AddComponent<Camera>(); cam.enabled = false; cam.orthographic = true; cam.orthographicSize = 5;
            camObject.transform.position = new Vector3(X + 30, 30, -10);
            Set(dog, "gameplayCamera", cam); Set(dog, "protectedUntil", -1f);
            Place(new Vector2(X + 30, 15)); body.linearVelocity = Vector2.down;
            Call(dog, "UpdateSurvival"); Check(dog.GameplayLocked, "13 below-camera death"); Call(dog, "FinishRespawn"); Set(dog, "gameplayCamera", null);
            var trap = Box("Validation trap", new Vector2(X + 30, 5), new Vector2(2, 2), 0);
            trap.isTrigger = true; trap.gameObject.AddComponent<Trap2D>(); Set(dog, "protectedUntil", -1f);
            Place(trap.bounds.center); Step(2);
            Check(dog.GameplayLocked && Get<Vector2>(dog, "respawnDestination") == Get<Vector2>(dog, "levelStart"), "14 actual Trap trigger chooses original start");
            Call(dog, "FinishRespawn"); Check(!dog.GameplayLocked && dog.Mode == DogController2D.SurfaceMode.Normal, "14 trap respawn restores control");
            // Use the authored Door and actual UI references, with clone only; reset after the check.
            var goal = UnityEngine.Object.FindFirstObjectByType<LevelGoal2D>();
            Place(goal.GetComponent<Collider2D>().bounds.center); Set(dog, "protectedUntil", -1f); Step(2);
            Check(dog.GameplayLocked && Get<GameObject>(goal, "completionPanel").activeSelf, "15 actual Door trigger opens completion panel and locks control");
            Check(Get<Button>(goal, "restartButton").onClick != null, "16 Restart button configured (reload checked separately)");
            Get<GameObject>(goal, "completionPanel").SetActive(false); Set(goal, "completed", false);
            var follow = camObject.AddComponent<DeadZoneCamera2D>(); Set(follow, "target", dog.transform);
            camObject.transform.position = new Vector3(X, 0, -10);
            dog.transform.position = new Vector3(X + 1, 1, 0); var cameraBefore = camObject.transform.position;
            Call(follow, "LateUpdate"); Check(camObject.transform.position == cameraBefore, "17/19 camera stays still within horizontal and vertical buffer");
            dog.transform.position = new Vector3(X + 5, 1, 0); Call(follow, "LateUpdate");
            Check(camObject.transform.position.x > X && camObject.transform.position.x < X + 2, "18 camera eases toward horizontal boundary");
            dog.transform.position = new Vector3(X + 1, 5, 0); float previousY = camObject.transform.position.y; Call(follow, "LateUpdate");
            Check(camObject.transform.position.y > previousY && camObject.transform.position.y < 3.2f, "20 camera eases toward vertical boundary");
        }
        catch (Exception e) { results.Add("FAIL: " + e); Debug.LogException(e); }
        finally
        {
            Physics2D.simulationMode = oldMode;
            foreach (var item in temporary) if (item) UnityEngine.Object.DestroyImmediate(item);
            temporary.Clear();
            System.IO.File.WriteAllLines("Temp/UpsideDogExtensionsVerification.txt", results);
            Debug.Log(string.Join("\n", results));
        }
    }

    private static void TestWall(bool inverted)
    {
        if ((dog.Mode == DogController2D.SurfaceMode.Inverted) != inverted)
        { Set(dog, "nextFlipTime", -1f); Call(dog, "TryFlip"); Step(10); }
        float side = inverted ? -1 : 1;
        var wall = Box("Wall test", new Vector2(body.position.x + 1.1f, side * 1.6f), new Vector2(0.4f, 4), 0);
        Set(dog, "horizontal", 1f); Set(dog, "jumpRequested", true);
        bool hit = false;
        for (int i = 0; i < 15; i++)
        {
            Step(1);
            if (shape.IsTouching(wall)) { hit = true; Check(body.linearVelocity.y * side <= 0.01f, inverted ? "06 inverted wall ends ascent" : "05 normal wall ends ascent"); break; }
        }
        Check(hit, inverted ? "06 physical wall contact" : "05 physical wall contact");
        UnityEngine.Object.DestroyImmediate(wall.gameObject); Set(dog, "horizontal", 0f); Step(100);
    }

    [MenuItem("Tools/Upside Dog/Verify Restart Button (Play Mode)")]
    public static void VerifyRestart()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        var goal = UnityEngine.Object.FindFirstObjectByType<LevelGoal2D>();
        Set(goal, "completed", true);
        Get<GameObject>(goal, "completionPanel").SetActive(true);
        int originalID = goal.GetInstanceID();
        Get<Button>(goal, "restartButton").onClick.Invoke();
        double deadline = EditorApplication.timeSinceStartup + 8;
        EditorApplication.CallbackFunction check = null;
        check = () =>
        {
            var reloadedGoal = UnityEngine.Object.FindFirstObjectByType<LevelGoal2D>();
            if (reloadedGoal && reloadedGoal.GetInstanceID() != originalID)
            {
                var reloadedDog = UnityEngine.Object.FindFirstObjectByType<DogController2D>(FindObjectsInactive.Include);
                bool good = !reloadedDog.gameObject.activeSelf && !Get<GameObject>(reloadedGoal, "completionPanel").activeSelf;
                System.IO.File.WriteAllText("Temp/UpsideDogRestartVerification.txt", good ? "PASS: 16 actual Restart button reloads the level, restores original inactive Dog/start-menu flow and hides completion GUI." : "FAIL: Reloaded scene state differs.");
                EditorApplication.update -= check;
            }
            else if (EditorApplication.timeSinceStartup > deadline)
            {
                System.IO.File.WriteAllText("Temp/UpsideDogRestartVerification.txt", "FAIL: Scene did not reload.");
                EditorApplication.update -= check;
            }
        };
        EditorApplication.update += check;
    }
    private static void Place(Vector2 center)
    {
        body.position = center - (Vector2)(Quaternion.Euler(0, 0, body.rotation) * Offset);
        body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); Call(dog, "DetectSurface");
    }
    private static void Step(int count)
    {
        for (int i = 0; i < count; i++) { Call(dog, "FixedUpdate"); Physics2D.Simulate(0.02f); Physics2D.SyncTransforms(); }
    }
    private static GameObject New(string name) { var g = new GameObject(name); temporary.Add(g); return g; }
    private static BoxCollider2D Box(string name, Vector2 point, Vector2 size, int layer)
    {
        var g = New(name); g.transform.position = point; g.layer = layer;
        var c = g.AddComponent<BoxCollider2D>(); c.size = size; Physics2D.SyncTransforms(); return c;
    }
    private static object Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); results.Add("PASS: " + message); }
}
