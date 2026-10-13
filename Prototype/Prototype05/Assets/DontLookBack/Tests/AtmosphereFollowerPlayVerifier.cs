using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public sealed class AtmosphereFollowerPlayVerifier : MonoBehaviour
{
    const string Folder = "IntegrationBackups/AtmosphereFollower";
    readonly StringBuilder report = new StringBuilder();
    void Check(bool valid, string message)
    {
        report.AppendLine((valid ? "PASS " : "FAIL ") + message); File.WriteAllText(Folder + "/PlayChecks.txt", report.ToString());
        if (!valid) throw new InvalidOperationException(message);
    }
    void Move(CorridorLoopManager loop, Vector3 p, Quaternion q)
    {
        loop.player.SetPositionAndRotation(p, q);
        var body = loop.player.GetComponent<Rigidbody>(); body.position = p; body.rotation = q;
        loop.view.localRotation = Quaternion.identity; Physics.SyncTransforms();
    }
    public IEnumerator Run()
    {
        var manager = FindFirstObjectByType<CorridorManager>(); var loop = manager.loop;
        var atmosphere = manager.GetComponent<AtmosphereManager>(); var follower = manager.GetComponent<FollowerAudioManager>();
        var body = loop.player.GetComponent<Rigidbody>(); var mover = loop.player.GetComponent<FirstPersonMovement>();
        bool movingEnabled = mover.enabled, lookEnabled = loop.mouseLook.enabled, followerEnabled = follower.enabled, kinematic = body.isKinematic, background = Application.runInBackground;
        var pool = manager.destinations; float cooldown = manager.arrivalCooldownSeconds;
        bool playerStepTiming = follower.usePlayerStepTimingForEcho;
        var profiles = pool.Select(d => d.instance.audioProfile).ToArray();
        var lightmaps = LightmapSettings.lightmaps.Select(m => m.lightmapColor).ToArray();
        var baked = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => LightBakeTypeUtility.GetBakeType(l) == LightmapBakeType.Baked).ToDictionary(l => l, l => l.intensity);
        CorridorAudioProfile echo = null, mismatch = null;
        Action playerStepObserved = null; Action<AudioClip> followerStepObserved = null;
        try
        {
            Application.runInBackground = true; mover.enabled = loop.mouseLook.enabled = false; body.isKinematic = true;
            loop.Initialize(true);
            Check(atmosphere.CompletedDoorTransitions == 0 && atmosphere.StageIndex == 0, "Fresh play session starts Normal");
            Check(atmosphere.BakedLightCount == 20, "All 20 baked lamps identified and preserved");
            Check(follower.CurrentProfile == profiles[0] && profiles[0] != profiles[1], "Corridor A/B carry distinct audio profiles");
            Check(profiles.All(p => p.clips.Length > 0 && p.clips.All(c => c)), "Real imported clips assigned; no placeholders");
            Check(!profiles[0].clips.Intersect(profiles[1].clips).Any(), "Echo tile and Mismatch wood clips differ");
            Check(follower.footstepSource.spatialBlend == 1 && !follower.footstepSource.loop && follower.footstepSource.dopplerLevel == 0, "One spatial footstep voice without Doppler teleport artifacts");
            Check(follower.playerFootsteps && follower.usePlayerStepTimingForEcho, "Echo subscribes to actual player step timing while using its own audio voice");
            echo = Instantiate(profiles[0]); echo.activationMovingSeconds = 0; echo.resumeDelay = Vector2.zero;
            pool[0].instance.audioProfile = echo; follower.ApplyCorridor(0);
            float playerStepAt = -1, followerStepAt = -1;
            playerStepObserved = () => { if (playerStepAt < 0) playerStepAt = Time.time; };
            followerStepObserved = clip => { if (followerStepAt < 0) followerStepAt = Time.time; };
            follower.playerFootsteps.StepStarted += playerStepObserved; follower.FootstepPlayed += followerStepObserved;
            follower.playerFootsteps.ResetMotionAudio();
            Move(loop, loop.player.position + Vector3.forward * .08f, Quaternion.identity);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Check(playerStepAt >= 0 && follower.PendingSteps > 0, "Real player step signal queues Echo independently of clip playback");
            yield return new WaitForSecondsRealtime(.65f);
            Check(followerStepAt - playerStepAt >= .44f && followerStepAt - playerStepAt <= .62f, "Actual Echo begins approximately 0.5 seconds after player step");
            follower.playerFootsteps.StepStarted -= playerStepObserved; follower.FootstepPlayed -= followerStepObserved;
            playerStepObserved = null; followerStepObserved = null;
            pool[0].instance.audioProfile = profiles[0]; follower.ApplyCorridor(0); Destroy(echo); echo = null;
            void Turn()
            {
                var frame = loop.corridors[loop.CurrentCorridor].startPoint;
                loop.SampleLook(frame.forward, 1); loop.SampleLook(frame.forward, .2f);
                loop.SampleLook(-frame.forward, .2f); loop.SampleLook(frame.forward, .2f);
            }
            Turn(); Check(loop.LookBackCount == 1 && atmosphere.CompletedDoorTransitions == 0, "First look-back does not darken scene");
            Turn(); Check(loop.CurrentCorridor == 1 && atmosphere.CompletedDoorTransitions == 0 && follower.CurrentProfile == profiles[1], "Second look-back switches audio profile without darkness progression");
            follower.enabled = false;
            var normalBrightness = new float[2];
            for (int i = 0; i < 2; i++)
            {
                loop.EnterFromDoor(i, pool[i].instance.spawnPoint);
                Move(loop, loop.corridors[i].startPoint.TransformPoint(Vector3.forward * 4), loop.corridors[i].startPoint.rotation);
                yield return null; normalBrightness[i] = Capture(manager.gameplayCamera, "Normal" + i);
            }
            loop.Initialize(true);
            var portals = FindObjectsByType<BlackDoorTransition>(FindObjectsSortMode.None);
            var initialDoors = new BlackDoorTransition[2];
            for (int i = 0; i < 2; i++)
            {
                var corridor = loop.corridors[i];
                initialDoors[i] = portals.Where(p => p.transform.IsChildOf(corridor.environment) && p.entranceFrame.position.x < corridor.startPoint.position.x
                    && Mathf.Abs(Vector3.Dot(p.entranceFrame.forward, corridor.startPoint.forward)) < .5f).OrderBy(p => (p.entranceFrame.position - corridor.startPoint.position).sqrMagnitude).First();
                initialDoors[i].door.TryOpen();
            }
            yield return new WaitForSecondsRealtime(1.7f);
            manager.arrivalCooldownSeconds = .02f;
            for (int crossing = 0; crossing < 5; crossing++)
            {
                int index = loop.CurrentCorridor;
                if (crossing == 4) manager.destinations = new[] { pool[index] }; // valid same-corridor fallback
                var portal = initialDoors[index];
                yield return new WaitForSecondsRealtime(.05f);
                Move(loop, portal.entranceFrame.position - portal.entranceFrame.forward * .32f + Vector3.up * .01f, portal.entranceFrame.rotation);
                yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                Check(portal.IsEntranceArmed, "Door approach armed " + crossing);
                Move(loop, portal.entranceFrame.position + portal.entranceFrame.forward * .1f + Vector3.up * .01f, portal.entranceFrame.rotation);
                yield return new WaitForFixedUpdate(); yield return null;
                float deadline = Time.unscaledTime + 4;
                while (manager.IsBusy && Time.unscaledTime < deadline) yield return null;
                Check(!manager.IsBusy && manager.CompletedTransitions == crossing + 1, "Actual black door transition completes exactly once " + crossing);
                Check(atmosphere.CompletedDoorTransitions == Mathf.Min(crossing + 1, 4), "Darkness counts only successful changes to another corridor " + crossing);
                atmosphere.Apply(10); yield return null;
                Check(atmosphere.StageIndex == Mathf.Min(crossing + 1, 3), "Four-stage progression clamps at Dread " + crossing);
            }
            manager.destinations = pool;
            float dreadExposure = atmosphere.CurrentExposure;
            for (int i = 0; i < 2; i++)
            {
                loop.EnterFromDoor(i, pool[i].instance.spawnPoint);
                Move(loop, loop.corridors[i].startPoint.TransformPoint(Vector3.forward * 4), loop.corridors[i].startPoint.rotation);
                yield return null; float dreadBrightness = Capture(manager.gameplayCamera, "Dread" + i);
                Check(dreadBrightness < normalBrightness[i] * .95f && dreadBrightness > normalBrightness[i] * .5f,
                    "Rendered Dread darkens corridor without erasing visibility " + i + " ratio=" + dreadBrightness / normalBrightness[i]);
                Check(Mathf.Approximately(atmosphere.CurrentExposure, dreadExposure), "Returning to corridor preserves global darkness " + i);
                for (float distance = 0; distance < 53; distance += .5f)
                    Check(loop.IsClear(loop.corridors[i].startPoint.TransformPoint(Vector3.forward * distance), loop.corridors[i].startPoint.rotation), "Dread route grounded and clear " + i + "/" + distance);
            }
            Check(baked.All(p => p.Key.intensity == p.Value) && LightmapSettings.lightmaps.Select(m => m.lightmapColor).SequenceEqual(lightmaps), "Baked light intensities and original lightmap textures unchanged");
            Check(dreadExposure >= -1 && dreadExposure < 0, "Dread exposure bounded above pitch-black range");
            Check(!manager.TryBeginTransition(initialDoors[0]) && atmosphere.CompletedDoorTransitions == 4, "Rejected doorway attempt cannot advance atmosphere");

            // Independent movement samples exercise follower cadence without playing
            // or observing the player's footstep source, and without changing assets.
            echo = Instantiate(profiles[0]); mismatch = Instantiate(profiles[1]);
            follower.usePlayerStepTimingForEcho = false; // Exercise independent fallback cadence as well.
            echo.activationMovingSeconds = .3f; echo.strideMeters = .4f; echo.resumeDelay = Vector2.zero;
            mismatch.activationMovingSeconds = 0; mismatch.strideMeters = .4f; mismatch.resumeDelay = Vector2.zero;
            mismatch.extraStepsChance = 1; mismatch.extraSteps = new Vector2Int(3, 3);
            pool[0].instance.audioProfile = echo; pool[1].instance.audioProfile = mismatch;
            loop.EnterFromDoor(0, pool[0].instance.spawnPoint); follower.ApplyCorridor(0);
            Vector3 sample = loop.player.position;
            void Tick(bool walking, Vector3 gaze, float dt = .1f, bool suspended = false)
            {
                if (walking) sample += Vector3.forward * (.2f * dt / .1f);
                Move(loop, sample, Quaternion.identity);
                follower.SampleMotion(sample, gaze, dt, true, suspended);
            }
            int played = follower.PlayedFootsteps;
            Tick(true, Vector3.forward); Tick(true, Vector3.forward);
            Check(follower.PendingSteps == 0 && follower.PlayedFootsteps == played, "Follower waits for movement activation time");
            for (int i = 0; i < 3; i++) Tick(true, Vector3.forward);
            Check(follower.PendingSteps > 0 && follower.PlayedFootsteps == played, "Echo beat queued for delayed playback");
            for (int i = 0; i < 7; i++) Tick(false, Vector3.forward);
            Check(follower.PlayedFootsteps > played && follower.footstepSource.clip && echo.clips.Contains(follower.footstepSource.clip), "Echo plays imported tile clip after delayed movement beat");
            Check(Vector3.Dot(follower.footstepSource.transform.position - loop.player.position, follower.TravelDirection) < -2, "3D source stays behind movement direction");
            Tick(true, Vector3.forward); Tick(true, Vector3.forward);
            Tick(false, Vector3.back);
            Check(follower.LookingBack && follower.PendingSteps == 0 && !follower.footstepSource.isPlaying, "Looking backward silences voice and cancels queued beats");
            echo.resumeDelay = new Vector2(.5f, .7f); Tick(true, Vector3.forward);
            played = follower.PlayedFootsteps; for (int i = 0; i < 3; i++) Tick(true, Vector3.forward);
            Check(follower.PlayedFootsteps == played && follower.PendingSteps == 0, "Forward return waits for randomized resume delay");
            Tick(true, Vector3.forward, .1f, true);
            Check(follower.PendingSteps == 0 && follower.MovingSeconds == 0 && !follower.footstepSource.isPlaying, "Fade/teleport suppression clears temporary follower state");
            loop.EnterFromDoor(1, pool[1].instance.spawnPoint); follower.ApplyCorridor(1); sample = loop.player.position;
            Tick(true, Vector3.forward); Tick(true, Vector3.forward); Tick(false, Vector3.forward);
            Check(follower.PendingSteps == 3 && follower.CurrentProfile == mismatch, "Mismatch schedules at most three tail steps after stopping");
            played = follower.PlayedFootsteps;
            for (int i = 0; i < 25; i++) { Tick(false, Vector3.forward); yield return null; }
            Check(follower.PlayedFootsteps > played && mismatch.clips.Contains(follower.footstepSource.clip), "Mismatch plays different imported wood clips");
            Check(follower.PendingSteps == 0 && follower.PlayedFootsteps - played <= 3, "Tail sequence is bounded and drains without stacking");
            sample += Vector3.right * 200; Tick(false, Vector3.forward);
            Check(follower.PendingSteps == 0 && follower.MovingSeconds == 0 && !follower.footstepSource.isPlaying, "Unsignaled teleport displacement cannot generate footsteps");
            Debug.Log("ATMOSPHERE AND FOLLOWER CHECKS PASSED");
        }
        finally
        {
            if (playerStepObserved != null) follower.playerFootsteps.StepStarted -= playerStepObserved;
            if (followerStepObserved != null) follower.FootstepPlayed -= followerStepObserved;
            manager.destinations = pool; manager.arrivalCooldownSeconds = cooldown;
            follower.usePlayerStepTimingForEcho = playerStepTiming;
            for (int i = 0; i < pool.Length; i++) pool[i].instance.audioProfile = profiles[i];
            follower.enabled = followerEnabled; loop.SuspendForDoor(false); loop.Initialize(true);
            body.isKinematic = kinematic; mover.enabled = movingEnabled; loop.mouseLook.SynchronizeAfterTeleport(); loop.mouseLook.enabled = lookEnabled;
            Application.runInBackground = background;
            if (echo) Destroy(echo); if (mismatch) Destroy(mismatch); Destroy(gameObject);
        }
    }
    float Capture(Camera camera, string name)
    {
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var target = new RenderTexture(960, 600, 24); var pixels = new Texture2D(960, 600, TextureFormat.RGB24, false);
        try
        {
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); pixels.Apply(); File.WriteAllBytes(Folder + "/" + name + ".png", pixels.EncodeToPNG());
            return pixels.GetPixels().Average(c => c.grayscale);
        }
        finally { camera.targetTexture = previousTarget; RenderTexture.active = previousActive; Destroy(target); Destroy(pixels); }
    }
}
