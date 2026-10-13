using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BlackDoorSetup
{
    public const string ReportFolder = "IntegrationBackups/BlackDoors";
    [MenuItem("Tools/Don't Look Back/Black Doors/Create Prefab Test Fixture")]
    public static void Fixture()
    {
        const string path = "Assets/DontLookBack/Tests/PrefabDestinationFixture.prefab";
        if (File.Exists(path)) return;
        var root = new GameObject("Prefab destination test fixture");
        try
        {
            var marker = root.AddComponent<CorridorDestination>();
            var spawn = new GameObject("SpawnPoint").transform; spawn.SetParent(root.transform, false); spawn.localPosition = Vector3.up * 0.01f;
            marker.spawnPoint = spawn; marker.lookBackStart = spawn;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = Vector3.down * 0.05f; floor.transform.localScale = new Vector3(10, 0.1f, 10);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/AK Studio Art/Modular Horror Corridor/Materials/Materials General/Tiles.mat");
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    [MenuItem("Tools/Don't Look Back/Black Doors/Run Play Mode Checks")]
    public static void RunChecks()
    {
        if (UnityEngine.Object.FindFirstObjectByType<BlackDoorTransitionManager>()) { CorridorRevisionSetup.CheckPlay(); return; }
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<BlackDoorPlayVerifier>()) throw new InvalidOperationException("Checks already running.");
        var verifier = new GameObject("Temporary black door verifier").AddComponent<BlackDoorPlayVerifier>();
        verifier.prefabFixture = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DontLookBack/Tests/PrefabDestinationFixture.prefab");
        verifier.existingOpeningController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Arts/Animations/Door.controller");
        verifier.StartCoroutine(verifier.Run());
    }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    [MenuItem("Tools/Don't Look Back/Black Doors/Export Selected Corridor Prefab")]
    public static void ExportCorridor()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Export in Edit mode.");
        var source = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<CorridorDestination>() : null;
        if (!source || !source.HasValidReferences) throw new InvalidOperationException("Select a corridor root with valid CorridorDestination references.");
        string folder = "Assets/DontLookBack/Prefabs";
        Directory.CreateDirectory(folder);
        string path = folder + "/" + source.name + ".prefab";
        if (File.Exists(path)) throw new IOException("Prefab exists; refusing to overwrite it: " + path);
        var sourceCache = source.GetComponent<CorridorLightingCache>();
        if (!sourceCache) sourceCache = UnityEngine.Object.FindFirstObjectByType<CorridorManager>().GetComponent<CorridorLightingCache>();
        if (!sourceCache) throw new InvalidOperationException("Scene lighting cache missing.");
        var wrapper = new GameObject(source.name);
        wrapper.SetActive(false);
        wrapper.transform.SetPositionAndRotation(source.spawnPoint.position, source.spawnPoint.rotation);
        try
        {
            var clone = UnityEngine.Object.Instantiate(source.gameObject, wrapper.transform, true);
            clone.name = "Environment";
            var copiedMarker = clone.GetComponent<CorridorDestination>();
            var marker = wrapper.AddComponent<CorridorDestination>();
            marker.spawnPoint = copiedMarker.spawnPoint; marker.lookBackStart = copiedMarker.lookBackStart;
            marker.audioProfile = copiedMarker.audioProfile;
            marker.kind = copiedMarker.kind; marker.doorExits = copiedMarker.doorExits;
            foreach (var exit in marker.doorExits) if (exit) exit.environment = marker;
            UnityEngine.Object.DestroyImmediate(copiedMarker);
            var rendererMap = source.GetComponentsInChildren<Renderer>(true).Zip(clone.GetComponentsInChildren<Renderer>(true), (a, b) => new { a, b }).ToDictionary(p => p.a, p => p.b);
            var lightMap = source.GetComponentsInChildren<Light>(true).Zip(clone.GetComponentsInChildren<Light>(true), (a, b) => new { a, b }).ToDictionary(p => p.a, p => p.b);
            var cache = wrapper.AddComponent<CorridorLightingCache>();
            cache.atlases = sourceCache.atlases.ToArray();
            cache.surfaces = sourceCache.surfaces.Where(s => s.renderer && rendererMap.ContainsKey(s.renderer)).Select(s => new CorridorLightingCache.Surface {
                renderer = rendererMap[s.renderer], atlas = s.atlas, scaleOffset = s.scaleOffset }).ToArray();
            cache.probes = sourceCache.probes.Where(p => p.renderer && rendererMap.ContainsKey(p.renderer)).Select(p => new CorridorLightingCache.Probe {
                renderer = rendererMap[p.renderer], coefficients = (float[])p.coefficients.Clone() }).ToArray();
            cache.lamps = sourceCache.lamps.Where(l => l.light && lightMap.ContainsKey(l.light)).Select(l => new CorridorLightingCache.Lamp {
                light = lightMap[l.light], isBaked = l.isBaked, bakeType = l.bakeType, mixedMode = l.mixedMode,
                occlusionMaskChannel = l.occlusionMaskChannel, probeOcclusionLightIndex = l.probeOcclusionLightIndex }).ToArray();
            foreach (var door in clone.GetComponentsInChildren<BlackDoorTransition>(true)) door.manager = null;
            wrapper.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(wrapper, path);
            Debug.Log("Exported normalized corridor prefab with its lighting cache: " + path);
        }
        finally { UnityEngine.Object.DestroyImmediate(wrapper); }
    }
    [MenuItem("Tools/Don't Look Back/Black Doors/Audit Setup")]
    public static void Audit()
    {
        var manager = UnityEngine.Object.FindFirstObjectByType<CorridorManager>();
        if (!manager || !manager.loop || !manager.screenFade || !manager.gameplayCamera) throw new InvalidOperationException("Transition system references missing.");
        manager.loop.Initialize(false); Physics.SyncTransforms();
        var report = new StringBuilder();
        foreach (var entry in manager.destinations)
        {
            if (entry == null || !entry.instance || !entry.instance.HasValidReferences
                || !manager.loop.IsClear(entry.instance.spawnPoint.position, entry.instance.spawnPoint.rotation)) throw new InvalidOperationException("Invalid initial destination.");
            report.AppendLine("PASS valid loaded destination " + entry.instance.name);
        }
        foreach (var door in UnityEngine.Object.FindObjectsByType<BlackDoorTransition>(FindObjectsSortMode.None))
        {
            if (!door.Trigger.isTrigger || !door.entranceFrame || !door.door || door.manager != manager || !door.door.blackVoid) throw new InvalidOperationException("Door wiring incomplete.");
            var renderer = door.door.blackVoid.GetComponent<Renderer>();
            if (!renderer || !renderer.sharedMaterial || renderer.sharedMaterial.shader.name != "DontLookBack/Black Door Void"
                || ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader)) throw new InvalidOperationException("Invalid opaque void shader.");
            if (door.door.hinge.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.AreStaticEditorFlagsSet(t.gameObject, StaticEditorFlags.BatchingStatic)))
                throw new InvalidOperationException("Moving panel is still static-batched.");
            report.AppendLine("PASS portal mask, entrance trigger and moving mesh: " + PathOf(door.door.transform));
        }
        File.WriteAllText(ReportFolder + "/SetupAudit.txt", report.ToString());
        Debug.Log("Black door setup audit passed.");
    }
    static GameObject NewObject(string name, Transform parent)
    { var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, name); go.transform.SetParent(parent, false); return go; }
    static Material VoidMaterial()
    {
        const string path = "Assets/DontLookBack/Materials/BlackDoorVoid.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;
        var shader = Shader.Find("DontLookBack/Black Door Void");
        if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Black void shader must compile first.");
        Directory.CreateDirectory("Assets/DontLookBack/Materials");
        var material = new Material(shader) { name = "BlackDoorVoid" };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
    public static BlackDoorTransition ConfigureDoor(Transform panel, CorridorDestination corridor, CorridorManager manager, Material material)
    {
        if (!panel) throw new InvalidOperationException("Select a door panel.");
        var existing = panel.GetComponent<DoorInteraction>();
        var existingPortal = corridor.GetComponentsInChildren<BlackDoorTransition>(true).SingleOrDefault(d => d.door == existing && existing);
        if (existingPortal)
        {
            Undo.RecordObject(existingPortal, "Repair shared door manager");
            existingPortal.manager = manager;
            Undo.RecordObject(existing, "Preserve existing door animation");
            BindAnimation(panel, existing);
            EditorUtility.SetDirty(existing);
            PrefabUtility.RecordPrefabInstancePropertyModifications(existing);
            PrefabUtility.RecordPrefabInstancePropertyModifications(existingPortal);
            return existingPortal;
        }
        if (existing && existing.blackVoid) throw new InvalidOperationException("Existing black surface has no linked trigger; review instead of duplicating it.");
        var renderer = panel.GetComponent<MeshRenderer>();
        if (!renderer || !panel.GetComponent<Collider>()) throw new InvalidOperationException("Door needs its existing panel renderer and collider.");
        var bounds = renderer.bounds;
        bool alreadyOpen = Quaternion.Angle(panel.localRotation, Quaternion.identity) > 20;
        if (alreadyOpen)
        {
            // The integrated connector was deliberately left open. Locate its original
            // doorway from the mesh bounds without changing the current panel pose.
            var matrix = Matrix4x4.TRS(panel.position, panel.parent.rotation, panel.lossyScale);
            var local = renderer.localBounds;
            var ext = matrix.MultiplyVector(new Vector3(local.extents.x, 0, 0));
            var ey = matrix.MultiplyVector(new Vector3(0, local.extents.y, 0));
            var ez = matrix.MultiplyVector(new Vector3(0, 0, local.extents.z));
            bounds = new Bounds(matrix.MultiplyPoint3x4(local.center), new Vector3(
                Mathf.Abs(ext.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x),
                Mathf.Abs(ext.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y),
                Mathf.Abs(ext.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z)) * 2);
        }
        Vector3 outward = Vector3.ProjectOnPlane(-panel.parent.forward, Vector3.up).normalized;
        Vector3 corridorForward = Vector3.ProjectOnPlane(corridor.spawnPoint.forward, Vector3.up).normalized;
        if (Mathf.Abs(Vector3.Dot(outward, corridorForward)) > 0.9f)
        {
            // End and internal doors: entrance is the side toward the configured spawn.
            if (Vector3.Dot(outward, bounds.center - corridor.spawnPoint.position) < 0) outward = -outward;
        }
        var frame = NewObject("Black Door - " + panel.parent.name + " - " + panel.parent.parent.name, corridor.transform).transform;
        frame.SetPositionAndRotation(new Vector3(bounds.center.x, corridor.spawnPoint.position.y - 0.01f, bounds.center.z), Quaternion.LookRotation(outward, Vector3.up));
        float width = Mathf.Max(0.9f, Mathf.Abs(frame.right.x) * bounds.size.x + Mathf.Abs(frame.right.z) * bounds.size.z + 0.08f);
        float height = Mathf.Max(2.1f, bounds.size.y + 0.16f);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(cube, "Add opaque darkness");
        cube.name = "Opaque Black Void"; cube.transform.SetParent(frame, false);
        cube.transform.localPosition = new Vector3(0, height / 2, 0.8f);
        cube.transform.localScale = new Vector3(width, height, 1.5f);
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        cube.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cube.GetComponent<MeshRenderer>().receiveShadows = false;
        cube.SetActive(false);
        var floor = NewObject("Dark Doorway Floor", frame).AddComponent<BoxCollider>();
        floor.center = new Vector3(0, -0.05f, 0.65f); floor.size = new Vector3(width, 0.1f, 1.5f);
        var triggerObject = NewObject("Entrance Trigger", frame);
        var trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true; trigger.center = new Vector3(0, 0.9f, 0.15f); trigger.size = new Vector3(width - 0.02f, 1.8f, 0.6f);
        var interaction = existing ? existing : Undo.AddComponent<DoorInteraction>(panel.gameObject);
        Undo.RecordObject(interaction, "Configure existing door interaction");
        foreach (var part in panel.GetComponentsInChildren<Transform>(true))
        {
            Undo.RecordObject(part.gameObject, "Allow door movement");
            GameObjectUtility.SetStaticEditorFlags(part.gameObject, GameObjectUtility.GetStaticEditorFlags(part.gameObject) & ~StaticEditorFlags.BatchingStatic);
            PrefabUtility.RecordPrefabInstancePropertyModifications(part.gameObject);
        }
        interaction.hinge = panel; interaction.blackVoid = cube; interaction.startsOpen = alreadyOpen;
        interaction.releasePanelCollidersWhenOpen = true;
        cube.SetActive(alreadyOpen);
        Vector3 relative = bounds.center - panel.position;
        float plus = Vector3.Dot(Quaternion.AngleAxis(100, Vector3.up) * relative, outward);
        float minus = Vector3.Dot(Quaternion.AngleAxis(-100, Vector3.up) * relative, outward);
        interaction.openAngle = plus >= minus ? 100 : -100;
        BindAnimation(panel, interaction);
        var transition = triggerObject.AddComponent<BlackDoorTransition>();
        transition.manager = manager; transition.door = interaction; transition.entranceFrame = frame;
        EditorUtility.SetDirty(interaction); EditorUtility.SetDirty(transition);
        PrefabUtility.RecordPrefabInstancePropertyModifications(interaction);
        return transition;
    }
    static void BindAnimation(Transform panel, DoorInteraction interaction)
    {
        if (interaction.animator || interaction.startsOpen) return;
        var animator = panel.GetComponentsInParent<Animator>(true).FirstOrDefault(a => a.transform == panel);
        if (animator && animator.runtimeAnimatorController is AnimatorController controller)
        {
            var state = controller.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0);
            if (state)
            {
                interaction.animator = animator;
                interaction.openingState = controller.layers[0].name + "." + state.name;
                if (state.motion is AnimationClip clip) interaction.openDuration = clip.length;
            }
        }
    }
    [MenuItem("Tools/Don't Look Back/Black Doors/Set Up Initial Doors")]
    public static void Setup()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "DontLookBack" || Application.isPlaying) throw new InvalidOperationException("Open DontLookBack in Edit mode.");
        var roots = scene.GetRootGameObjects();
        var loop = roots.SelectMany(r => r.GetComponentsInChildren<CorridorLoopManager>(true)).Single();
        if (loop.GetComponent<CorridorManager>()) throw new InvalidOperationException("System is already configured; refusing to duplicate it.");
        var panelA = loop.corridors[0].environment.Find("Corridor (1)/Wall with Door A 12/Door 2 B/Door");
        var panelB = loop.corridors[1].environment.Find("Corridor2 Extension/Wall with Door B 01/Door 1 B/Door");
        if (!panelA || !panelB) throw new InvalidOperationException("Initial side-door paths changed; use selected-door setup instead.");
        Inspect();
        var material = VoidMaterial();
        Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Add black door mechanic");
        var manager = Undo.AddComponent<CorridorManager>(loop.gameObject);
        var fade = Undo.AddComponent<ScreenFadeController>(loop.gameObject);
        fade.EnsureOverlay();
        Undo.RegisterCreatedObjectUndo(fade.overlay.gameObject, "Create screen fade");
        manager.loop = loop; manager.gameplayCamera = loop.view.GetComponent<Camera>(); manager.screenFade = fade;
        manager.controlsToSuspend = new Behaviour[] { loop.player.GetComponent<FirstPersonMovement>(), loop.mouseLook };
        manager.destinations = new CorridorManager.Destination[loop.corridors.Length];
        for (int i = 0; i < loop.corridors.Length; i++)
        {
            var definition = loop.corridors[i];
            var marker = Undo.AddComponent<CorridorDestination>(definition.environment.gameObject);
            var spawn = NewObject("SpawnPoint", definition.environment).transform;
            spawn.SetPositionAndRotation(definition.startPoint.position, definition.startPoint.rotation);
            marker.spawnPoint = spawn; marker.lookBackStart = definition.startPoint;
            manager.destinations[i] = new CorridorManager.Destination { instance = marker };
            EditorUtility.SetDirty(marker);
        }
        ConfigureDoor(panelA, manager.destinations[0].instance, manager, material);
        ConfigureDoor(panelB, manager.destinations[1].instance, manager, material);
        Undo.CollapseUndoOperations(undoGroup);
        EditorUtility.SetDirty(manager); EditorUtility.SetDirty(fade);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        File.WriteAllText(ReportFolder + "/Setup.txt", "Configured two side doors nearest the initial spawns (left side, z=-33.11). Press E while looking at a panel within 2.5m. Each has an opaque unlit black enclosure, support floor and entrance-side trigger. Corridor_A/B loaded instances form the pool; separate SpawnPoints reference clear starts. One overlay canvas; existing player/camera reused. No original mesh, material, collider or light component changed. BeforeBlackDoors.unity preserves the pre-setup unsaved state.");
        Debug.Log("Black door mechanic configured and saved.");
    }
    [MenuItem("Tools/Don't Look Back/Black Doors/Configure Selected Door Panel")]
    public static void SelectedDoor()
    {
        if (Application.isPlaying || !Selection.activeTransform) throw new InvalidOperationException("Select a door panel in Edit mode.");
        var marker = Selection.activeTransform.GetComponentInParent<CorridorDestination>();
        var manager = UnityEngine.Object.FindFirstObjectByType<CorridorManager>();
        if (!marker || !manager) throw new InvalidOperationException("Selected panel must belong to a configured destination corridor.");
        ConfigureDoor(Selection.activeTransform, marker, manager, VoidMaterial());
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Selected door configured; save the scene to retain it.");
    }
    [MenuItem("Tools/Don't Look Back/Black Doors/Prepare Movable Door Meshes")]
    public static void PrepareMeshes()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        foreach (var door in UnityEngine.Object.FindObjectsByType<DoorInteraction>(FindObjectsSortMode.None))
        foreach (var part in door.hinge.GetComponentsInChildren<Transform>(true))
        {
            Undo.RecordObject(part.gameObject, "Allow interactive door movement");
            GameObjectUtility.SetStaticEditorFlags(part.gameObject, GameObjectUtility.GetStaticEditorFlags(part.gameObject) & ~StaticEditorFlags.BatchingStatic);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }
    [MenuItem("Tools/Don't Look Back/Black Doors/Inspect And Back Up")]
    public static void Inspect()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "DontLookBack" || Application.isPlaying) throw new InvalidOperationException("Open DontLookBack in Edit mode.");
        Directory.CreateDirectory(ReportFolder);
        const string backup = "Assets/DontLookBack/Backups/BeforeBlackDoors.unity";
        if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Cannot back up scene.");
        var report = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects())
        foreach (var panel in root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "Door"))
            report.AppendLine(PathOf(panel.transform) + " hinge=" + panel.transform.position + " bounds=" + panel.bounds
                + " animators=" + panel.GetComponentsInParent<Animator>(true).Length + " colliders=" + panel.GetComponentsInChildren<Collider>(true).Length);
        File.WriteAllText(ReportFolder + "/DoorsBefore.txt", report.ToString());
        Debug.Log("Door inspection and scene backup complete.");
    }
}
