using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Temporary menu-driven Play mode verifier; never attached to the saved scene.
public sealed class UniversalDoorPlayVerifier : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    void Check(bool valid, string message)
    {
        report.AppendLine((valid ? "PASS " : "FAIL ") + message);
        File.WriteAllText("IntegrationBackups/UniversalDoors/PlayChecks.txt", report.ToString());
        if (!valid) throw new InvalidOperationException(message);
    }
    public IEnumerator Run()
    {
        var manager = FindFirstObjectByType<CorridorManager>(); var loop = manager.loop;
        var mover = loop.player.GetComponent<FirstPersonMovement>();
        bool movementEnabled = mover.enabled, lookEnabled = loop.mouseLook.enabled;
        var body = loop.player.GetComponent<Rigidbody>(); bool wasKinematic = body.isKinematic;
        var portals = FindObjectsByType<BlackDoorTransition>(FindObjectsSortMode.None);
        var ui = FindFirstObjectByType<DoorCrosshair>();
        bool oldBackground = Application.runInBackground;
        float fadeOut = manager.fadeOutSeconds, hold = manager.blackHoldSeconds, fadeIn = manager.fadeInSeconds, cooldown = manager.arrivalCooldownSeconds;
        try
        {
            Application.runInBackground = true;
            mover.enabled = loop.mouseLook.enabled = false; body.isKinematic = true;
            loop.Initialize(true); loop.SuspendForDoor(true);
            Check(portals.Length == 80, "All 80 active scene door panels have portals; five inactive connectors retained dormant");
            Check(!manager.FindTargetDoor() && !ui.prompt.enabled, "Prompt hidden when no door targeted");
            foreach (var portal in portals)
            {
                var door = portal.door;
                if (door.startsOpen) { Check(door.IsOpen && door.blackVoid.activeSelf, "Already-open connector preserved"); continue; }
                var frame = portal.entranceFrame;
                // Move the real player/camera to the entrance side and aim through the doorway.
                Vector3 p = frame.position - frame.forward * 0.85f + Vector3.up * 0.01f;
                loop.player.SetPositionAndRotation(p, frame.rotation); body.position = p; body.rotation = frame.rotation;
                loop.view.localRotation = Quaternion.identity; Physics.SyncTransforms();
                float range = manager.interactionRange; manager.interactionRange = 0.05f;
                Check(!manager.FindTargetDoor(), "Range respected: " + door.transform.parent.parent.name);
                manager.interactionRange = range;
                var target = manager.FindTargetDoor();
                // Adjacent authored doors may obscure one another until the first
                // opening completes. Test their real occlusion, without bypassing it.
                if (target != door) { yield return new WaitForSecondsRealtime(1.7f); target = manager.FindTargetDoor(); }
                Check(target == door, "Center ray resolves door component: " + door.transform.parent.parent.name + " at " + frame.position);
                yield return null; yield return null;
                Check(ui.prompt.enabled && ui.prompt.text == "[E] Open Door", "Target prompt displayed");
                if (!File.Exists("IntegrationBackups/UniversalDoors/Crosshair.png"))
                {
                    ScreenCapture.CaptureScreenshot("IntegrationBackups/UniversalDoors/Crosshair.png");
                    yield return null; yield return null;
                }
                // Drive the existing sole input path; no additional input handler.
                Check(manager.TryInteract() && door.blackVoid.activeSelf, "Interaction opens panel and reveals mask");
                Check(!door.IsOpen && !manager.TryBeginTransition(portal), "Opening door cannot trigger premature teleport");
                Check(!manager.TryInteract(), "Repeated interaction is disabled");
            }
            loop.Initialize(true); loop.SuspendForDoor(true);
            yield return new WaitForSecondsRealtime(2);
            foreach (var portal in portals)
            {
                var door = portal.door;
                Check(door.IsOpen && door.blackVoid.activeInHierarchy, "Open black portal: " + door.transform.parent.parent.name);
                Check(door.hinge.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "Moving panel colliders release passage");
                Vector3 crossing = portal.entranceFrame.position + portal.entranceFrame.forward * 0.10f + Vector3.up * 0.01f;
                Check(loop.IsClear(crossing, portal.entranceFrame.rotation), "Grounded crossing without wall collision: " + portal.entranceFrame.position);
                if (door.animator) Check(door.animator.speed == 0, "Existing opening clip held at final pose");
            }
            Check(manager.CompletedTransitions == 0, "Opening doors and aiming causes no transition");
            // Fast timings for this exhaustive check only; saved gameplay values
            // remain unchanged. Cross each real trigger using physics callbacks.
            manager.fadeOutSeconds = manager.fadeInSeconds = 0.02f;
            manager.blackHoldSeconds = 0; manager.arrivalCooldownSeconds = 0.02f;
            foreach (var portal in portals)
            {
                var source = portal.GetComponentInParent<CorridorDestination>();
                int sourceIndex = Array.FindIndex(loop.corridors, c => c.environment == source.Environment);
                Check(loop.EnterFromDoor(sourceIndex, source.spawnPoint), "Source corridor entered safely");
                loop.SuspendForDoor(true);
                yield return new WaitForSecondsRealtime(0.05f);
                void Move(Vector3 p)
                {
                    var frame = portal.entranceFrame;
                    loop.player.SetPositionAndRotation(p, frame.rotation); body.position = p; body.rotation = frame.rotation;
                    loop.view.localRotation = Quaternion.identity; Physics.SyncTransforms();
                }
                int before = manager.CompletedTransitions;
                Move(portal.entranceFrame.position - portal.entranceFrame.forward * 0.32f + Vector3.up * 0.01f);
                yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                Check(portal.IsEntranceArmed, "Real entrance-side approach arms " + portal.door.transform.parent.parent.name);
                Move(portal.entranceFrame.position + portal.entranceFrame.forward * 0.10f + Vector3.up * 0.01f);
                yield return new WaitForFixedUpdate(); yield return null;
                float deadline = Time.unscaledTime + 3;
                while (manager.IsBusy && Time.unscaledTime < deadline) yield return null;
                Check(!manager.IsBusy && manager.CompletedTransitions == before + 1, "Exactly one transition from " + portal.door.transform.parent.parent.name + " at " + portal.entranceFrame.position);
                Check(loop.CurrentCorridor != sourceIndex && loop.LookBackCount == 0, "Different destination and reset count");
                var destination = manager.destinations.First(d => d.instance.Environment == loop.corridors[loop.CurrentCorridor].environment).instance;
                Check(Vector3.Distance(loop.player.position, destination.spawnPoint.position) < 0.01f && Vector3.Angle(loop.view.forward, destination.spawnPoint.forward) < 0.1f,
                    "Clear SpawnPoint arrival with forward orientation");
            }
            Check(manager.CompletedTransitions == portals.Length, "All 80 active doorways physically trigger exactly once");
            Debug.Log("Universal all-door Play checks passed.");
        }
        finally
        {
            Application.runInBackground = oldBackground;
            manager.fadeOutSeconds = fadeOut; manager.blackHoldSeconds = hold; manager.fadeInSeconds = fadeIn; manager.arrivalCooldownSeconds = cooldown;
            body.isKinematic = wasKinematic;
            loop.SuspendForDoor(false); loop.Initialize(true);
            mover.enabled = movementEnabled; loop.mouseLook.SynchronizeAfterTeleport(); loop.mouseLook.enabled = lookEnabled;
            Destroy(gameObject);
        }
    }
}
