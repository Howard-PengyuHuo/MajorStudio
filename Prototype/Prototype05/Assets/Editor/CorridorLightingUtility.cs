using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class CorridorLightingUtility
{
    static string Output => SceneManager.GetActiveScene().name == "Demo 1" ? "LightingBackups/Corridor" : "LightingBackups/" + SceneManager.GetActiveScene().name.Replace(" ", "-");
    static GameObject CopyRoot() => SceneManager.GetActiveScene().GetRootGameObjects().Single(r => r.name.StartsWith("Corridor", StringComparison.Ordinal));
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    [Serializable] public class Snapshot
    {
        public string scene, pipeline, lightingData, lightingSettings, ambientMode;
        public float ambientIntensity, reflectionIntensity;
        public Color ambientColor;
        public int lightmaps, lightProbeCount;
        public List<RendererRow> renderers = new List<RendererRow>();
        public List<LightRow> lights = new List<LightRow>();
        public List<ProbeRow> probes = new List<ProbeRow>();
    }
    [Serializable] public class RendererRow
    {
        public string path, mesh, materials, receiveGI, probeUsage, reflectionUsage;
        public int id, lightmapIndex, realtimeLightmapIndex;
        public Vector4 lightmapST;
        public Vector3 position;
        public Vector3 rotation, scale;
        public string meshName;
        public bool staticGI;
    }
    [Serializable] public class LightRow
    {
        public string path, type, bakeType, shadows;
        public Vector3 position;
        public float intensity, range, indirect;
        public bool enabled;
        public int cullingMask;
        public bool isBaked;
        public string mixedMode;
    }
    [Serializable] public class ProbeRow
    {
        public string path, mode, texture;
        public Vector3 position, size, center;
        public float intensity, blendDistance;
        public int importance;
        public bool boxProjection;
    }
    static IEnumerable<T> All<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));
    [MenuItem("Tools/Corridor Lighting/Verify Matched Views")]
    public static void VerifyViews()
    {
        var original = All<MeshRenderer>().Single(r => PathOf(r.transform) == "Assets/Floor/Floor 06");
        var copy = CopyRoot().GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor 06");
        var offset = copy.transform.position - original.transform.position;
        CaptureView(original.transform.position + new Vector3(0, 1.65f, 0.5f), Output + "/OriginalReference.png");
        CaptureView(original.transform.position + new Vector3(0, 1.65f, 0.5f) + offset, Output + "/DuplicateMatched.png");
    }
    static void CaptureView(Vector3 position, string path)
    {
        var go = new GameObject("Temporary lighting verification camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.rotation = Quaternion.identity;
        camera.fieldOfView = 65;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 80;
        var target = new RenderTexture(960, 600, 24);
        var image = new Texture2D(960, 600, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
    [MenuItem("Tools/Corridor Lighting/Match Duplicated Corridor To Original")]
    public static void Repair()
    {
        var scene = SceneManager.GetActiveScene();
        var original = scene.GetRootGameObjects().Single(r => r.name == "Assets");
        var copy = CopyRoot();
        if (copy.GetComponent<CorridorBakedLightingReuse>()) throw new InvalidOperationException("Lighting repair already applied.");
        var sources = original.GetComponentsInChildren<MeshRenderer>(true);
        var pairs = new List<(MeshRenderer target, MeshRenderer source)>();
        var probeOnly = new HashSet<MeshRenderer>();
        var referenceFloor = sources.Single(r => PathOf(r.transform) == "Assets/Floor/Floor 06");
        var copiedFloor = copy.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor 06");
        Vector3? offset = copiedFloor.transform.position - referenceFloor.transform.position;
        foreach (var target in copy.GetComponentsInChildren<MeshRenderer>(true))
        {
            string relative = PathOf(target.transform).Substring(copy.name.Length + 1);
            // Duplication may rename/flatten corner and support objects. Resolve
            // by the exact mesh and translated transform, not names alone.
            var compatible = sources.Where(r =>
                r.GetComponent<MeshFilter>().sharedMesh == target.GetComponent<MeshFilter>().sharedMesh
                && r.sharedMaterials.SequenceEqual(target.sharedMaterials)
                && Quaternion.Angle(r.transform.rotation, target.transform.rotation) <= 0.1f
                && Vector3.Distance(r.transform.lossyScale, target.transform.lossyScale) <= 0.001f).ToArray();
            var candidates = compatible.Where(r => Vector3.Distance(target.transform.position - r.transform.position, offset.Value) <= 0.002f).ToArray();
            var named = candidates.Where(r => PathOf(r.transform).EndsWith("/" + relative, StringComparison.Ordinal)).ToArray();
            MeshRenderer source;
            if (candidates.Length == 0)
            {
                if (compatible.Length == 0) throw new InvalidOperationException("No compatible original mesh: " + relative);
                source = compatible.OrderBy(r => Vector3.Distance(target.transform.position - offset.Value, r.transform.position)).First();
                // Independently moved decorations cannot reuse baked occlusion
                // correctly. Match their original-area probe sample instead.
                probeOnly.Add(target);
            }
            else source = named.Length == 1 ? named[0] : candidates.Single();
            if (source.GetComponent<MeshFilter>().sharedMesh != target.GetComponent<MeshFilter>().sharedMesh
                || !source.sharedMaterials.SequenceEqual(target.sharedMaterials)
                || Quaternion.Angle(source.transform.rotation, target.transform.rotation) > 0.1f
                || Vector3.Distance(source.transform.lossyScale, target.transform.lossyScale) > 0.001f)
                throw new InvalidOperationException("Copy differs in mesh/material/orientation: " + relative);
            Vector3 delta = target.transform.position - source.transform.position;
            if (!probeOnly.Contains(target) && offset.HasValue && Vector3.Distance(offset.Value, delta) > 0.002f)
                throw new InvalidOperationException("Copy is not a uniform translation: " + relative);
            pairs.Add((target, source));
        }
        var probePairs = new List<(ReflectionProbe target, ReflectionProbe source)>();
        foreach (var probe in copy.GetComponentsInChildren<ReflectionProbe>(true))
        {
            var source = All<ReflectionProbe>().Single(p => p.transform.root.name == "Lighting" && p.name == probe.name);
            if (!source.texture || Vector3.Distance(probe.transform.position - source.transform.position, offset.Value) > 0.002f)
                throw new InvalidOperationException("Reflection probe does not match original: " + probe.name);
            probePairs.Add((probe, source));
        }
        Directory.CreateDirectory(Output);
        string backupScene = scene.name == "Demo 1" ? "Assets/CorridorLightingRepair/BeforeLightingRepair.unity" : "Assets/CorridorLightingRepair/BeforeLightingRepair-" + scene.name.Replace(" ", "-") + ".unity";
        if (File.Exists(backupScene)) throw new InvalidOperationException("Backup already exists; refusing to overwrite it.");
        File.Copy(scene.path, Output + "/OriginalOnDisk.unity", false);
        if (!EditorSceneManager.SaveScene(scene, backupScene, true)) throw new IOException("Cannot back up unsaved scene.");
        File.Copy(backupScene, Output + "/BeforeLightingRepair.unity", false);
        Inspect();
        File.Copy(Output + "/Inspection.json", Output + "/Before.json", false);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Match corridor baked lighting");
        var reuse = Undo.AddComponent<CorridorBakedLightingReuse>(copy);
        var bindings = new List<CorridorBakedLightingReuse.Binding>();
        int anchors = 0;
        foreach (var pair in pairs)
        {
            if (!probeOnly.Contains(pair.target) && pair.source.lightmapIndex >= 0 && pair.source.lightmapIndex < LightmapSettings.lightmaps.Length)
                bindings.Add(new CorridorBakedLightingReuse.Binding { target = pair.target,
                    lightmap = LightmapSettings.lightmaps[pair.source.lightmapIndex].lightmapColor,
                    scaleOffset = pair.source.lightmapScaleOffset });
            else
            {
                // Hinges/decals without baked UV lighting sample the same authored
                // light probe location as their original counterparts.
                Undo.RecordObject(pair.target, "Match corridor probe sample");
                if (probeOnly.Contains(pair.target)) pair.target.receiveGI = ReceiveGI.LightProbes;
                pair.target.probeAnchor = pair.source.probeAnchor ? pair.source.probeAnchor : pair.source.transform;
                anchors++;
            }
        }
        reuse.bindings = bindings.ToArray();
        BindLights(reuse, original, copy);
        EditorUtility.SetDirty(reuse);
        reuse.Apply();
        foreach (var pair in probePairs)
        {
            Undo.RecordObject(pair.target, "Reuse corridor reflection");
            pair.target.customBakedTexture = pair.source.texture;
            pair.target.mode = ReflectionProbeMode.Custom;
            EditorUtility.SetDirty(pair.target);
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save repaired scene.");
        Inspect();
        File.Copy(Output + "/Inspection.json", Output + "/After.json", true);
        File.WriteAllText(Output + "/Changes.txt", $"Reused original lighting for {bindings.Count} static renderers; matched {anchors} unlightmapped probe anchors ({probeOnly.Count} independently moved decorations use LightProbes GI); assigned {probePairs.Count} original reflection cubemaps; matched {reuse.lights.Length} copied lamps' bake output. Translation: {offset}. No light settings, ambient/URP settings, transforms, geometry, materials or gameplay components changed. Added one rendering-only lightmap persistence component. Backup: {backupScene}\nProbe-only decorations: " + string.Join("; ", probeOnly.Select(r => PathOf(r.transform))));
        SceneView.RepaintAll();
        Debug.Log(File.ReadAllText(Output + "/Changes.txt"));
    }
    static void BindLights(CorridorBakedLightingReuse reuse, GameObject original, GameObject copy)
    {
        var sources = original.GetComponentsInChildren<Light>(true);
        reuse.lights = copy.GetComponentsInChildren<Light>(true).Select(target =>
        {
            string relative = PathOf(target.transform).Substring(copy.name.Length + 1);
            var source = sources.Single(l => PathOf(l.transform).EndsWith("/" + relative, StringComparison.Ordinal));
            if (source.lightmapBakeType != LightmapBakeType.Baked || target.lightmapBakeType != LightmapBakeType.Baked
                || source.intensity != target.intensity || source.range != target.range)
                throw new InvalidOperationException("Copied lamp does not match original: " + relative);
            return new CorridorBakedLightingReuse.LightBinding { target = target, source = source };
        }).ToArray();
    }
    [MenuItem("Tools/Corridor Lighting/Complete Copied Lamp Bake State")]
    public static void CompleteLights()
    {
        var scene = SceneManager.GetActiveScene();
        var original = scene.GetRootGameObjects().Single(r => r.name == "Assets");
        var copy = CopyRoot();
        var reuse = copy.GetComponent<CorridorBakedLightingReuse>();
        if (!reuse) throw new InvalidOperationException("Apply the lighting reuse repair first.");
        Undo.RecordObject(reuse, "Match copied lamp bake output");
        BindLights(reuse, original, copy);
        EditorUtility.SetDirty(reuse);
        reuse.Apply();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Inspect();
        File.Copy(Output + "/Inspection.json", Output + "/After.json", true);
        File.AppendAllText(Output + "/Changes.txt", "\nMatched 3 copied lamps' LightBakingOutput to their original Baked lamps, removing their additional direct-light contribution. Intensity, range, shadows, light type and Baked mode unchanged.");
    }
    [MenuItem("Tools/Corridor Lighting/Inspect Lighting Data")]
    public static void InspectData()
    {
        var so = new SerializedObject(Lightmapping.lightingDataAsset);
        var p = so.GetIterator();
        var lines = new List<string>();
        while (p.Next(true))
        {
            if (p.propertyPath.Contains("Array.data[") && !p.propertyPath.Contains("Array.data[0]")) continue;
            lines.Add(p.propertyPath + " type=" + p.propertyType + " value=" + (p.propertyType == SerializedPropertyType.Integer ? p.longValue.ToString() : p.propertyType == SerializedPropertyType.ObjectReference ? AssetDatabase.GetAssetPath(p.objectReferenceValue) : ""));
        }
        File.WriteAllLines(Output + "/LightingDataProperties.txt", lines);
    }
    [MenuItem("Tools/Corridor Lighting/Inspect")]
    public static void Inspect()
    {
        Directory.CreateDirectory(Output);
        var s = new Snapshot {
            scene = SceneManager.GetActiveScene().path,
            pipeline = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.GetType().FullName : "Built-in",
            lightingData = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset),
            lightingSettings = AssetDatabase.GetAssetPath(Lightmapping.lightingSettings),
            ambientMode = RenderSettings.ambientMode.ToString(), ambientIntensity = RenderSettings.ambientIntensity,
            ambientColor = RenderSettings.ambientLight, reflectionIntensity = RenderSettings.reflectionIntensity,
            lightmaps = LightmapSettings.lightmaps.Length,
            lightProbeCount = LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : 0
        };
        foreach (var r in All<MeshRenderer>()) s.renderers.Add(new RendererRow {
            path = PathOf(r.transform), id = r.GetInstanceID(),
            mesh = r.GetComponent<MeshFilter>() ? AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh) : "",
            materials = string.Join(";", r.sharedMaterials.Select(AssetDatabase.GetAssetPath)),
            lightmapIndex = r.lightmapIndex, realtimeLightmapIndex = r.realtimeLightmapIndex,
            lightmapST = r.lightmapScaleOffset, position = r.transform.position,
            rotation = r.transform.eulerAngles, scale = r.transform.lossyScale,
            meshName = r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh ? r.GetComponent<MeshFilter>().sharedMesh.name : "",
            receiveGI = r.receiveGI.ToString(), probeUsage = r.lightProbeUsage.ToString(), reflectionUsage = r.reflectionProbeUsage.ToString(),
            staticGI = GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.ContributeGI)
        });
        foreach (var l in All<Light>()) s.lights.Add(new LightRow { path = PathOf(l.transform), type = l.type.ToString(), bakeType = l.lightmapBakeType.ToString(), shadows = l.shadows.ToString(), position = l.transform.position, intensity = l.intensity, range = l.range, indirect = l.bounceIntensity, enabled = l.enabled && l.gameObject.activeInHierarchy, cullingMask = l.cullingMask, isBaked = l.bakingOutput.isBaked, mixedMode = l.bakingOutput.mixedLightingMode.ToString() });
        foreach (var p in All<ReflectionProbe>()) s.probes.Add(new ProbeRow { path = PathOf(p.transform), mode = p.mode.ToString(), texture = AssetDatabase.GetAssetPath(p.texture), position = p.transform.position, center = p.center, size = p.size, intensity = p.intensity, blendDistance = p.blendDistance, importance = p.importance, boxProjection = p.boxProjection });
        File.WriteAllText(Output + "/Inspection.json", JsonUtility.ToJson(s, true));
        Debug.Log($"Corridor lighting inspection: {s.renderers.Count} renderers, {s.lights.Count} lights, {s.probes.Count} reflection probes, {s.lightmaps} lightmaps. See {Output}/Inspection.json");
    }
}
