using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Temporary Play mode test runner, never added to the saved gameplay scene.
public sealed class BlackDoorPlayVerifier : MonoBehaviour
{
    public GameObject prefabFixture;
    public RuntimeAnimatorController existingOpeningController;
    readonly StringBuilder report = new StringBuilder();
    void Check(bool condition, string description)
    {
        if (!condition) { File.WriteAllText("IntegrationBackups/BlackDoors/PlayChecksFailed.txt", report + "FAIL " + description); throw new InvalidOperationException(description); }
        report.AppendLine("PASS " + description);
    }
    void Move(CorridorManager manager, Vector3 position, Quaternion rotation)
    {
        var body = manager.loop.player.GetComponent<Rigidbody>();
        manager.loop.player.SetPositionAndRotation(position, rotation);
        body.position = position; body.rotation = rotation;
        if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        manager.loop.view.localRotation = Quaternion.identity;
        manager.loop.mouseLook.SynchronizeAfterTeleport();
        Physics.SyncTransforms();
    }
    public IEnumerator Run()
    {
        var manager = FindFirstObjectByType<CorridorManager>();
        var loop = manager.loop;
        var doors = FindObjectsByType<BlackDoorTransition>(FindObjectsSortMode.None);
        BlackDoorTransition InitialSideDoor(int index) => doors.Where(d => d.transform.IsChildOf(loop.corridors[index].environment)
            && Mathf.Abs(Vector3.Dot(d.entranceFrame.forward, loop.corridors[index].startPoint.forward)) < 0.5f
            && d.entranceFrame.position.x < loop.corridors[index].startPoint.position.x)
            .OrderBy(d => (d.entranceFrame.position - loop.corridors[index].startPoint.position).sqrMagnitude).First();
        var doorA = InitialSideDoor(0);
        var doorB = InitialSideDoor(1);
        var originalPool = manager.destinations;
        bool oldBackground = Application.runInBackground;
        var originalPreference = manager.preferDifferentCorridor;
        var randomState = UnityEngine.Random.state;
        var mover = loop.player.GetComponent<FirstPersonMovement>();
        bool moverEnabled = mover.enabled, lookEnabled = loop.mouseLook.enabled;
        float oldRange = manager.interactionRange;
        Vector3 oldSpawnB = originalPool[1].instance.spawnPoint.position;
        GameObject invalidMarkerObject = null, prefabInstance = null, animatedFixture = null;
        try
        {
            Application.runInBackground = true;
            mover.enabled = false; loop.mouseLook.enabled = false;
            loop.Initialize(true); loop.SuspendForDoor(true);
            var a = originalPool[0].instance; var b = originalPool[1].instance;
            invalidMarkerObject = new GameObject("Temporary invalid destination");
            var invalid = invalidMarkerObject.AddComponent<CorridorDestination>();
            manager.destinations = new[] { null, new CorridorManager.Destination(), new CorridorManager.Destination { instance = invalid }, originalPool[0], originalPool[1] };
            for (int i = 0; i < 12; i++)
            { Check(manager.TrySelectDestination(out var choice) && choice == b, "Prefer a different valid corridor " + i); }
            manager.destinations = new[] { originalPool[0] };
            Check(manager.TrySelectDestination(out var single) && single == a, "Single valid corridor supported");
            manager.destinations = new[] { null, new CorridorManager.Destination { instance = invalid } };
            Check(!manager.TrySelectDestination(out _), "Missing and invalid destinations skipped");
            manager.destinations = originalPool; manager.preferDifferentCorridor = false;
            UnityEngine.Random.InitState(2026);
            var seen = new System.Collections.Generic.HashSet<CorridorDestination>();
            for (int i = 0; i < 64; i++) { Check(manager.TrySelectDestination(out var choice), "Random choice valid " + i); seen.Add(choice); }
            Check(seen.Count == 2, "Random pool reaches both corridors");
            manager.preferDifferentCorridor = true;
            if (prefabFixture)
            {
                var entry = new CorridorManager.Destination { prefab = prefabFixture };
                manager.destinations = new[] { entry };
                Check(manager.TrySelectDestination(out var spawned) && spawned && spawned.gameObject.scene.IsValid(), "Optional prefab destination instantiated safely");
                prefabInstance = spawned.gameObject;
                Check(manager.TrySelectDestination(out var cached) && cached == spawned, "Prefab instance reused instead of repeatedly instantiated");
                Check(spawned.GetComponentsInChildren<Camera>(true).Length == 0 && spawned.GetComponentsInChildren<FirstPersonMovement>(true).Length == 0, "Prefab spawning adds no player or camera");
                Destroy(prefabInstance); prefabInstance = null; yield return null;
            }
            manager.destinations = originalPool;
            if (existingOpeningController)
            {
                animatedFixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
                animatedFixture.name = "Temporary existing-animation check";
                animatedFixture.transform.position = new Vector3(700, 0, 0);
                var animator = animatedFixture.AddComponent<Animator>();
                animator.runtimeAnimatorController = existingOpeningController; animator.enabled = false;
                var animatedDoor = animatedFixture.AddComponent<DoorInteraction>();
                animatedDoor.animator = animator; animatedDoor.openingState = "Base Layer.Open01";
                animatedDoor.openDuration = 1.5f;
                var mask = new GameObject("Temporary mask"); mask.transform.SetParent(animatedFixture.transform, false);
                animatedDoor.blackVoid = mask;
                Check(animatedDoor.TryOpen(), "Existing opening animation can be requested");
                yield return new WaitForSecondsRealtime(1.7f);
                Check(animatedDoor.IsOpen && animator.speed == 0 && Quaternion.Angle(animatedDoor.ClosedRotation, animatedDoor.hinge.localRotation) > 60,
                    "Existing looping Open01 clip plays and holds its open pose");
                Destroy(animatedFixture); animatedFixture = null; yield return null;
            }
            Move(manager, doorA.entranceFrame.position - doorA.entranceFrame.forward * 0.9f + Vector3.up * 0.01f, doorA.entranceFrame.rotation);
            manager.interactionRange = 0.1f;
            Check(!manager.TryInteract() && !doorA.door.HasBeenOpened, "E interaction respects range");
            manager.interactionRange = oldRange;
            Check(manager.TryInteract(), "Center-view ray opens configured A door");
            Check(doorA.door.blackVoid.activeSelf, "Dark mask enabled before door starts opening");
            yield return new WaitForSecondsRealtime(doorA.door.openDuration + 0.15f);
            Check(doorA.door.IsOpen && Quaternion.Angle(doorA.door.ClosedRotation, doorA.door.hinge.localRotation) > 60, "Door hinge opens correctly");
            Check(!manager.TryInteract(), "Repeated E does not restart opening");
            CheckOpaque(manager, doorA, "A");
            // Establish a first look-back count, then verify fade rotations cannot add one.
            loop.SuspendForDoor(false); loop.Initialize(true);
            var forward = loop.corridors[0].startPoint.forward;
            loop.SampleLook(forward, 0.2f); loop.SampleLook(-forward, 0.2f); loop.SampleLook(forward, 0.2f);
            Check(loop.LookBackCount == 1, "Existing first look-back still functions");
            loop.SuspendForDoor(true);
            Move(manager, doorA.entranceFrame.position - doorA.entranceFrame.forward * 0.32f + Vector3.up * 0.01f, doorA.entranceFrame.rotation);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Check(doorA.IsEntranceArmed, "Entrance-side approach arms the trigger");
            Move(manager, doorA.entranceFrame.position + doorA.entranceFrame.forward * 0.10f + Vector3.up * 0.01f, doorA.entranceFrame.rotation);
            yield return new WaitForFixedUpdate(); yield return null;
            Check(manager.IsBusy, "Walking across the doorway begins transition");
            Check(!manager.TryBeginTransition(doorA), "Second callback cannot start another transition");
            loop.SampleLook(-forward, 0.2f); loop.SampleLook(forward, 0.2f);
            Check(loop.LookBackCount == 1 && loop.CurrentCorridor == 0, "Fade rotation does not count as a look-back");
            bool fullyBlack = false;
            float deadline = Time.unscaledTime + 4;
            while (manager.IsBusy && Time.unscaledTime < deadline)
            { fullyBlack |= manager.screenFade.Alpha >= 0.999f; yield return null; }
            Check(!manager.IsBusy && fullyBlack && manager.screenFade.Alpha == 0, "Smooth fade reaches black and returns to clear");
            Check(manager.CompletedTransitions == 1 && loop.CurrentCorridor == 1 && loop.LookBackCount == 0, "Exactly one A to B arrival resets look-back count");
            Check(Vector3.Distance(loop.player.position, b.spawnPoint.position) < 0.1f && Vector3.Angle(loop.view.forward, b.spawnPoint.forward) < 0.1f, "Arrival is at valid forward-facing SpawnPoint");
            Check(!manager.CanEnter(doorA), "Arrival cooldown blocks immediate retry");
            Check(loop.IsClear(loop.player.position, loop.player.rotation), "Arrival does not place player inside a wall");
            loop.SuspendForDoor(true);
            Move(manager, doorB.entranceFrame.position - doorB.entranceFrame.forward * 0.9f + Vector3.up * 0.01f, doorB.entranceFrame.rotation);
            Check(manager.TryInteract(), "Center-view ray opens configured B door");
            yield return new WaitForSecondsRealtime(doorB.door.openDuration + manager.arrivalCooldownSeconds + 0.15f);
            Check(doorB.door.IsOpen, "B door opens"); CheckOpaque(manager, doorB, "B");
            // Enter from the far side; this must not count as an entrance crossing.
            Move(manager, doorB.entranceFrame.position + doorB.entranceFrame.forward * 0.95f + Vector3.up * 0.01f, doorB.entranceFrame.rotation);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Move(manager, doorB.entranceFrame.position + doorB.entranceFrame.forward * 0.2f + Vector3.up * 0.01f, doorB.entranceFrame.rotation);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Check(!manager.IsBusy && !doorB.IsEntranceArmed && manager.CompletedTransitions == 1, "Wrong-side trigger entry does not transition");
            // Deliberately put an arrival point overlapping an open door's trigger.
            loop.Initialize(true); loop.SuspendForDoor(true);
            // Find a genuinely clear arrival that overlaps the trigger; frame ledges
            // differ between door models, so do not assume one fixed clearance.
            var playerCapsule = loop.player.GetComponent<CapsuleCollider>();
            bool foundOverlapSpawn = false;
            for (float distance = 0.25f; distance <= 0.44f; distance += 0.01f)
            {
                Vector3 candidate = doorB.entranceFrame.position - doorB.entranceFrame.forward * distance + Vector3.up * 0.01f;
                if (loop.IsClear(candidate, b.spawnPoint.rotation) && Physics.ComputePenetration(playerCapsule, candidate, b.spawnPoint.rotation,
                    doorB.Trigger, doorB.Trigger.transform.position, doorB.Trigger.transform.rotation, out _, out _))
                { b.spawnPoint.position = candidate; foundOverlapSpawn = true; break; }
            }
            Check(foundOverlapSpawn, "Found unobstructed arrival overlapping the door trigger");
            manager.destinations = new[] { originalPool[1] };
            Check(manager.TrySelectDestination(out var overlapChoice) && overlapChoice == b, "Arrival-overlap SpawnPoint remains valid");
            deadline = Time.unscaledTime + 4;
            while (!manager.CanEnter(doorA) && Time.unscaledTime < deadline) yield return null;
            Check(manager.CanEnter(doorA), "Source door becomes available within cooldown deadline");
            Move(manager, doorA.entranceFrame.position - doorA.entranceFrame.forward * 0.32f + Vector3.up * 0.01f, doorA.entranceFrame.rotation);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Check(doorA.IsEntranceArmed, "Repeat entrance-side approach arms the trigger");
            Move(manager, doorA.entranceFrame.position + doorA.entranceFrame.forward * 0.10f + Vector3.up * 0.01f, doorA.entranceFrame.rotation);
            yield return new WaitForFixedUpdate(); yield return null;
            Check(manager.IsBusy, "Second physical crossing starts one new transition");
            deadline = Time.unscaledTime + 4;
            while (manager.IsBusy && Time.unscaledTime < deadline) yield return null;
            yield return new WaitForSecondsRealtime(manager.arrivalCooldownSeconds + 0.2f);
            Check(manager.CompletedTransitions == 2 && !manager.CanEnter(doorB), "Arrival-overlap lock survives cooldown while player remains in doorway");
            Move(manager, doorB.entranceFrame.position - doorB.entranceFrame.forward * 0.9f + Vector3.up * 0.01f, doorB.entranceFrame.rotation);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Check(manager.CanEnter(doorB), "Arrival door rearms only after player exits its trigger");
            Check(!loop.TransitionSuspended && !manager.IsBusy && manager.screenFade.Alpha == 0, "Transition restores usable game state");
            File.WriteAllText("IntegrationBackups/BlackDoors/PlayChecks.txt", report.ToString());
            Debug.Log("BLACK DOOR CHECKS PASSED; IntegrationBackups/BlackDoors/PlayChecks.txt");
        }
        finally
        {
            Application.runInBackground = oldBackground;
            manager.destinations = originalPool; manager.preferDifferentCorridor = originalPreference;
            manager.interactionRange = oldRange; UnityEngine.Random.state = randomState;
            originalPool[1].instance.spawnPoint.position = oldSpawnB;
            loop.SuspendForDoor(false); loop.Initialize(true);
            mover.enabled = moverEnabled; loop.mouseLook.enabled = lookEnabled;
            if (invalidMarkerObject) Destroy(invalidMarkerObject);
            if (prefabInstance) Destroy(prefabInstance);
            if (animatedFixture) Destroy(animatedFixture);
            Destroy(gameObject);
        }
    }
    void CheckOpaque(CorridorManager manager, BlackDoorTransition door, string suffix)
    {
        var camera = manager.gameplayCamera;
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var lightObject = new GameObject("Temporary bright flashlight test");
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Point; light.range = 10; light.intensity = 10000;
        lightObject.transform.position = camera.transform.position;
        var target = new RenderTexture(320, 200, 24);
        var pixels = new Texture2D(320, 200, TextureFormat.RGB24, false);
        try
        {
            Check(door.door.blackVoid.GetComponent<Renderer>().sharedMaterial.shader.name == "DontLookBack/Black Door Void", suffix + " uses opaque lighting-independent void shader");
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 320, 200), 0, 0); pixels.Apply();
            Color center = pixels.GetPixel(160, 100);
            File.WriteAllBytes("IntegrationBackups/BlackDoors/Door" + suffix + ".png", pixels.EncodeToPNG());
            Check(center.r <= 1f / 255 && center.g <= 1f / 255 && center.b <= 1f / 255, suffix + " void renders black with intensity-10000 light: " + center);
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            DestroyImmediate(lightObject); DestroyImmediate(target); DestroyImmediate(pixels);
        }
    }
}
