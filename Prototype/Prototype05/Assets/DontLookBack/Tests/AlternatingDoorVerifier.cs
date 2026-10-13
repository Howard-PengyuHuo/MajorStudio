using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class AlternatingDoorVerifier : MonoBehaviour
{
    const string Folder="IntegrationBackups/AlternatingDoors";
    readonly StringBuilder report=new StringBuilder();
    void Check(bool valid,string message){report.AppendLine((valid?"PASS ":"FAIL ")+message);File.WriteAllText(Folder+"/PlayChecks.txt",report.ToString());if(!valid)throw new InvalidOperationException(message);}
    void Move(CorridorLoopManager loop,Vector3 p,Quaternion q){loop.player.SetPositionAndRotation(p,q);var body=loop.player.GetComponent<Rigidbody>();body.position=p;body.rotation=q;loop.view.localRotation=Quaternion.identity;loop.mouseLook.SynchronizeAfterTeleport();Physics.SyncTransforms();}
    public IEnumerator Run()
    {
        var manager=FindFirstObjectByType<CorridorManager>();var loop=manager.loop;var selector=manager.blackDoorDestinations;
        var atmosphere=manager.GetComponent<AtmosphereManager>();var follower=manager.GetComponent<FollowerAudioManager>();
        var groups=selector.groups;var weights=groups.Select(g=>g.weight).ToArray();var random=UnityEngine.Random.state;
        var mover=loop.player.GetComponent<FirstPersonMovement>();var body=loop.player.GetComponent<Rigidbody>();
        bool movement=mover.enabled,look=loop.mouseLook.enabled,kinematic=body.isKinematic,background=Application.runInBackground;
        try
        {
            Application.runInBackground=true;mover.enabled=loop.mouseLook.enabled=false;body.isKinematic=true;loop.Initialize(true);
            Check(atmosphere.CompletedDoorTransitions==0,"Fresh atmosphere baseline");
            var mains=loop.corridors.Select(c=>c.environment.GetComponent<CorridorDestination>()).ToArray();
            var prefabs=groups.Select(g=>g.instance).Where(e=>e.kind==CorridorDestination.EnvironmentKind.DoorPrefab).ToArray();
            var environments=mains.Concat(prefabs).ToArray();
            Check(environments.Length==4 && environments.Distinct().Count()==4 && groups.Length==4,"Four distinct environments in shared registry");
            foreach(var env in environments)
            {
                Check(env.doorExits.Length==env.GetComponentsInChildren<BlackDoorTransition>().Length,"Every active designated door has an exit: "+env.name);
                foreach(var exit in env.doorExits) Check(exit.IsValid(loop) && loop.IsClear(exit.transform.position+exit.transform.forward*.3f,exit.transform.rotation),"Clear arrival and first forward step: "+env.name+" / "+exit.name);
                var reached=new System.Collections.Generic.HashSet<CorridorDestination>();var exits=new System.Collections.Generic.HashSet<DoorExitPoint>();
                for(int i=0;i<50;i++)
                {
                    Check(selector.TrySelectFrom(env,out var chosen) && chosen.environment.kind!=env.kind && chosen.transform!=chosen.environment.spawnPoint,"Opposite category and door-specific random arrival from "+env.name+" #"+i);
                    reached.Add(chosen.environment);exits.Add(chosen);
                }
                Check(reached.Count==2 && exits.Count>2,"Both opposite environments and multiple exit doors reached: "+env.name);
            }
            // Empty opposite pool must fail closed, never fall back to same-category or a StartPoint.
            foreach(var group in groups)group.weight=group.instance.kind==CorridorDestination.EnvironmentKind.MainLookBack?1:0;
            Check(!selector.TrySelectFrom(mains[0],out _),"No opposite destination means no transition, not a StartPoint fallback");
            for(int i=0;i<groups.Length;i++)groups[i].weight=weights[i];
            int[,] pairs={{0,2},{0,3},{1,2},{1,3},{2,0},{2,1},{3,0},{3,1}};
            for(int n=0;n<pairs.GetLength(0);n++)
            {
                var source=environments[pairs[n,0]];var target=environments[pairs[n,1]];
                foreach(var g in groups)g.weight=g.instance==target?1:0;
                if(source.kind==CorridorDestination.EnvironmentKind.MainLookBack)loop.EnterFromDoor(Array.IndexOf(mains,source),source.spawnPoint);
                else Check(loop.EnterDoorExit(source.doorExits[0]),"Set up prefab source environment");
                var portal=source.GetComponentsInChildren<BlackDoorTransition>().First(p=>Mathf.Abs(Vector3.Dot(p.entranceFrame.forward,source.spawnPoint.forward))<.5f);
                Move(loop,portal.entranceFrame.position-portal.entranceFrame.forward*.9f+Vector3.up*.01f,portal.entranceFrame.rotation);
                if(!portal.door.IsOpen)Check(manager.TryInteract(),"Existing camera/E interaction opens source door");
                yield return new WaitForSecondsRealtime(portal.door.openDuration+manager.arrivalCooldownSeconds+.1f);
                Check(manager.CompletedTransitions==n && atmosphere.CompletedDoorTransitions==n,"Opening without crossing does not teleport or darken");
                Check(BlackPixel(manager.gameplayCamera),"Opened doorway stays pitch black");
                Move(loop,portal.entranceFrame.position-portal.entranceFrame.forward*.32f+Vector3.up*.01f,portal.entranceFrame.rotation);
                yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                Check(portal.IsEntranceArmed,"Entrance side arms crossing");
                Move(loop,portal.entranceFrame.position+portal.entranceFrame.forward*.1f+Vector3.up*.01f,portal.entranceFrame.rotation);
                yield return new WaitForFixedUpdate();yield return null;
                Check(manager.IsBusy && !manager.TryBeginTransition(portal),"One transition only; repeat callback rejected");
                bool fullBlack=false;float deadline=Time.unscaledTime+7;
                while(manager.IsBusy && Time.unscaledTime<deadline){fullBlack|=manager.screenFade.Alpha>.999f;yield return null;}
                var exit=selector.LastSelectedExit;
                Check(!manager.IsBusy && fullBlack && manager.screenFade.Alpha==0 && manager.CompletedTransitions==n+1,"Fade completes exactly one transfer: "+source.name+" -> "+target.name);
                Check(loop.CurrentEnvironment==target && exit.environment==target && Vector3.Distance(loop.player.position,exit.transform.position)<.02f,"Arrival uses selected DoorExitPoint: "+exit.name);
                Check(exit.portal.door.IsOpen && Vector3.Angle(loop.view.forward,exit.transform.forward)<.1f && loop.IsClear(loop.player.position+loop.player.forward*.3f,loop.player.rotation),"Destination door open; player faces outward and can explore immediately");
                Check(!manager.CanEnter(exit.portal),"Arrival cooldown prevents immediate retrigger");
                Check(loop.LookBackCount==0 && follower.CurrentProfile==target.audioProfile && follower.MovingSeconds==0 && !follower.footstepSource.isPlaying,"Arrival resets look-back and follower state; correct audio profile");
                Check(atmosphere.CompletedDoorTransitions==n+1 && atmosphere.StageIndex==Mathf.Min(n+1,3),"One global darkness increment per door, clamped at maximum");
                yield return new WaitForSecondsRealtime(manager.arrivalCooldownSeconds+.1f);
                Check(manager.CompletedTransitions==n+1,"No immediate loop while standing at arrival");
                if(target.kind==CorridorDestination.EnvironmentKind.DoorPrefab)
                {
                    Check(loop.CurrentCorridor==-1,"Prefab arrival remains outside main look-back loop");var p=loop.player.position;
                    loop.SampleLook(target.spawnPoint.forward,1);loop.SampleLook(-target.spawnPoint.forward,1);loop.SampleLook(target.spawnPoint.forward,1);
                    Check(loop.player.position==p && loop.LookBackCount==0,"Look-back disabled in prefab destination");
                }
                else
                {
                    int index=Array.IndexOf(mains,target);Check(loop.CurrentCorridor==index,"Main arrival restores exact registered main index");
                    var frame=loop.corridors[index].startPoint;Move(loop,frame.TransformPoint(Vector3.forward*5),frame.rotation);
                    void Turn(){loop.SampleLook(frame.forward,1);loop.SampleLook(frame.forward,.2f);loop.SampleLook(-frame.forward,.2f);loop.SampleLook(frame.forward,.2f);}
                    Turn();Check(loop.CurrentCorridor==index && loop.LookBackCount==1 && Vector3.Distance(loop.player.position,frame.position)<.02f,"First look-back after main arrival returns to its own StartPoint");
                    Move(loop,frame.TransformPoint(Vector3.forward*7),frame.rotation);Turn();
                    Check(loop.CurrentCorridor==1-index && loop.LookBackCount==0 && Vector3.Distance(loop.player.position,loop.corridors[1-index].startPoint.TransformPoint(Vector3.forward*7))<.02f,"Second look-back preserves relative position in other main corridor");
                    Check(atmosphere.CompletedDoorTransitions==n+1,"Main look-backs do not advance darkness");
                }
            }
            Check(loop.corridors.Length==2 && FindObjectsByType<CorridorManager>(FindObjectsSortMode.None).Length==1 && FindObjectsByType<BlackDoorTransitionManager>(FindObjectsSortMode.None).Length==1,"No competing transition or main-loop managers");
            Debug.Log("ALTERNATING BLACK DOOR CHECKS PASSED (all eight cross-category pairs)");
        }
        finally
        {
            for(int i=0;i<groups.Length;i++)groups[i].weight=weights[i];loop.Initialize(true);body.isKinematic=kinematic;mover.enabled=movement;loop.mouseLook.enabled=look;Application.runInBackground=background;UnityEngine.Random.state=random;Destroy(gameObject);
        }
    }
    bool BlackPixel(Camera camera)
    {
        var active=RenderTexture.active;var rt=new RenderTexture(320,200,24);var texture=new Texture2D(320,200,TextureFormat.RGB24,false);
        try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,320,200),0,0);texture.Apply();var c=texture.GetPixel(160,100);return c.r<=1f/255 && c.g<=1f/255 && c.b<=1f/255;}
        finally{RenderTexture.active=active;Destroy(rt);Destroy(texture);}
    }
}
