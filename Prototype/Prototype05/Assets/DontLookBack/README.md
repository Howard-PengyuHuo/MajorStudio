# Don't Look Back

Open `Assets/DontLookBack/DontLookBack.unity` and press Play. Use WASD and mouse.
The black-door mechanic uses E and alternates Main/Prefab environments; see [Alternating Black Doors](AlternatingBlackDoors.md) for routing, references and tests, and [Corridor systems](CorridorSystems.md) for darkness and audio.
The existing minimal player retains its walking speed and disabled running setting.
There is one active gameplay camera, AudioListener and player controller.

## Scene layout

Demo 1's Corridor and Corridor (1) connect to its original Assets environment;
their floor spans overlap slightly at the joins, with no floor gaps. They and their
Lighting root are grouped under Corridor_A without moving their geometry.
Corridor_B uses Demo 2's Corridor2, another instance of that section, and its original
environment to match the entire A footprint. B is translated 200m on X. The existing
demo cameras are disabled in this scene, and only Demo 1's Post Processing placeholder
is retained. It did not contain a valid URP Volume; no new visual effects were added.

One closed B connector door was opened 100 degrees and its stain decal attached to
the panel. Its original meshes/materials/collider components are retained. The player
capsule radius is 0.3m instead of 0.5m to fit that narrow authored doorway; height,
movement scripts and audio are retained. Both changes are specific to this new scene.

Both demo scenes' on-disk files were verified unchanged. Current pre-integration
states, including unsaved Demo 1 state, are preserved in `Assets/DontLookBack/Backups`.
The on-disk originals were also copied to `IntegrationBackups` before integration.

## Look-back behavior

Corridor Loop System holds the editable CorridorLoopManager references. Each start
point is a ground-level player pivot whose +Z is the corridor's forward direction.
The start points are also the local-space frames used for relative transfers.

- Face forward to arm detection. Hold a backward turn of at least 140 degrees for
  0.12s, then return within 35 degrees of forward for 0.08s.
- Looking backward alone does not teleport. Small movements, brief glances, nearly
  vertical gaze and a 0.4s post-teleport cooldown prevent false triggers.
- First completed turn returns to the current start, retaining facing direction.
- Second completed turn maps position and rotation through the current/next start
  frames with InverseTransformPoint/TransformPoint. It transfers A to B or B to A
  and resets the count to zero. The next first turn returns to that corridor's start.
- Physics clearance/floor checks reject obstructed destinations without changing
  the count or silently substituting a different relative position. Keep footprints
  compatible if editing corridor geometry or start frames.
- Rigidbody velocity is cleared; CharacterControllers are temporarily disabled and
  restored. The small FirstPersonLook synchronization API prevents accumulated yaw
  from snapping back on the next mouse update. Enable Debug Logs for count changes.

## Lighting

For the current separate main/prefab registries, darkness and invisible follower,
see [Corridor systems](CorridorSystems.md). All four environments are configured;
their runtime changes preserve the original lighting assets described below.

CorridorLightingCache retains both original color/directional/shadow-mask atlases,
UV assignments, baked lamp output and sampled static detail probe lighting. Copied
reflection probes use their original cubemaps. This is deliberate reuse of the dark
authored bakes; no textures/materials were overwritten and lamp intensity/range remain.
No original demo scene or pipeline asset was modified. If changing geometry or lamp
positions, disable the lighting cache and perform a new bake rather than expecting
cached lighting to represent the changes. Dynamic probe coverage is not newly baked.

## Verification and repeatable tools

Tools > Don't Look Back > Audit Integrated Scene checks single player/camera/listener,
both starts, centerline clearance, shaders, lighting persistence and missing scripts.
In Play mode, Run Play Mode Mechanic Checks validates A/B cycles, delayed completion,
noise rejection, Rigidbody velocity, mouse-look sync, CharacterController re-enabling
and a genuine transfer between differently rotated frames. It resets the player to
A afterward and removes its temporary fixture.

Reports: `IntegrationBackups/SceneAudit.txt`, `PlayModeChecks.txt`, `BuildReport.txt`.
DontLookBack is the first enabled build scene; existing build entries remain.
The scene builder refuses to overwrite DontLookBack. All scene integration utilities
are under `Assets/Editor`; the reusable runtime scripts are under `Scripts` here.

To revert gameplay integration, reopen either original demo scene. For the pre-build
unsaved state use the corresponding Backups scene. Remove the DontLookBack entry from
Build Settings if reverting the launch scene. The original demos, textures, prefabs
and corridor materials remain available unchanged.
