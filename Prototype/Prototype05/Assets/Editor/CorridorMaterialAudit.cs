using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Read-only verification: never changes materials, prefabs, scenes or pipeline settings.
public static class CorridorMaterialAudit
{
    const string Root = "Assets/AK Studio Art/Modular Horror Corridor";
    [MenuItem("Tools/Corridor Materials/Verify URP Repair")]
    public static void Verify()
    {
        var report = new StringBuilder();
        int failures = 0, materials = 0, prefabRenderers = 0, sceneRenderers = 0;
        var pipeline = GraphicsSettings.currentRenderPipeline;
        report.AppendLine("Active render pipeline: " + (pipeline ? pipeline.GetType().FullName : "Built-in"));
        report.AppendLine("Quality: " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
        if (!pipeline || !pipeline.GetType().FullName.Contains("UniversalRenderPipelineAsset")) failures++;
        var corridorMaterials = new HashSet<Material>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] {Root + "/Materials"}))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            corridorMaterials.Add(mat);
            materials++;
            bool valid = mat && mat.shader && mat.shader.name == "Universal Render Pipeline/Lit"
                && mat.shader.isSupported && !ShaderUtil.ShaderHasError(mat.shader);
            if (!valid) failures++;
            report.AppendLine((valid ? "OK " : "FAIL ") + path + " shader=" + (mat && mat.shader ? mat.shader.name : "missing"));
            if (!mat) continue;
            foreach (string property in mat.GetTexturePropertyNames())
            {
                var texture = mat.GetTexture(property);
                if (!texture) continue;
                string texturePath = AssetDatabase.GetAssetPath(texture);
                report.AppendLine("  " + property + " = " + texturePath);
                if (property == "_BumpMap" && AssetImporter.GetAtPath(texturePath) is TextureImporter importer
                    && importer.textureType != TextureImporterType.NormalMap)
                { failures++; report.AppendLine("FAIL normal map importer: " + texturePath); }
            }
            // Nonzero serialized texture references must resolve, even when not exposed by Lit.
            var serialized = new SerializedObject(mat);
            var envs = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = 0; envs != null && i < envs.arraySize; i++)
            {
                var slot = envs.GetArrayElementAtIndex(i).FindPropertyRelative("second.m_Texture");
                if (slot != null && slot.objectReferenceValue == null && slot.objectReferenceInstanceIDValue != 0)
                { failures++; report.AppendLine("FAIL unresolved texture in " + path); }
            }
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] {Root + "/Prefabs"}))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            { prefabRenderers++; CheckRenderer(renderer, path, report, ref failures); }
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bool relevant = scene.path.StartsWith(Root, StringComparison.Ordinal);
                foreach (var material in renderer.sharedMaterials) relevant |= corridorMaterials.Contains(material);
                if (!relevant) continue;
                sceneRenderers++; CheckRenderer(renderer, scene.path, report, ref failures);
            }
        }
        report.AppendLine($"SUMMARY: {materials} materials, {prefabRenderers} prefab renderers, {sceneRenderers} loaded-scene renderers, {failures} failures.");
        Directory.CreateDirectory("MaterialBackups/Corridor-Before-URP");
        File.WriteAllText("MaterialBackups/Corridor-Before-URP/UnityVerification.txt", report.ToString());
        SceneView.RepaintAll();
        if (failures == 0) Debug.Log(report.ToString()); else Debug.LogError(report.ToString());
    }
    static void CheckRenderer(Renderer renderer, string context, StringBuilder report, ref int failures)
    {
        foreach (var material in renderer.sharedMaterials)
            if (!material || !material.shader || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader)
                || material.shader.name == "Hidden/InternalErrorShader"
                || (AssetDatabase.GetAssetPath(material).StartsWith(Root, StringComparison.Ordinal)
                    && material.shader.name != "Universal Render Pipeline/Lit"))
            { failures++; report.AppendLine("FAIL renderer " + context + "/" + renderer.name); }
    }
    [MenuItem("Tools/Corridor Materials/Capture Current Scene View")]
    public static void Capture()
    {
        var view = SceneView.lastActiveSceneView;
        if (!view || !view.camera) throw new InvalidOperationException("Open a Scene view first.");
        var camera = view.camera;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24);
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes("MaterialBackups/Corridor-Before-URP/SceneViewAfter.png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
