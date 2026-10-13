using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Scoped build validation only; does not save scenes or change project settings.
public static class CompilationRepairValidation
{
    const string Folder = "IntegrationBackups/CompilationRepair";
    [MenuItem("Tools/Don't Look Back/Compilation Repair/Inspect Runtime Light Modes")]
    public static void Inspect()
    {
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder();
        report.AppendLine("Unity=" + Application.unityVersion);
        report.AppendLine("URP=" + UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UniversalRenderPipelineAsset).Assembly)?.version);
        report.AppendLine("Editor=" + EditorApplication.applicationPath);
        report.AppendLine("Active build target=" + EditorUserBuildSettings.activeBuildTarget);
        report.AppendLine("Pipeline=" + AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline));
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine(light.transform.root.name + "/" + light.name + " authored=" + light.lightmapBakeType
                + " runtime=" + light.bakingOutput.lightmapBakeType + " isBaked=" + light.bakingOutput.isBaked);
        // Validate all three enum values without altering any scene light.
        var temporary = new GameObject("Temporary light API check") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var light = temporary.AddComponent<Light>();
            foreach (LightmapBakeType mode in new[] { LightmapBakeType.Baked, LightmapBakeType.Mixed, LightmapBakeType.Realtime })
            {
                light.lightmapBakeType = mode;
                light.bakingOutput = new LightBakingOutput { lightmapBakeType = mode, isBaked = mode != LightmapBakeType.Realtime };
                if (LightBakeTypeUtility.GetBakeType(light) != mode || light.bakingOutput.lightmapBakeType != mode)
                    throw new InvalidOperationException("Light mode mismatch: " + mode);
                report.AppendLine("PASS authored/runtime bake mode " + mode);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(temporary); }
        File.WriteAllText(Folder + "/LightModeValidation.txt", report.ToString());
        Debug.Log("Light API validation passed.");
    }
    [MenuItem("Tools/Don't Look Back/Compilation Repair/Build Windows Player")]
    public static void QueueBuild()
    {
        if (Application.isPlaying || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Finish Play/compilation/build before validation.");
        EditorApplication.delayCall -= Build;
        Build();
    }
    static void Build()
    {
        Directory.CreateDirectory(Folder);
        string output = Path.GetFullPath("Builds/CompilationRepair/DontLookBack.exe");
        File.WriteAllText(Folder + "/BuildStatus.txt", "Building Windows x64 Player\nOutput=" + output);
        try
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Windows x64 build support is not installed.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            var text = new StringBuilder();
            text.AppendLine("Result=" + report.summary.result);
            text.AppendLine("Errors=" + report.summary.totalErrors + " Warnings=" + report.summary.totalWarnings);
            text.AppendLine("Duration=" + report.summary.totalTime + " Bytes=" + report.summary.totalSize);
            text.AppendLine("Output=" + report.summary.outputPath);
            foreach (var step in report.steps)
            foreach (var message in step.messages)
                if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning)
                    text.AppendLine(message.type + ": " + message.content);
            File.WriteAllText(Folder + "/BuildStatus.txt", text.ToString());
            Debug.Log("Compilation repair Player build: " + report.summary.result);
        }
        catch (Exception error)
        {
            File.WriteAllText(Folder + "/BuildStatus.txt", "Build exception: " + error);
            Debug.LogException(error);
        }
    }
}
