using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AlternatingDoorSetup
{
    public const string Folder="IntegrationBackups/AlternatingDoors";
    [MenuItem("Tools/Don't Look Back/Alternating Doors/Audit Saved Setup")]
    public static void Audit()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Audit in Edit mode.");
        var manager=UnityEngine.Object.FindObjectsByType<CorridorManager>(FindObjectsSortMode.None).Single();
        var loop=manager.loop;loop.Initialize(false);Physics.SyncTransforms();var groups=manager.blackDoorDestinations.groups;
        var report=new StringBuilder();
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);report.AppendLine("PASS "+message);}
        Check(loop.corridors.Length==2 && groups.Length==4 && groups.Select(g=>g.instance).Distinct().Count()==4,"Four distinct registry instances; two main loop references");
        foreach(var group in groups)
        {
            var env=group.instance;var portals=env.GetComponentsInChildren<BlackDoorTransition>();
            int expected=env.kind==CorridorDestination.EnvironmentKind.MainLookBack?40:14;
            Check(portals.Length==expected && env.doorExits.Length==expected,"Complete exit list "+env.name+": "+expected);
            Check(env.doorExits.All(e=>e.IsValid(loop) && e.portal.manager==manager),"Safe exits and shared owner: "+env.name);
            Check(portals.All(p=>env.doorExits.Count(e=>e.portal==p)==1),"Exactly one exit per active door: "+env.name);
            Check(env.audioProfile,"Original audio profile retained: "+env.name+" / "+env.audioProfile.name);
            if(env.kind==CorridorDestination.EnvironmentKind.MainLookBack)Check(loop.corridors.Any(c=>c.environment==env.transform),"Main arrival maps to existing main loop");
            else Check(group.prefab && !loop.corridors.Any(c=>c.environment==env.transform),"Prefab fallback retained and excluded from main loop");
        }
        Check(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c=>c.isActiveAndEnabled)==1 && UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(c=>c.isActiveAndEnabled)==1,"One gameplay camera and listener");
        Check(UnityEngine.Object.FindObjectsByType<FirstPersonMovement>(FindObjectsSortMode.None).Count(c=>c.isActiveAndEnabled)==1,"One player controller");
        File.WriteAllText(Folder+"/SceneAudit.txt",report.ToString());Debug.Log("Alternating door saved-scene audit passed.");
    }
    public static bool IsConfigured => UnityEngine.Object.FindFirstObjectByType<BlackDoorTransitionManager>()?.groups.Any(g=>g!=null && g.instance && g.instance.kind==CorridorDestination.EnvironmentKind.MainLookBack) == true;
    [InitializeOnLoadMethod] static void Listen(){EditorApplication.playModeStateChanged-=OnPlay;EditorApplication.playModeStateChanged+=OnPlay;}
    static void OnPlay(PlayModeStateChange state)
    {
        if(!SessionState.GetBool("AlternatingDoorChecks",false))return;
        if(state==PlayModeStateChange.EnteredEditMode)EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;
        if(state==PlayModeStateChange.EnteredPlayMode){SessionState.SetBool("AlternatingDoorChecks",false);EditorApplication.delayCall+=Checks;}
    }
    [MenuItem("Tools/Don't Look Back/Alternating Doors/Run Fresh Play Checks")]
    public static void Fresh(){SessionState.SetBool("AlternatingDoorChecks",true);EditorApplication.isPlaying=!EditorApplication.isPlaying;}
    public static void Checks()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter fresh Play mode.");
        if(UnityEngine.Object.FindFirstObjectByType<AlternatingDoorVerifier>())throw new InvalidOperationException("Checks already running");
        var test=new GameObject("Temporary alternating door verifier").AddComponent<AlternatingDoorVerifier>();test.StartCoroutine(test.Run());
    }
    [MenuItem("Tools/Don't Look Back/Alternating Doors/Configure Main Exits And Registry")]
    public static void Configure()
    {
        Inspect();var manager=UnityEngine.Object.FindFirstObjectByType<CorridorManager>();var loop=manager.loop;
        loop.Initialize(false);Physics.SyncTransforms();
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Configure alternating black door exits");
        var report=new StringBuilder();
        var selector=manager.blackDoorDestinations;
        var groups=selector.groups.Where(g=>g!=null && (!g.instance || g.instance.kind==CorridorDestination.EnvironmentKind.DoorPrefab)).ToList();
        foreach(var entry in loop.corridors)
        {
            var environment=entry.environment.GetComponent<CorridorDestination>();
            if(!environment || environment.kind!=CorridorDestination.EnvironmentKind.MainLookBack)throw new InvalidOperationException("Invalid main reference");
            Undo.RecordObject(environment,"Configure main door exit list");
            var exits=new System.Collections.Generic.List<DoorExitPoint>();
            foreach(var portal in environment.GetComponentsInChildren<BlackDoorTransition>())
            {
                var exit=portal.entranceFrame.GetComponentInChildren<DoorExitPoint>(true);
                if(!exit)
                {
                    var go=new GameObject("DoorExitPoint - "+portal.door.transform.parent.parent.name);
                    Undo.RegisterCreatedObjectUndo(go,"Add main corridor door exit");go.transform.SetParent(portal.entranceFrame,false);exit=go.AddComponent<DoorExitPoint>();
                }
                Undo.RecordObject(exit,"Wire main door exit");Undo.RecordObject(exit.transform,"Place corridor-side arrival");
                exit.environment=environment;exit.portal=portal;exit.weight=1;
                bool valid=false;
                for(float distance=.65f;distance<=1.21f;distance+=.05f)
                {
                    exit.transform.SetPositionAndRotation(portal.entranceFrame.position-portal.entranceFrame.forward*distance+Vector3.up*.01f,Quaternion.LookRotation(-portal.entranceFrame.forward,Vector3.up));
                    Physics.SyncTransforms();if(exit.IsValid(loop) && loop.IsClear(exit.transform.position+exit.transform.forward*.3f,exit.transform.rotation)){valid=true;break;}
                }
                if(!valid){exit.weight=0;report.AppendLine("EXCLUDED "+CorridorRevisionSetup.PathOf(portal.door.transform));}
                else{exits.Add(exit);report.AppendLine("EXIT "+CorridorRevisionSetup.PathOf(exit.transform)+" at "+exit.transform.position);}
                EditorUtility.SetDirty(exit);
            }
            if(exits.Count==0)throw new InvalidOperationException("No safe main exits: "+environment.name);
            environment.doorExits=exits.ToArray();EditorUtility.SetDirty(environment);
            var old=selector.groups.FirstOrDefault(g=>g!=null && g.instance==environment);
            groups.Add(old??new BlackDoorTransitionManager.Group{label=environment.name,instance=environment,weight=1});
            report.AppendLine("MAIN "+environment.name+" exits="+exits.Count);
        }
        Undo.RecordObject(selector,"Register both corridor categories");selector.groups=groups.ToArray();EditorUtility.SetDirty(selector);
        Undo.CollapseUndoOperations(undo);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText(Folder+"/Configured.txt",report.ToString());Debug.Log("Alternating door registry configured.");
    }
    [MenuItem("Tools/Don't Look Back/Alternating Doors/Inspect And Back Up")]
    public static void Inspect()
    {
        if(Application.isPlaying || SceneManager.GetActiveScene().name!="DontLookBack") throw new InvalidOperationException("Open DontLookBack in Edit mode.");
        Directory.CreateDirectory(Folder);
        string backup="Assets/DontLookBack/Backups/BeforeAlternatingDoors.unity";
        if(!File.Exists(backup) && !EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),backup,true)) throw new IOException("Backup failed");
        var manager=UnityEngine.Object.FindFirstObjectByType<CorridorManager>();
        var report=new StringBuilder();
        foreach(var c in manager.loop.corridors) report.AppendLine("MAIN reference "+c.environment.name+" start="+c.startPoint.position);
        foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var c=root.GetComponent<CorridorDestination>();if(!c)continue;
            report.AppendLine("ENV "+c.name+" category="+c.kind+" pos="+c.transform.position+" profile="+c.audioProfile+" exits="+c.doorExits.Length+" active doors="+c.GetComponentsInChildren<BlackDoorTransition>().Length+" prefab="+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root));
        }
        foreach(var g in manager.blackDoorDestinations.groups)report.AppendLine("REGISTRY "+g.label+" instance="+g.instance+" asset="+AssetDatabase.GetAssetPath(g.prefab)+" weight="+g.weight);
        File.WriteAllText(Folder+"/Inspection.txt",report.ToString());Debug.Log(report.ToString());
    }
}
