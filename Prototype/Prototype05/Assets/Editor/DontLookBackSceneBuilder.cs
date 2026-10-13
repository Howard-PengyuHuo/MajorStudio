using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class DontLookBackSceneBuilder
{
    const string Folder = "Assets/DontLookBack";
    const string ScenePath = Folder + "/DontLookBack.unity";
    static GameObject Root(Scene s, string name) => s.GetRootGameObjects().Single(r => r.name == name);
    static void CollectLighting(GameObject source, GameObject target, CorridorLightingCache cache,
        List<CorridorLightingCache.Surface> surfaces, List<CorridorLightingCache.Probe> probes,
        List<CorridorLightingCache.Lamp> lamps, List<CorridorLightingCache.Atlas> atlases)
    {
        var originals = source.GetComponentsInChildren<Renderer>(true);
        var copies = target.GetComponentsInChildren<Renderer>(true);
        if (originals.Length != copies.Length) throw new InvalidOperationException("Renderer clone mismatch.");
        for (int i = 0; i < originals.Length; i++)
        {
            var r = originals[i];
            if (r.lightmapIndex >= 0 && r.lightmapIndex < LightmapSettings.lightmaps.Length)
            {
                var map = LightmapSettings.lightmaps[r.lightmapIndex];
                int index = atlases.FindIndex(a => a.color == map.lightmapColor);
                if (index < 0) { index = atlases.Count; atlases.Add(new CorridorLightingCache.Atlas { color = map.lightmapColor, direction = map.lightmapDir, shadowMask = map.shadowMask }); }
                surfaces.Add(new CorridorLightingCache.Surface { renderer = copies[i], atlas = index, scaleOffset = r.lightmapScaleOffset });
            }
            else
            {
                LightProbes.GetInterpolatedProbe(r.probeAnchor ? r.probeAnchor.position : r.bounds.center, r, out var sh);
                var values = new float[27];
                for (int rgb = 0; rgb < 3; rgb++) for (int k = 0; k < 9; k++) values[rgb * 9 + k] = sh[rgb, k];
                probes.Add(new CorridorLightingCache.Probe { renderer = copies[i], coefficients = values });
                copies[i].probeAnchor = null; // No references back into either demo scene.
            }
        }
        var sourceLights = source.GetComponentsInChildren<Light>(true);
        var copyLights = target.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < sourceLights.Length; i++)
        {
            var output = sourceLights[i].bakingOutput;
            lamps.Add(new CorridorLightingCache.Lamp { light = copyLights[i], isBaked = output.isBaked,
                bakeType = output.lightmapBakeType, mixedMode = output.mixedLightingMode,
                occlusionMaskChannel = output.occlusionMaskChannel, probeOcclusionLightIndex = output.probeOcclusionLightIndex });
        }
        var sourceProbes = source.GetComponentsInChildren<ReflectionProbe>(true);
        var copyProbes = target.GetComponentsInChildren<ReflectionProbe>(true);
        for (int i = 0; i < sourceProbes.Length; i++) if (sourceProbes[i].texture)
        { copyProbes[i].customBakedTexture = sourceProbes[i].texture; copyProbes[i].mode = ReflectionProbeMode.Custom; }
        foreach (var helper in target.GetComponentsInChildren<CorridorBakedLightingReuse>(true)) UnityEngine.Object.DestroyImmediate(helper);
    }
    [MenuItem("Tools/Don't Look Back/Build Playable Scene")]
    public static void Build()
    {
        if (Application.isPlaying || File.Exists(ScenePath)) throw new InvalidOperationException("Stop Play mode; builder refuses to overwrite an existing DontLookBack scene.");
        var demo1 = SceneManager.GetSceneByName("Demo 1");
        var demo2 = SceneManager.GetSceneByName("Demo 2");
        if (!demo1.isLoaded || !demo2.isLoaded) throw new InvalidOperationException("Load Demo 1 and Demo 2 additively first.");
        Inspect();
        Directory.CreateDirectory("IntegrationBackups");
        File.Copy(demo1.path, "IntegrationBackups/Demo-1.unity", false);
        File.Copy(demo2.path, "IntegrationBackups/Demo-2.unity", false);
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(Folder + "/Backups");
        if (!EditorSceneManager.SaveScene(demo1, Folder + "/Backups/Demo1BeforeIntegration.unity", true)
            || !EditorSceneManager.SaveScene(demo2, Folder + "/Backups/Demo2BeforeIntegration.unity", true))
            throw new IOException("Cannot preserve the current demo states.");
        if (!EditorSceneManager.SaveScene(demo1, ScenePath, true)) throw new IOException("Cannot create Demo 1 scene copy.");
        var game = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(game);
        var a = new GameObject("Corridor_A");
        SceneManager.MoveGameObjectToScene(a, game);
        var b = new GameObject("Corridor_B");
        SceneManager.MoveGameObjectToScene(b, game);
        var system = new GameObject("Corridor Loop System");
        SceneManager.MoveGameObjectToScene(system, game);
        var cache = system.AddComponent<CorridorLightingCache>();
        var surfaces = new List<CorridorLightingCache.Surface>();
        var probes = new List<CorridorLightingCache.Probe>();
        var lamps = new List<CorridorLightingCache.Lamp>();
        var atlases = new List<CorridorLightingCache.Atlas>();
        foreach (string name in new[] { "Assets", "Corridor", "Corridor (1)", "Lighting" })
        {
            var source = Root(demo1, name);
            var target = Root(game, name);
            CollectLighting(source, target, cache, surfaces, probes, lamps, atlases);
            target.transform.SetParent(a.transform, true);
        }
        GameObject CloneB(string sourceName, string targetName, Vector3 translation)
        {
            var source = Root(demo2, sourceName);
            var target = UnityEngine.Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(target, game);
            target.name = targetName;
            CollectLighting(source, target, cache, surfaces, probes, lamps, atlases);
            target.transform.position += translation;
            target.transform.SetParent(b.transform, true);
            return target;
        }
        Vector3 separation = new Vector3(200, -5.69f, 0);
        CloneB("Assets", "Original Environment", separation);
        CloneB("Lighting", "Lighting", separation);
        CloneB("Corridor2", "Corridor2", separation + new Vector3(0, 0, 0.14f));
        CloneB("Corridor2", "Corridor2 Extension", separation + new Vector3(0, 0, -16.59f));
        cache.atlases = atlases.ToArray(); cache.surfaces = surfaces.ToArray();
        cache.probes = probes.ToArray(); cache.lamps = lamps.ToArray();
        cache.Apply();
        var player = Root(game, "First Person Controller Minimal");
        var camera = player.GetComponentInChildren<Camera>(true);
        foreach (var root in game.GetRootGameObjects()) foreach (var other in root.GetComponentsInChildren<Camera>(true))
            if (other != camera) { other.enabled = false; foreach (var listener in other.GetComponents<AudioListener>()) listener.enabled = false; }
        camera.enabled = true; camera.tag = "MainCamera"; camera.farClipPlane = 80;
        var gameplayListener = camera.GetComponent<AudioListener>();
        if (!gameplayListener) gameplayListener = camera.gameObject.AddComponent<AudioListener>();
        gameplayListener.enabled = true;
        // Match Demo 1's original camera rendering settings without creating a second volume.
        var sourceCamera = Root(demo1, "Camera").GetComponent<Camera>();
        var cameraData = camera.GetUniversalAdditionalCameraData();
        var originalData = sourceCamera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = originalData.renderPostProcessing;
        foreach (var renderer in player.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false; // Editor capsule is not a first-person body.
        var manager = system.AddComponent<CorridorLoopManager>();
        manager.player = player.transform; manager.view = camera.transform;
        manager.mouseLook = camera.GetComponent<FirstPersonLook>();
        Transform StartPoint(GameObject parent, string name, Vector3 position)
        { var point = new GameObject(name).transform; point.SetParent(parent.transform); point.position = position; return point; }
        var startA = StartPoint(a, "Start_A", new Vector3(0, -5.68f, -33.77f));
        var startB = StartPoint(b, "Start_B", new Vector3(200, -5.68f, -33.77f));
        manager.corridors = new[] {
            new CorridorLoopManager.Corridor { name = "Corridor_A", environment = a.transform, startPoint = startA },
            new CorridorLoopManager.Corridor { name = "Corridor_B", environment = b.transform, startPoint = startB } };
        player.transform.SetPositionAndRotation(startA.position, startA.rotation);
        manager.Initialize(false);
        EditorSceneManager.CloseScene(demo1, true);
        EditorSceneManager.CloseScene(demo2, true);
        cache.Apply();
        Physics.SyncTransforms();
        if (!manager.IsClear(startA.position, startA.rotation) || !manager.IsClear(startB.position, startB.rotation))
            throw new InvalidOperationException("Start-point collision check failed; integrated scene left open for repair.");
        foreach (var root in game.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject); // Imported obsolete Built-in post-processing references only.
        EditorUtility.SetDirty(cache); EditorUtility.SetDirty(manager);
        EditorSceneManager.SaveScene(game);
        // Close originals without saving; only the new game scene should be loaded in Play mode.
        cache.Apply();
        EditorSceneManager.SaveScene(game);
        var builds = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        builds.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = builds.ToArray();
        File.WriteAllText("IntegrationBackups/BuildReport.txt", $"Created {ScenePath}. A includes Assets + Corridor + Corridor (1), continuous floor run -35.06..22.75. B uses Demo 2 original environment and two copies of Corridor2 to match that footprint, translated 200m on X. {surfaces.Count} atlas bindings, {probes.Count} probe bindings, {lamps.Count} baked lights, {atlases.Count} atlases. One player/camera/listener; original demo cameras disabled in new scene. Original scenes closed without saving. Starts validated clear. Build scene placed first.");
        Selection.activeGameObject = system;
        Debug.Log(File.ReadAllText("IntegrationBackups/BuildReport.txt"));
    }
    [MenuItem("Tools/Don't Look Back/Finalize Integrated Scene")]
    public static void FinalizeScene()
    {
        var game = SceneManager.GetActiveScene();
        if (game.name != "DontLookBack") throw new InvalidOperationException("Open the integrated scene first.");
        var system = Root(game, "Corridor Loop System");
        var cache = system.GetComponent<CorridorLightingCache>();
        var manager = system.GetComponent<CorridorLoopManager>();
        // The original scenes would overlap A during physics checks if kept loaded.
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene != game && (scene.name == "Demo 1" || scene.name == "Demo 2")) EditorSceneManager.CloseScene(scene, true);
        }
        cache.Apply(); manager.Initialize(false); Physics.SyncTransforms();
        foreach (var corridor in manager.corridors)
            if (!manager.IsClear(corridor.startPoint.position, corridor.startPoint.rotation)) throw new InvalidOperationException("Obstructed start: " + corridor.name);
        foreach (var root in game.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        EditorSceneManager.MarkSceneDirty(game);
        EditorSceneManager.SaveScene(game);
        var builds = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        builds.Insert(0, new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = builds.ToArray();
        File.WriteAllText("IntegrationBackups/BuildReport.txt", $"Created {ScenePath}; Demo 1 based scene. Corridor_A groups Assets, Corridor, Corridor (1), Lighting; continuous floors -35.06..22.75. Corridor_B uses corresponding Demo 2 modules, 200m away, with matching floor coverage. {cache.surfaces.Length} atlas bindings; {cache.probes.Length} probe bindings; {cache.lamps.Length} baked lamps; {cache.atlases.Length} atlases. Two clear starts. Original disk scenes unchanged; loaded unsaved states backed up. Only new scene loaded. One player camera/listener. First build scene.");
        Debug.Log(File.ReadAllText("IntegrationBackups/BuildReport.txt"));
    }
    [MenuItem("Tools/Don't Look Back/Open B Connector Door")]
    public static void OpenConnector()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "DontLookBack" || Application.isPlaying) throw new InvalidOperationException("Open DontLookBack in Edit mode.");
        var b = Root(scene, "Corridor_B");
        var section = b.transform.Find("Corridor2");
        var door = section.Find("Wall with Door B 10/Door 1 B/Door");
        var decal = section.Find("Decal 1 (1)");
        if (!decal) throw new InvalidOperationException("Connector already opened, or expected doorway changed.");
        EditorSceneManager.SaveScene(scene, Folder + "/Backups/DontLookBackBeforeConnector.unity", true);
        Undo.SetCurrentGroupName("Open connecting doorway");
        Undo.SetTransformParent(decal, door, "Keep door decal attached");
        Undo.RecordObject(door, "Open door panel");
        door.Rotate(0, 100, 0, Space.Self);
        var capsule = Root(scene, "First Person Controller Minimal").GetComponent<CapsuleCollider>();
        Undo.RecordObject(capsule, "Fit player through authored doorway");
        capsule.radius = 0.3f; // Authored door opening is narrower than the preview capsule's 1m diameter.
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.AppendAllText("IntegrationBackups/BuildReport.txt", "\nOpened B's internal connector door panel 100 degrees; attached its stain decal to the panel. Retained all environment collider components. Player capsule radius 0.5 -> 0.3m to fit the authored doorway; height, movement and audio unchanged.");
    }
    [MenuItem("Tools/Don't Look Back/Inspect Demo Scenes")]
    public static void Inspect()
    {
        Directory.CreateDirectory("IntegrationBackups");
        var report = new StringBuilder();
        report.AppendLine("Global lightmap count: " + LightmapSettings.lightmaps.Length);
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            report.AppendLine($"SCENE {scene.name} dirty={scene.isDirty} path={scene.path}");
            foreach (var root in scene.GetRootGameObjects())
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                report.AppendLine($"ROOT {root.name} position={root.transform.position:F3} boundsMin={bounds.min:F3} boundsMax={bounds.max:F3} renderers={renderers.Length} lights={root.GetComponentsInChildren<Light>(true).Length}");
                foreach (var floor in renderers.Where(r => r.name.StartsWith("Floor")))
                    report.AppendLine($" FLOOR {floor.name} center={floor.bounds.center:F3} min={floor.bounds.min:F3} max={floor.bounds.max:F3} lm={floor.lightmapIndex}");
            }
        }
        File.WriteAllText("IntegrationBackups/Inspection.txt", report.ToString());
        Debug.Log("Demo scene inspection written to IntegrationBackups/Inspection.txt");
    }
}
