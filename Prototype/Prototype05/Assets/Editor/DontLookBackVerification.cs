using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public static class DontLookBackVerification
{
    [MenuItem("Tools/Don't Look Back/Inspect Connector Blockers")]
    public static void Blockers()
    {
        var manager = All<CorridorLoopManager>().Single(); manager.Initialize(false); Physics.SyncTransforms();
        var lines = new System.Collections.Generic.HashSet<string>();
        string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
        foreach (var corridor in manager.corridors) for (float z = 0; z < 55; z += 0.25f)
        {
            var p = corridor.startPoint.TransformPoint(new Vector3(0, 0, z));
            if (manager.IsClear(p, corridor.startPoint.rotation)) continue;
            foreach (var hit in Physics.OverlapCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.3f, 0.475f, ~0, QueryTriggerInteraction.Ignore))
                lines.Add(Path(hit.transform) + " position=" + hit.transform.position + " parent=" + hit.transform.parent.position);
        }
        File.WriteAllLines("IntegrationBackups/ConnectorBlockers.txt", lines);
    }
    static T[] All<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    static void Check(bool condition, string message, StringBuilder report)
    { if (!condition) throw new InvalidOperationException(message); report.AppendLine("PASS " + message); }
    [MenuItem("Tools/Don't Look Back/Audit Integrated Scene")]
    public static void Audit()
    {
        var report = new StringBuilder();
        Check(SceneManager.GetActiveScene().name == "DontLookBack", "Integrated scene active", report);
        Check(SceneManager.sceneCount == 1, "Only one scene loaded", report);
        Check(All<FirstPersonMovement>().Count(c => c.isActiveAndEnabled) == 1, "One active player controller", report);
        Check(All<Camera>().Count(c => c.isActiveAndEnabled) == 1, "One active gameplay camera", report);
        Check(All<AudioListener>().Count(c => c.isActiveAndEnabled) == 1, "One active AudioListener", report);
        Check(All<Volume>().Count(c => c.isActiveAndEnabled && c.isGlobal) <= 1, "No duplicate global post-processing volumes", report);
        var manager = All<CorridorLoopManager>().Single();
        manager.Initialize(false);
        Physics.SyncTransforms();
        foreach (var corridor in manager.corridors)
        {
            Check(manager.IsClear(corridor.startPoint.position, corridor.startPoint.rotation), corridor.name + " start is grounded and unobstructed", report);
            // Entire shared centerline, not just the spawn points, must transfer safely.
            for (float z = 0; z < 53; z += 0.5f)
            {
                Vector3 p = corridor.startPoint.TransformPoint(new Vector3(0, 0, z));
                if (!manager.IsClear(p, corridor.startPoint.rotation))
                {
                    var hits = Physics.OverlapCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.3f, 0.475f, ~0, QueryTriggerInteraction.Ignore);
                    string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
                    throw new InvalidOperationException(corridor.name + " blocked at " + z + ": " + string.Join("; ", hits.Select(h => Path(h.transform) + " bounds=" + h.bounds)));
                }
                Check(true, corridor.name + " clear centerline at " + z, report);
            }
        }
        var cache = All<CorridorLightingCache>().Single();
        Check(cache.surfaces.All(s => s.renderer && s.renderer.lightmapIndex >= 0
            && s.renderer.lightmapIndex < LightmapSettings.lightmaps.Length
            && LightmapSettings.lightmaps[s.renderer.lightmapIndex].lightmapColor == cache.atlases[s.atlas].color), "Lighting assignments survive scene loading", report);
        cache.Apply();
        Check(cache.atlases.Length == 2 && cache.atlases.All(a => a.color), "Both original lighting atlases referenced", report);
        Check(cache.surfaces.All(s => s.renderer && s.renderer.lightmapIndex >= 0), "All cached baked surfaces assigned", report);
        Check(All<Light>().All(l => l.lightmapBakeType != LightmapBakeType.Baked || l.bakingOutput.isBaked), "Baked lamps do not add unbaked illumination", report);
        Check(All<Renderer>().Where(r => r.enabled).All(r => r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported && m.shader.name != "Standard" && !ShaderUtil.ShaderHasError(m.shader))), "Environment materials supported", report);
        int missing = All<Transform>().Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        Check(missing == 0, "No missing scripts", report);
        var colliders = All<Collider>();
        report.AppendLine($"Renderer count: {All<Renderer>().Length}; colliders: {colliders.Length}; lamps: {All<Light>().Length}; reflection probes: {All<ReflectionProbe>().Length}; global volumes: {All<Volume>().Length}");
        File.WriteAllText("IntegrationBackups/SceneAudit.txt", report.ToString());
        Debug.Log("Don't Look Back scene audit passed; IntegrationBackups/SceneAudit.txt");
    }
    [MenuItem("Tools/Don't Look Back/Run Play Mode Mechanic Checks")]
    public static void PlayChecks()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        var report = new StringBuilder();
        var manager = All<CorridorLoopManager>().Single();
        var body = manager.player.GetComponent<Rigidbody>();
        var look = manager.mouseLook;
        bool lookEnabled = look && look.enabled;
        if (look) look.enabled = false;
        float holdBack = manager.backwardHoldSeconds + 0.02f;
        float holdForward = manager.forwardHoldSeconds + 0.02f;
        void SetPlayer(Vector3 position, Quaternion rotation)
        { manager.player.SetPositionAndRotation(position, rotation); body.position = position; body.rotation = rotation; Physics.SyncTransforms(); }
        void Arm()
        {
            var frame = manager.corridors[manager.CurrentCorridor].startPoint;
            manager.SampleLook(frame.forward, manager.teleportCooldown + 0.1f);
            manager.SampleLook(frame.forward, holdForward);
        }
        void Complete()
        {
            var frame = manager.corridors[manager.CurrentCorridor].startPoint;
            manager.SampleLook(-frame.forward, holdBack);
            manager.SampleLook(frame.forward, holdForward);
        }
        try
        {
            manager.Initialize(true);
            var start = manager.corridors[0].startPoint;
            SetPlayer(start.TransformPoint(new Vector3(0.1f, 0, 5)), Quaternion.Euler(0, 18, 0));
            Arm();
            for (int i = 0; i < 20; i++) manager.SampleLook(Quaternion.Euler(0, i % 2 == 0 ? 25 : -25, 0) * start.forward, 0.05f);
            Check(manager.LookBackCount == 0 && !manager.AwaitingReturn, "Small camera movements ignored", report);
            Vector3 before = manager.player.position;
            manager.SampleLook(-start.forward, manager.backwardHoldSeconds * 0.5f);
            manager.SampleLook(start.forward, holdForward);
            Check(!manager.AwaitingReturn, "Brief backward glance ignored", report);
            manager.SampleLook(-start.forward, holdBack);
            Check(manager.AwaitingReturn && Vector3.Distance(before, manager.player.position) < 0.001f, "Looking backward alone does not teleport", report);
            manager.SampleLook(start.forward, holdForward);
            Check(manager.LookBackCount == 1 && manager.CurrentCorridor == 0 && Vector3.Distance(manager.player.position, start.position) < 0.001f, "First completed turn returns to current start", report);
            SetPlayer(start.TransformPoint(new Vector3(0.2f, 0, 6)), Quaternion.Euler(0, 18, 0));
            Vector3 local = start.InverseTransformPoint(manager.player.position);
            Quaternion localRotation = Quaternion.Inverse(start.rotation) * manager.player.rotation;
            body.linearVelocity = new Vector3(1, -2, 3); body.angularVelocity = Vector3.up;
            Arm(); Complete();
            var next = manager.corridors[1].startPoint;
            Check(manager.CurrentCorridor == 1 && manager.LookBackCount == 0, "Second completed turn changes corridor and resets counter", report);
            Check(Vector3.Distance(next.InverseTransformPoint(manager.player.position), local) < 0.001f && Quaternion.Angle(Quaternion.Inverse(next.rotation) * manager.player.rotation, localRotation) < 0.1f, "A to B preserves local position and facing", report);
            Check(body.linearVelocity.sqrMagnitude < 0.001f && body.angularVelocity.sqrMagnitude < 0.001f, "Rigidbody velocity cleared safely", report);
            var yawField = typeof(FirstPersonLook).GetField("velocity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var yaw = (Vector2)yawField.GetValue(look);
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw.x, manager.player.localEulerAngles.y)) < 0.1f, "Mouse-look accumulator synchronized", report);
            Arm(); Complete();
            Check(manager.CurrentCorridor == 1 && manager.LookBackCount == 1 && Vector3.Distance(manager.player.position, next.position) < 0.001f, "First B turn returns to B start", report);
            SetPlayer(next.TransformPoint(new Vector3(0.2f, 0, 8)), Quaternion.Euler(0, 18, 0));
            Arm(); Complete();
            Check(manager.CurrentCorridor == 0 && manager.LookBackCount == 0 && Vector3.Distance(start.InverseTransformPoint(manager.player.position), new Vector3(0.2f, 0, 8)) < 0.001f, "B to A loops and preserves position", report);
            CheckCharacterController(report);
            Check(manager.IsClear(manager.player.position, manager.player.rotation), "Player finishes on clear floor", report);
            File.WriteAllText("IntegrationBackups/PlayModeChecks.txt", report.ToString());
            Debug.Log("Don't Look Back play-mode checks passed.");
        }
        finally
        {
            manager.Initialize(true);
            if (look) { look.SynchronizeAfterTeleport(); look.enabled = lookEnabled; }
        }
    }
    static void CheckCharacterController(StringBuilder report)
    {
        var fixture = new GameObject("Temporary CharacterController verification");
        try
        {
            Transform Point(string name, Vector3 position, Quaternion rotation)
            { var p = new GameObject(name).transform; p.SetParent(fixture.transform); p.SetPositionAndRotation(position, rotation); return p; }
            var a = Point("A", new Vector3(400, -5.94f, 0), Quaternion.identity);
            var b = Point("B", new Vector3(450, -5.94f, 0), Quaternion.Euler(0, 90, 0));
            foreach (var point in new[] { a, b })
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.SetParent(fixture.transform);
                floor.transform.position = point.position - Vector3.up * 0.11f;
                floor.transform.localScale = new Vector3(20, 0.2f, 20);
            }
            var player = Point("Test player", a.position, Quaternion.identity);
            var cc = player.gameObject.AddComponent<CharacterController>();
            cc.radius = 0.3f; cc.height = 1.8f; cc.center = Vector3.up * 0.9f;
            var test = fixture.AddComponent<CorridorLoopManager>();
            test.player = player; test.view = player;
            test.corridors = new[] {
                new CorridorLoopManager.Corridor { environment = a, startPoint = a },
                new CorridorLoopManager.Corridor { environment = b, startPoint = b } };
            Physics.SyncTransforms(); test.Initialize(true);
            void Cycle()
            {
                var frame = test.corridors[test.CurrentCorridor].startPoint;
                test.SampleLook(frame.forward, test.teleportCooldown + 0.1f);
                test.SampleLook(frame.forward, 0.2f);
                test.SampleLook(-frame.forward, 0.2f);
                test.SampleLook(frame.forward, 0.2f);
            }
            Cycle();
            player.SetPositionAndRotation(a.TransformPoint(new Vector3(0.2f, 0, 4)), Quaternion.Euler(0, 18, 0));
            Physics.SyncTransforms(); Cycle();
            Check(test.CurrentCorridor == 1 && cc.enabled, "CharacterController remains enabled after transfer", report);
            Check(Vector3.Distance(b.InverseTransformPoint(player.position), new Vector3(0.2f, 0, 4)) < 0.001f
                && Quaternion.Angle(player.rotation, Quaternion.Euler(0, 108, 0)) < 0.1f, "Actual rotated-corridor transfer preserves pose", report);
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }
    [MenuItem("Tools/Don't Look Back/Clear Verification Console")]
    public static void ClearConsole()
    {
        var type = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
        type.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
    }
}
