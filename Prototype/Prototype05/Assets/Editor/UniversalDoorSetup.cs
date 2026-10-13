using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class UniversalDoorSetup
{
    public const string Folder = "IntegrationBackups/UniversalDoors";
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    static MeshRenderer[] Panels(CorridorDestination c) => c.GetComponentsInChildren<MeshRenderer>(true)
        .Where(r => r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh
            && (r.name == "Door" || r.GetComponent<DoorInteraction>())
            && !r.GetComponentInParent<BlackDoorTransition>()).ToArray();
    [MenuItem("Tools/Don't Look Back/Universal Doors/Inspect And Back Up")]
    public static void Inspect()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        Directory.CreateDirectory(Folder);
        var scene = SceneManager.GetActiveScene();
        string backup = "Assets/DontLookBack/Backups/BeforeUniversalDoors.unity";
        if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Backup failed.");
        var report = new StringBuilder();
        foreach (var c in UnityEngine.Object.FindObjectsByType<CorridorDestination>(FindObjectsSortMode.None))
        foreach (var r in Panels(c))
        {
            var t = r.transform;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(t);
            report.AppendLine(PathOf(t) + "\n  position=" + t.position.ToString("F3") + " localRotation=" + t.localEulerAngles.ToString("F2")
                + " parentRotation=" + t.parent.eulerAngles.ToString("F2") + " bounds=" + r.bounds + " collider=" + string.Join(",", t.GetComponentsInChildren<Collider>(true).Select(x => x.GetType().Name))
                + " animator=" + string.Join(",", t.GetComponentsInParent<Animator>(true).Select(x => x.runtimeAnimatorController ? x.runtimeAnimatorController.name : "none"))
                + " configured=" + (bool)t.GetComponent<DoorInteraction>() + " sourceRotation=" + (source ? source.localEulerAngles.ToString("F2") : "no prefab"));
        }
        File.WriteAllText(Folder + "/Inspection.txt", report.ToString());
        Debug.Log("Universal door inspection and unsaved-scene backup complete.");
    }
    [MenuItem("Tools/Don't Look Back/Universal Doors/Configure All Corridor Doors")]
    public static void ConfigureAll()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "DontLookBack") throw new InvalidOperationException("Open DontLookBack first.");
        Inspect();
        var manager = UnityEngine.Object.FindObjectsByType<CorridorManager>(FindObjectsSortMode.None).Single();
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/DontLookBack/Materials/BlackDoorVoid.mat");
        if (!material || !manager.loop || !manager.gameplayCamera) throw new InvalidOperationException("Shared system or reference material missing.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Configure universal black doors");
        var report = new StringBuilder(); int added = 0, reused = 0, skipped = 0;
        foreach (var corridor in manager.destinations.Where(d => d != null && d.instance).Select(d => d.instance).Distinct())
        foreach (var renderer in Panels(corridor))
        {
            var panel = renderer.transform;
            Vector3 position = panel.position, scale = panel.localScale; Quaternion rotation = panel.rotation;
            bool wasConfigured = panel.GetComponent<DoorInteraction>() && panel.GetComponent<DoorInteraction>().blackVoid;
            try
            {
                var interactions = panel.GetComponents<DoorInteraction>();
                var bound = corridor.GetComponentsInChildren<BlackDoorTransition>(true).FirstOrDefault(p => interactions.Contains(p.door));
                var keep = bound ? bound.door : interactions.FirstOrDefault();
                foreach (var duplicate in interactions.Where(d => d != keep))
                {
                    if (duplicate.blackVoid || duplicate.hinge || duplicate.animator) throw new InvalidOperationException("Two meaningful interactions require review.");
                    Undo.DestroyObjectImmediate(duplicate);
                    report.AppendLine("REMOVED empty duplicate interaction: " + PathOf(panel));
                }
                var portal = BlackDoorSetup.ConfigureDoor(panel, corridor, manager, material);
                var door = portal.door;
                Undo.RecordObject(portal.entranceFrame.gameObject, "Preserve dormant door state");
                portal.entranceFrame.gameObject.SetActive(panel.gameObject.activeInHierarchy);
                if (panel.gameObject.activeInHierarchy) BindPassageObstacles(corridor, portal, manager, report);
                Undo.RecordObject(door, "Allow passage through fully opened door");
                door.releasePanelCollidersWhenOpen = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(door);
                EditorUtility.SetDirty(door);
                if (panel.position != position || panel.rotation != rotation || panel.localScale != scale) throw new InvalidOperationException("Panel pose changed.");
                if (wasConfigured) reused++; else added++;
                report.AppendLine((panel.gameObject.activeInHierarchy ? (wasConfigured ? "REUSED " : "CONFIGURED ") : "DORMANT ") + PathOf(panel)
                    + " | " + (door.startsOpen ? "existing open pose" : door.animator ? "existing animation " + door.openingState : "hinge")
                    + " | portal=" + PathOf(portal.entranceFrame));
            }
            catch (Exception error) { skipped++; report.AppendLine("SKIPPED " + PathOf(panel) + " | " + error.Message); }
        }
        Undo.RecordObject(manager, "Set universal interaction range"); manager.interactionRange = 3;
        var crosshairs = UnityEngine.Object.FindObjectsByType<DoorCrosshair>(FindObjectsSortMode.None);
        if (crosshairs.Length > 1) throw new InvalidOperationException("Multiple crosshairs already present; review before changing them.");
        var ui = crosshairs.SingleOrDefault();
        if (!ui) ui = Undo.AddComponent<DoorCrosshair>(manager.gameObject);
        Undo.RecordObject(ui, "Wire shared crosshair"); ui.manager = manager;
        bool hadCanvas = ui.canvas; ui.EnsureUI();
        if (!hadCanvas) Undo.RegisterCreatedObjectUndo(ui.canvas.gameObject, "Create crosshair overlay");
        EditorUtility.SetDirty(manager); EditorUtility.SetDirty(ui);
        Undo.CollapseUndoOperations(group);
        var allDoors = manager.destinations.Where(d => d != null && d.instance).SelectMany(d => Panels(d.instance)).ToArray();
        report.Insert(0, $"Active doors: {allDoors.Count(r => r.gameObject.activeInHierarchy)}; dormant doors: {allDoors.Count(r => !r.gameObject.activeInHierarchy)}. New portals this run: {added}; reused: {reused}; skipped: {skipped}\n");
        File.WriteAllText(Folder + "/ConfiguredDoors.txt", report.ToString());
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Debug.Log($"Universal doors saved: {added} new, {reused} reused, {skipped} skipped.");
    }
    public static void BindPassageObstacles(CorridorDestination corridor, BlackDoorTransition portal, CorridorManager manager, StringBuilder report)
    {
        var capsule = manager.loop.player.GetComponent<CapsuleCollider>();
        if (!capsule) return;
        var frame = portal.entranceFrame;
        var crossing = frame.position + frame.forward * 0.1f + Vector3.up * 0.01f;
        Vector3 scale = manager.loop.player.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float segment = Mathf.Max(0, capsule.height * Mathf.Abs(scale.y) / 2 - radius);
        Vector3 center = crossing + frame.rotation * Vector3.Scale(capsule.center, scale);
        var blockers = Physics.OverlapCapsule(center - Vector3.up * segment, center + Vector3.up * segment, radius, ~0, QueryTriggerInteraction.Ignore)
            .Where(c => c.transform.IsChildOf(corridor.transform) && c.gameObject.activeInHierarchy
            && c.name.StartsWith("Decal", StringComparison.OrdinalIgnoreCase)
            && Mathf.Abs(frame.InverseTransformPoint(c.bounds.center).z) < 0.06f).ToArray();
        Undo.RecordObject(portal.door, "Ignore only verified decorative doorway collisions after opening");
        portal.door.passageObstacles = blockers;
        foreach (var c in blockers) report.AppendLine("SCOPED player collision exemption after opening: " + PathOf(c.transform));
    }
    [MenuItem("Tools/Don't Look Back/Universal Doors/Audit All Doors")]
    public static void Audit()
    {
        var manager = UnityEngine.Object.FindObjectsByType<CorridorManager>(FindObjectsSortMode.None).Single();
        var report = new StringBuilder();
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); report.AppendLine("PASS " + message); }
        var ui = UnityEngine.Object.FindObjectsByType<DoorCrosshair>(FindObjectsSortMode.None).Single();
        Check(ui.manager == manager && ui.canvas.renderMode == RenderMode.ScreenSpaceOverlay, "One centered overlay crosshair using shared manager");
        Check(ui.crosshair.rectTransform.anchorMin == Vector2.one * 0.5f && ui.crosshair.rectTransform.anchoredPosition == Vector2.zero, "Crosshair anchored at screen center");
        Check(!ui.crosshair.raycastTarget && !ui.crossbar.raycastTarget && !ui.prompt.raycastTarget && !ui.canvas.GetComponent<CanvasGroup>().blocksRaycasts, "UI does not consume input");
        Check(manager.interactionRange == 3 && ui.interactionPrompt == "[E] Open Door", "Three meter range and default prompt");
        Check(UnityEngine.Object.FindObjectsByType<FirstPersonMovement>(FindObjectsSortMode.None).Count(x => x.isActiveAndEnabled) == 1, "One player");
        Check(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(x => x.isActiveAndEnabled) == 1, "One active camera");
        Check(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(x => x.isActiveAndEnabled) == 1, "One active AudioListener");
        Check(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length <= 1, "No duplicate EventSystem");
        manager.loop.Initialize(false); Physics.SyncTransforms();
        foreach (var entry in manager.destinations)
        {
            var corridor = entry.instance;
            Check(corridor && manager.loop.IsClear(corridor.spawnPoint.position, corridor.spawnPoint.rotation), corridor.name + " spawn is clear and grounded");
            var portals = corridor.GetComponentsInChildren<BlackDoorTransition>(true);
            var panels = Panels(corridor);
            Check(portals.Length == panels.Length, corridor.name + " portal count matches " + panels.Length + " panels");
            foreach (var renderer in panels)
            {
                var door = renderer.GetComponent<DoorInteraction>();
                Check(renderer.GetComponents<DoorInteraction>().Length == 1 && door.hinge == renderer.transform && door.blackVoid,
                    PathOf(renderer.transform) + " one interaction on moving panel");
                var portal = portals.Single(p => p.door == door);
                Check(portal.manager == manager && portal.entranceFrame && portal.Trigger.isTrigger, "Shared manager and entry trigger: " + PathOf(renderer.transform));
                Check(portal.entranceFrame.gameObject.activeSelf == renderer.gameObject.activeInHierarchy, "Dormant doors preserve inactive portals");
                var mask = door.blackVoid.GetComponent<Renderer>();
                Check(mask && mask.sharedMaterial.shader.name == "DontLookBack/Black Door Void" && !ShaderUtil.ShaderHasError(mask.sharedMaterial.shader), "Opaque unlit mask: " + PathOf(renderer.transform));
                Check(door.blackVoid.activeSelf == door.startsOpen, "Closed panel hides mask; existing open panel retains effect");
                Check(!renderer.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.AreStaticEditorFlagsSet(t.gameObject, StaticEditorFlags.BatchingStatic)), "Moving meshes excluded from static batching");
                Check(!door.animator || door.animator.transform == renderer.transform, "Animation moves only the panel");
            }
        }
        File.WriteAllText(Folder + "/Audit.txt", report.ToString()); Debug.Log("Universal door audit passed.");
    }
    [MenuItem("Tools/Don't Look Back/Universal Doors/Run All Door Play Checks")]
    public static void PlayChecks()
    {
        if (UnityEngine.Object.FindFirstObjectByType<BlackDoorTransitionManager>()) { CorridorRevisionSetup.CheckPlay(); return; }
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<UniversalDoorPlayVerifier>()) throw new InvalidOperationException("Checks already running.");
        var verifier = new GameObject("Temporary universal door verifier").AddComponent<UniversalDoorPlayVerifier>();
        verifier.StartCoroutine(verifier.Run());
    }
    [MenuItem("Tools/Don't Look Back/Universal Doors/Inspect Door Obstructions")]
    public static void Obstructions()
    {
        Physics.SyncTransforms();
        var report = new StringBuilder();
        var loop = UnityEngine.Object.FindFirstObjectByType<CorridorLoopManager>(); loop.Initialize(false);
        var actor = loop.player.GetComponent<CapsuleCollider>();
        report.AppendLine("PLAYER capsule radius=" + actor.radius + " height=" + actor.height + " center=" + actor.center + " scale=" + actor.transform.lossyScale + " mask=" + loop.obstructionMask.value);
        foreach (var portal in UnityEngine.Object.FindObjectsByType<BlackDoorTransition>(FindObjectsSortMode.None))
        {
            var frame = portal.entranceFrame;
            report.AppendLine(PathOf(portal.door.transform) + " frame=" + frame.position + " forward=" + frame.forward
                + " visible=" + portal.door.GetComponent<Renderer>().enabled + " active=" + portal.door.gameObject.activeInHierarchy
                + " colliders=" + string.Join(",", portal.door.GetComponentsInChildren<Collider>(true).Select(c => c.GetType().Name + " enabled=" + c.enabled + " bounds=" + c.bounds)));
            var ray = new Ray(frame.position - frame.forward * 0.85f + Vector3.up * 1.61f, frame.forward);
            foreach (var hit in Physics.RaycastAll(ray, 3, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                report.AppendLine("  ray " + hit.distance + " " + PathOf(hit.transform));
            var p = frame.position + frame.forward * 0.1f + Vector3.up * 0.01f;
            var center = p + frame.rotation * Vector3.Scale(actor.center, actor.transform.lossyScale);
            float radius = actor.radius * Mathf.Max(Mathf.Abs(actor.transform.lossyScale.x), Mathf.Abs(actor.transform.lossyScale.z));
            float segment = Mathf.Max(0, actor.height * Mathf.Abs(actor.transform.lossyScale.y) / 2 - radius);
            foreach (var hit in Physics.OverlapCapsule(center - Vector3.up * segment, center + Vector3.up * segment, radius - 0.025f, loop.obstructionMask, QueryTriggerInteraction.Ignore))
                report.AppendLine("  exact obstacle " + PathOf(hit.transform) + " ignored=" + Physics.GetIgnoreCollision(actor, hit));
            if (Physics.Raycast(p + Vector3.up * 0.15f, Vector3.down, out var floor, 0.4f, ~0, QueryTriggerInteraction.Ignore))
                report.AppendLine("  ground " + PathOf(floor.transform) + " normal=" + floor.normal + " distance=" + floor.distance);
            else report.AppendLine("  NO GROUND");
            foreach (var c in Physics.OverlapCapsule(p + Vector3.up * 0.3f, p + Vector3.up * 1.5f, 0.275f, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(portal.door.transform)) report.AppendLine("  crossing obstacle " + PathOf(c.transform)
                    + " local center=" + frame.InverseTransformPoint(c.bounds.center) + " size=" + c.bounds.size);
        }
        File.WriteAllText(Folder + "/Obstructions.txt", report.ToString());
    }
    [MenuItem("Tools/Don't Look Back/Universal Doors/Fix Verified Player Door Clearance")]
    public static void PlayerClearance()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var manager = UnityEngine.Object.FindFirstObjectByType<CorridorManager>();
        var capsule = manager.loop.player.GetComponent<CapsuleCollider>();
        if (!capsule) throw new InvalidOperationException("This measurement utility requires the existing capsule player.");
        float oldRadius = capsule.radius, fit = oldRadius;
        var portals = UnityEngine.Object.FindObjectsByType<BlackDoorTransition>(FindObjectsSortMode.None);
        var report = new StringBuilder();
        Vector3 scale = capsule.transform.lossyScale;
        bool Fits(float candidate)
        {
            float radius = candidate * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float segment = Mathf.Max(0, capsule.height * Mathf.Abs(scale.y) / 2 - radius);
            foreach (var portal in portals)
            for (float step = -0.3f; step < 0.31f; step += 0.05f)
            {
                var frame = portal.entranceFrame;
                var p = frame.position + frame.forward * step + Vector3.up * 0.01f;
                var center = p + frame.rotation * Vector3.Scale(capsule.center, scale);
                foreach (var hit in Physics.OverlapCapsule(center - Vector3.up * segment, center + Vector3.up * segment, radius,
                    manager.loop.obstructionMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(manager.loop.player) || hit.GetComponentInParent<DoorInteraction>()) continue;
                    if (portal.door.passageObstacles.Contains(hit)) continue;
                    report.AppendLine($"radius={candidate:F4} blocked by {PathOf(hit.transform)} at {p}");
                    return false;
                }
            }
            return true;
        }
        Physics.SyncTransforms();
        if (!Fits(oldRadius))
        {
            for (fit = Mathf.Floor(oldRadius * 100) / 100; fit >= 0.20f && !Fits(fit); fit -= 0.01f) { }
            if (fit < 0.20f) throw new InvalidOperationException("Cannot safely fit this player without reviewing doorway geometry.");
            fit -= 0.01f; // One centimeter clearance beyond the measured limit.
            Undo.RecordObject(capsule, "Fit player capsule through verified narrow door frames");
            capsule.radius = fit;
            PrefabUtility.RecordPrefabInstancePropertyModifications(capsule); EditorUtility.SetDirty(capsule);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        report.Insert(0, $"Original capsule radius={oldRadius:F7}; fitting radius with 1cm margin={fit:F7}. Center {capsule.center}, height {capsule.height}, movement settings unchanged.\n");
        File.WriteAllText(Folder + "/PlayerClearance.txt", report.ToString());
        Debug.Log($"Door clearance measured; capsule radius {oldRadius:F4} -> {fit:F4}.");
    }
}
