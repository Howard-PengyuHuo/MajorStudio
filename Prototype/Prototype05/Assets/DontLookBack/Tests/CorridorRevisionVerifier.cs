using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class CorridorRevisionVerifier : MonoBehaviour
{
    const string Folder="IntegrationBackups/CorridorRevision";
    readonly StringBuilder report=new StringBuilder();
    Color lastCenter;
    void Check(bool value,string text) { report.AppendLine((value?"PASS ":"FAIL ")+text); File.WriteAllText(Folder+"/PlayChecks.txt",report.ToString()); if(!value) throw new InvalidOperationException(text); }
    void Move(CorridorLoopManager loop,Vector3 p,Quaternion q)
    { loop.player.SetPositionAndRotation(p,q); var b=loop.player.GetComponent<Rigidbody>(); b.position=p;b.rotation=q; loop.view.localRotation=Quaternion.identity;loop.mouseLook.SynchronizeAfterTeleport();Physics.SyncTransforms(); }
    public IEnumerator Run()
    {
        var manager=FindFirstObjectByType<CorridorManager>();var loop=manager.loop;var selector=manager.blackDoorDestinations;
        var atmosphere=manager.GetComponent<AtmosphereManager>();var follower=manager.GetComponent<FollowerAudioManager>();
        var body=loop.player.GetComponent<Rigidbody>();var mover=loop.player.GetComponent<FirstPersonMovement>();
        bool movement=mover.enabled,look=loop.mouseLook.enabled,kinematic=body.isKinematic,background=Application.runInBackground,followerEnabled=follower.enabled;
        var groups=selector.groups;var weights=groups.Select(g=>g.weight).ToArray();var random=UnityEngine.Random.state;
        GameObject spawned=null;
        var maps=LightmapSettings.lightmaps.Select(m=>m.lightmapColor).ToArray();
        var lampValues=FindObjectsByType<Light>(FindObjectsSortMode.None).ToDictionary(l=>l,l=>l.intensity);
        try
        {
            Application.runInBackground=true;mover.enabled=loop.mouseLook.enabled=false;body.isKinematic=true;loop.Initialize(true);
            Check(loop.corridors.Length==2 && groups.Length==2,"Exactly two main references and two separate prefab groups");
            Check(groups.All(g=>!loop.corridors.Any(c=>c.environment==g.instance.transform)),"Prefab groups are distinct from main loop objects");
            Check(atmosphere.CompletedDoorTransitions==0,"Fresh session is reference brightness");
            void Turn()
            {var f=loop.corridors[loop.CurrentCorridor].startPoint.forward;loop.SampleLook(f,1);loop.SampleLook(f,.2f);loop.SampleLook(-f,.2f);loop.SampleLook(f,.2f);}
            for(int i=0;i<2;i++)
            {
                loop.EnterFromDoor(i,loop.corridors[i].startPoint);var frame=loop.corridors[i].startPoint;
                Move(loop,frame.TransformPoint(new Vector3(0,0,5)),frame.rotation);
                Turn();Check(loop.CurrentCorridor==i && loop.LookBackCount==1 && Vector3.Distance(loop.player.position,frame.position)<.02f,"Main "+i+" first look-back returns to its start");
                Move(loop,frame.TransformPoint(new Vector3(0,0,7)),frame.rotation);
                Turn();var next=loop.corridors[1-i].startPoint;
                Check(loop.CurrentCorridor==1-i && loop.LookBackCount==0 && Vector3.Distance(loop.player.position,next.TransformPoint(new Vector3(0,0,7)))<.02f,"Main "+i+" second look-back preserves local position in other MAIN corridor");
                Check(Vector3.Angle(loop.player.forward,next.forward)<.1f,"Main transfer preserves local forward orientation "+i);
            }
            Check(atmosphere.CompletedDoorTransitions==0,"Look-back teleports never advance darkness");
            foreach(var portal in groups.SelectMany(g=>g.instance.GetComponentsInChildren<BlackDoorTransition>()))
            {
                if(!portal.door.CanInteract) continue;
                Move(loop,portal.entranceFrame.position-portal.entranceFrame.forward*.9f+Vector3.up*.01f,portal.entranceFrame.rotation);
                Check(manager.FindTargetDoor()==portal.door,"Shared center-camera interaction detects prefab door "+portal.door.transform.parent.parent.name+" / "+portal.transform.root.name);
            }
            foreach(var group in groups)
            {
                Check(group.instance.doorExits.Length==14,"Fourteen explicitly configured door exits: "+group.label);
                foreach(var exit in group.instance.doorExits) Check(exit.IsValid(loop) && exit.transform!=group.instance.spawnPoint,"Valid grounded corridor-side door exit "+exit.name+" / "+group.label);
            }
            UnityEngine.Random.InitState(20261013);groups[0].weight=1;groups[1].weight=3;
            int[] hits=new int[2];var doorsSeen=new System.Collections.Generic.HashSet<DoorExitPoint>();
            for(int i=0;i<120;i++) {Check(selector.TrySelect(out var e),"Weighted selection valid "+i); hits[e.environment==groups[0].instance?0:1]++;doorsSeen.Add(e);}
            Check(hits[1]>hits[0] && hits[0]>8 && doorsSeen.Count>14,"Two-stage weighted group/door randomness reaches both groups and many doors ("+hits[0]+","+hits[1]+")");
            groups[0].weight=0;for(int i=0;i<5;i++) {selector.TrySelect(out var e);Check(e.environment==groups[1].instance,"Zero group weight excludes destination");}
            for(int i=0;i<2;i++)groups[i].weight=weights[i];
            // Exercise an asset-only entry and ensure all exits become world-space clone references.
            var assetOnly=new BlackDoorTransitionManager.Group {label="Temporary asset-only",prefab=groups[0].prefab,weight=1};
            selector.groups=new[]{assetOnly};Check(selector.TrySelect(out var runtimeExit),"Asset-only prefab instantiates a valid world-space door exit");spawned=assetOnly.instance.gameObject;
            Check(runtimeExit.gameObject.scene.IsValid() && runtimeExit.transform.IsChildOf(spawned.transform),"Selected exit belongs to the instantiated environment, not the asset");
            Check(selector.TrySelect(out _) && assetOnly.instance.gameObject==spawned,"Instantiated prefab reused");
            selector.groups=groups;Destroy(spawned);spawned=null;yield return null;
            follower.enabled=false;
            var environments=new[]{loop.corridors[0].environment.GetComponent<CorridorDestination>(),loop.corridors[1].environment.GetComponent<CorridorDestination>(),groups[0].instance,groups[1].instance};
            var baseline=new float[4];
            for(int i=0;i<4;i++) {Move(loop,environments[i].spawnPoint.position+environments[i].spawnPoint.forward*1.5f,environments[i].spawnPoint.rotation);baseline[i]=Capture(manager.gameplayCamera,"Stage0-"+i);}
            Check(environments.Select(e=>e.audioProfile).Distinct().Count()==4,"All four environments have independent assigned audio profiles");
            loop.Initialize(true);
            for(int crossing=0;crossing<4;crossing++)
            {
                // Force group A for two consecutive traversals to verify revisits still progress.
                groups[0].weight=crossing==0?0:1;groups[1].weight=crossing==0?1:0;
                var env=loop.CurrentEnvironment;
                var portal=env.GetComponentsInChildren<BlackDoorTransition>().Where(p=>Mathf.Abs(Vector3.Dot(p.entranceFrame.forward,env.spawnPoint.forward))<.5f).First();
                Move(loop,portal.entranceFrame.position-portal.entranceFrame.forward*.9f+Vector3.up*.01f,portal.entranceFrame.rotation);
                if(!portal.door.IsOpen) portal.door.TryOpen();
                yield return new WaitForSecondsRealtime(portal.door.openDuration+manager.arrivalCooldownSeconds+.1f);
                Check(manager.CompletedTransitions==crossing && atmosphere.CompletedDoorTransitions==crossing,"Opening without crossing does not transition or darken "+crossing);
                var flashlight=new GameObject("Temporary bright portal check").AddComponent<Light>();flashlight.type=LightType.Point;flashlight.range=10;flashlight.intensity=10000;flashlight.transform.position=manager.gameplayCamera.transform.position;
                Capture(manager.gameplayCamera,"BlackDoor-"+crossing);Destroy(flashlight.gameObject);
                Check(lastCenter.maxColorComponent<=1f/255,"Doorway remains opaque pure black under bright light at stage "+atmosphere.StageIndex);
                Move(loop,portal.entranceFrame.position-portal.entranceFrame.forward*.32f+Vector3.up*.01f,portal.entranceFrame.rotation);
                yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                Check(portal.IsEntranceArmed,"Intended entrance side arms "+crossing);
                Move(loop,portal.entranceFrame.position+portal.entranceFrame.forward*.1f+Vector3.up*.01f,portal.entranceFrame.rotation);
                yield return new WaitForFixedUpdate();yield return null;
                Check(manager.IsBusy && !manager.TryBeginTransition(portal),"Exactly one transition begins "+crossing);
                float deadline=Time.unscaledTime+6;while(manager.IsBusy && Time.unscaledTime<deadline) yield return null;
                var exit=selector.LastSelectedExit;
                Check(!manager.IsBusy && manager.CompletedTransitions==crossing+1 && atmosphere.CompletedDoorTransitions==crossing+1,"Successful door commit advances darkness, including same-group revisits "+crossing);
                Check(loop.CurrentCorridor==-1 && loop.CurrentEnvironment==exit.environment && loop.corridors.Length==2,"Prefab arrival disables main look-back without expanding its registry");
                Check(Vector3.Distance(loop.player.position,exit.transform.position)<.03f && Vector3.Angle(loop.view.forward,exit.transform.forward)<.1f && exit.portal.door.IsOpen,"Player emerges at selected OPEN door, facing corridor");
                var saved=loop.player.position;for(int n=0;n<3;n++){loop.SampleLook(-exit.transform.forward,1);loop.SampleLook(exit.transform.forward,1);}
                Check(loop.player.position==saved && loop.LookBackCount==0,"Prefab look-backs remain disabled");
                yield return new WaitForSecondsRealtime(manager.arrivalCooldownSeconds+.1f);
                Check(manager.CompletedTransitions==crossing+1,"Arrival cannot immediately retrigger");
                atmosphere.Apply(20);
                Check(atmosphere.StageIndex==Mathf.Min(3,crossing+1),"Darkness clamps at final stage");
                if(crossing<3) for(int i=0;i<4;i++)
                {
                    Move(loop,environments[i].spawnPoint.position+environments[i].spawnPoint.forward*1.5f,environments[i].spawnPoint.rotation);
                    float brightness=Capture(manager.gameplayCamera,"Stage"+(crossing+1)+"-"+i);float ratio=brightness/baseline[i];
                    float target=new[]{.75f,.55f,.35f}[crossing];
                    Check(Mathf.Abs(ratio-target)<.07f && ratio>.25f,"Rendered stage "+(crossing+1)+" environment "+i+" brightness ratio="+ratio.ToString("F3"));
                }
            }
            Check(lampValues.All(p=>p.Key.intensity==p.Value) && maps.All(m=>LightmapSettings.lightmaps.Any(l=>l.lightmapColor==m)),"Original light values and baked textures preserved");
            // Deterministic motion samples keep tests independent of OS keyboard focus.
            foreach(var env in environments)
            {
                if(env.kind==CorridorDestination.EnvironmentKind.MainLookBack) loop.EnterFromDoor(Array.FindIndex(loop.corridors,c=>c.environment==env.transform),env.spawnPoint);
                else loop.EnterDoorExit(env.doorExits[0]);
                follower.ApplyCorridor(loop.CurrentCorridor);
                Check(follower.CurrentProfile==env.audioProfile,"Correct audio profile: "+env.name);
                Vector3 p=loop.player.position;Vector3 f=loop.player.forward;
                void Tick(bool moving,Vector3 gaze,float dt=.1f) {if(moving)p+=f*(dt*2);Move(loop,p,Quaternion.LookRotation(f));follower.SampleMotion(p,gaze,dt,true,false);}
                int count=follower.PlayedFootsteps;
                for(int i=0;i<19;i++)Tick(true,f);
                Check(follower.PlayedFootsteps==count,"Silent for first 1.9 seconds: "+env.name);
                Tick(true,f);Check(follower.IsFollowing && follower.PlayedFootsteps==count+1,"Follower begins at 2 seconds: "+env.name);
                Check(Vector3.Dot(follower.footstepSource.transform.position-loop.player.position,f)<-2 && follower.footstepSource.spatialBlend==1 && !follower.footstepSource.mute,"Audible source is spatial and behind player: "+env.name);
                Tick(false,f);for(int i=0;i<8;i++)Tick(false,f);
                Check(follower.IsFollowing,"Follower continues during stop grace period");
                Tick(true,f);Check(follower.IsFollowing,"Resuming cancels pending stop without new activation");
                Tick(false,f);for(int i=0;i<11;i++)Tick(false,f);
                Check(!follower.IsFollowing && !follower.footstepSource.isPlaying,"Follower stops after approximately 1 second");
                for(int i=0;i<20;i++)Tick(true,f);Tick(false,-f);
                Check(follower.LookingBack && !follower.IsFollowing && !follower.footstepSource.isPlaying,"Backward look silences follower");
                for(int i=0;i<19;i++)Tick(true,f);
                Check(!follower.IsFollowing,"Forward return requires a NEW movement delay");Tick(true,f);
                Check(follower.IsFollowing,"Follower restarts after new 2-second delay");
                follower.SampleMotion(p+Vector3.right*200,f,.1f,true,false);
                Check(!follower.IsFollowing && follower.MovingSeconds==0 && !follower.footstepSource.isPlaying,"Teleport displacement resets all transient audio");
            }
            Check(FindObjectsByType<FirstPersonMovement>(FindObjectsSortMode.None).Length==1 && FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c=>c.isActiveAndEnabled)==1 && FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a=>a.isActiveAndEnabled)==1,"One player, gameplay camera and AudioListener");
            Debug.Log("CORRIDOR REVISION CHECKS PASSED");
        }
        finally
        {
            selector.groups=groups;for(int i=0;i<groups.Length;i++)groups[i].weight=weights[i];if(spawned)Destroy(spawned);
            follower.enabled=followerEnabled;loop.Initialize(true);body.isKinematic=kinematic;mover.enabled=movement;loop.mouseLook.enabled=look;
            UnityEngine.Random.state=random;Application.runInBackground=background;Destroy(gameObject);
        }
    }
    float Capture(Camera camera,string name)
    {
        var previous=RenderTexture.active;var target=new RenderTexture(800,500,24);var pixels=new Texture2D(800,500,TextureFormat.RGB24,false);
        try {RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,800,500),0,0);pixels.Apply();lastCenter=pixels.GetPixel(400,250);lastCenter.a=0;File.WriteAllBytes(Folder+"/"+name+".png",pixels.EncodeToPNG());return pixels.GetPixels().Average(c=>c.grayscale);}
        finally {RenderTexture.active=previous;Destroy(target);Destroy(pixels);}
    }
}
