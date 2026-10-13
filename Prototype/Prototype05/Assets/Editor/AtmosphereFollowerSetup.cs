using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class AtmosphereFollowerSetup
{
    public const string Folder = "IntegrationBackups/AtmosphereFollower";
    [MenuItem("Tools/Don't Look Back/Atmosphere and Follower/Inspect And Back Up")]
    public static void Inspect()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().name != "DontLookBack") throw new InvalidOperationException("Open DontLookBack in Edit mode.");
        Directory.CreateDirectory(Folder);
        const string backup = "Assets/DontLookBack/Backups/BeforeAtmosphereFollower.unity";
        if (!File.Exists(backup) && !EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), backup, true)) throw new IOException("Backup failed.");
        foreach (string file in new[] { "CorridorManager.cs", "CorridorLoopManager.cs", "CorridorDestination.cs" })
            if (!File.Exists(Folder + "/" + file + ".txt")) File.Copy("Assets/DontLookBack/Scripts/" + file, Folder + "/" + file + ".txt");
        var report = new StringBuilder();
        report.AppendLine("Pipeline=" + GraphicsSettings.currentRenderPipeline + " ambient=" + RenderSettings.ambientMode + " intensity=" + RenderSettings.ambientIntensity);
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) report.AppendLine("Light " + light.name + " mode=" + light.lightmapBakeType + " intensity=" + light.intensity);
        foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) report.AppendLine("Camera " + camera.name + " PP=" + camera.GetUniversalAdditionalCameraData().renderPostProcessing);
        foreach (var volume in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) report.AppendLine("Volume " + volume.name + " priority=" + volume.priority);
        foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) report.AppendLine("Audio " + source.name + " clip=" + source.clip + " loop=" + source.loop + " spatial=" + source.spatialBlend);
        foreach (var playerAudio in UnityEngine.Object.FindObjectsByType<FirstPersonAudio>(FindObjectsSortMode.None)) report.AppendLine("Player audio: " + playerAudio.name);
        File.WriteAllText(Folder + "/Inspection.txt", report.ToString()); Debug.Log(report.ToString());
    }
    [MenuItem("Tools/Don't Look Back/Atmosphere and Follower/Set Up Systems")]
    public static void Setup()
    {
        if (UnityEngine.Object.FindFirstObjectByType<BlackDoorTransitionManager>()) { CorridorRevisionSetup.Configure(); return; }
        Inspect();
        var manager = UnityEngine.Object.FindObjectsByType<CorridorManager>(FindObjectsSortMode.None).Single();
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) throw new InvalidOperationException("Expected URP.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Add atmosphere and invisible follower");
        var atmosphere = manager.GetComponent<AtmosphereManager>();
        if (!atmosphere) atmosphere = Undo.AddComponent<AtmosphereManager>(manager.gameObject);
        Undo.RecordObject(atmosphere, "Wire atmosphere"); atmosphere.corridors = manager; atmosphere.gameplayCamera = manager.gameplayCamera;
        atmosphere.atmosphereShader = Shader.Find("DontLookBack/Atmosphere Multiply");
        if (!atmosphere.atmosphereShader) throw new InvalidOperationException("Atmosphere shader has not imported.");
        var previousVolume = manager.transform.Find("Progressive Atmosphere Volume");
        if (previousVolume) Undo.DestroyObjectImmediate(previousVolume.gameObject);
        var follower = manager.GetComponent<FollowerAudioManager>();
        if (!follower) follower = Undo.AddComponent<FollowerAudioManager>(manager.gameObject);
        Undo.RecordObject(follower, "Wire follower audio"); follower.loop = manager.loop; follower.doors = manager;
        follower.groundCheck = manager.loop.player.GetComponentInChildren<GroundCheck>();
        follower.playerFootsteps = manager.loop.player.GetComponentInChildren<FirstPersonAudio>();
        if (!follower.footstepSource)
        {
            var go = new GameObject("Invisible Follower Footsteps"); Undo.RegisterCreatedObjectUndo(go, "Add follower audio voice");
            go.transform.SetParent(manager.transform, false);
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = false; source.spatialBlend = 1;
            source.dopplerLevel = 0; source.minDistance = 1.5f; source.maxDistance = 12; source.volume = .45f;
            source.rolloffMode = AudioRolloffMode.Logarithmic; follower.footstepSource = source;
        }
        Directory.CreateDirectory("Assets/DontLookBack/Audio");
        CorridorAudioProfile Profile(string name, string audioFolder, CorridorAudioProfile.Rhythm rhythm)
        {
            string path = "Assets/DontLookBack/Audio/" + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<CorridorAudioProfile>(path);
            if (profile) return profile;
            profile = ScriptableObject.CreateInstance<CorridorAudioProfile>(); profile.name = name; profile.rhythm = rhythm;
            profile.clips = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
                .Take(6).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
            if (profile.clips.Length == 0) throw new InvalidOperationException("Missing footstep assets: " + audioFolder);
            if (rhythm == CorridorAudioProfile.Rhythm.Mismatch)
            { profile.strideMeters = 1.15f; profile.echoDelay = .12f; profile.timingJitter = .12f; profile.pitchRange = new Vector2(.76f,.9f); profile.distanceBehind = 3.5f; }
            AssetDatabase.CreateAsset(profile, path); return profile;
        }
        var echo = Profile("Corridor A - Echo", "Assets/Footsteps - Essentials/Footsteps_Tile/Footsteps_Tile_Walk", CorridorAudioProfile.Rhythm.Echo);
        var mismatch = Profile("Corridor B - Mismatch", "Assets/Footsteps - Essentials/Footsteps_Wood/Footsteps_Wood_Walk", CorridorAudioProfile.Rhythm.Mismatch);
        foreach (var entry in manager.destinations.Where(d => d != null && d.instance))
        {
            Undo.RecordObject(entry.instance, "Assign corridor follower profile");
            if (!entry.instance.audioProfile) entry.instance.audioProfile = entry.instance.name == "Corridor_B" ? mismatch : echo;
            PrefabUtility.RecordPrefabInstancePropertyModifications(entry.instance); EditorUtility.SetDirty(entry.instance);
        }
        follower.fallbackProfile = echo;
        EditorUtility.SetDirty(atmosphere); EditorUtility.SetDirty(follower);
        Undo.CollapseUndoOperations(group); AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("Atmosphere and follower configured with real tile/wood clips; scene saved.");
    }
    [MenuItem("Tools/Don't Look Back/Atmosphere and Follower/Run Play Checks")]
    public static void Checks()
    {
        if (UnityEngine.Object.FindFirstObjectByType<BlackDoorTransitionManager>()) { CorridorRevisionSetup.CheckPlay(); return; }
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<AtmosphereFollowerPlayVerifier>()) throw new InvalidOperationException("Checks already running.");
        var test = new GameObject("Temporary atmosphere follower verifier").AddComponent<AtmosphereFollowerPlayVerifier>(); test.StartCoroutine(test.Run());
    }
}
