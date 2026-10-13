> Current destination and audio behavior is documented in [Corridor systems](CorridorSystems.md). The historical setup below predates the separate prefab-door registry.

# Black doorway mechanic

Already configured in DontLookBack. Press Play, walk about 0.7m forward from the
initial spawn, look at the left-hand door, and press E within 3m. Walk through its
opening to transition. All 80 active doors are configured, plus five dormant
connector panels whose inactive state is preserved. See UniversalDoors.md for the
complete setup, crosshair, door inventory and repeatable bulk utility.

The initial doors are:

- Corridor_A / Corridor (1) / Wall with Door A 12 / Door 2 B / Door
- Corridor_B / Corridor2 Extension / Wall with Door B 01 / Door 1 B / Door

## Behavior and integration

Each opened doorway reveals an opaque enclosure using DontLookBack/Black Door Void.
Its fragment shader returns RGBA(0,0,0,1), writes depth, draws both sides, and does
not sample lighting, textures or fog. It therefore hides whatever is behind it even
under bright flashlight illumination. The mask activates before the panel moves.
A small invisible support floor prevents falling beyond the corridor's original edge.
Transitions require the panel to finish opening. The reference opens once; E does
not close or reopen it. Panel/handle colliders are disabled only after fully opening.

The entrance trigger arms only on the corridor side and starts a transition after
the player's pivot crosses 0.06m beyond the doorway plane. Its entrance frame's +Z
points into the darkness. Contact with the trigger's edge alone does not teleport.
Entries from the far side do not trigger. Fade-out takes 0.45s, a fully black frame
precedes teleportation, black hold is 0.08s, and fade-in takes 0.45s by default.

CorridorManager prefers a valid destination other than the current corridor. With
the initial A/B pool, this means A leads to B and B leads to A. Disable Prefer
Different Corridor to allow either, or add more entries for random alternatives.
A single valid corridor is supported; missing references and obstructed spawns are
skipped. Loaded instances are preferred over prefab instantiation.

The player arrives at the destination's separate SpawnPoint, facing its +Z. Movement,
mouse look and look-back detection are suspended during the fade; physics and their
previous enabled states are restored afterward. Camera pitch/yaw are synchronized.
No second player, camera, AudioListener or post-processing volume is created.
Changing corridors resets the look-back count to zero; same-corridor arrivals retain
the count. All arrivals discard partial look-back detection and apply its cooldown.
The original first/second completed-look-back behavior remains in CorridorLoopManager.

An arrival cooldown of 1s prevents repeated transitions. If an arrival overlaps a
door trigger, that door remains blocked until the player's body physically exits,
even after the cooldown. Actor freeze/unfreeze trigger callbacks cannot release it
prematurely. Failed selection never leaves the screen permanently black.

## Inspector settings

Corridor Loop System contains CorridorManager and ScreenFadeController alongside
the existing loop and lighting components. Edit interaction key/range/mask, fade
durations, cooldown, destination pool, prefab staging origin/spacing and debug logs
there. Controls To Suspend should contain player input/movement scripts only.

Corridor_A and Corridor_B each have CorridorDestination with SpawnPoint and
LookBackStart references. Keep spawn points ground-level and clear of walls.
The interactive panels have DoorInteraction: hinge, local axis, angle and duration,
plus optional existing Animator/state or trigger/bool parameter. Existing animations can be configured by
their full state name, such as Base Layer.Open01; use their clip duration. The A
initial door uses its hinge; the B reference now preserves its existing Animator.
Looping
opening clips are held at the final open pose without editing the original clip.

Only moving panels/handles have Batching Static cleared, because statically batched
meshes cannot rotate correctly at runtime. Existing GI flags, lighting, materials,
meshes and environment collider components remain. Static-batch UV offsets in the
lighting cache are no longer redundantly rewritten at runtime.

## Designating additional doors

1. In Edit mode select the actual Door panel mesh under a corridor with
   CorridorDestination, rather than the wall/frame parent.
2. Run Tools > Don't Look Back > Black Doors > Configure Selected Door Panel.
3. Inspect the generated Black Door frame, trigger and floor. +Z must point out of
   the corridor. The helper derives side-door orientation from the existing frame;
   end/internal doors use the side toward the corridor spawn. Review the intended
   entrance side for new layouts. The bulk Universal Doors utility configures all
   existing candidates, including nested prefabs, and reuses existing portals.
4. Check the hinge swing clears the opening. Assign an existing Animator and opening
   state/parameter when applicable. Keep the opacity mesh immediately behind the
   closed panel and covering the complete hole.
5. Run Audit Setup, test the opening/crossing in Play mode, then save the scene.

## Additional corridors and prefabs

For a loaded corridor, add CorridorDestination to its environment root and create
SpawnPoint/LookBackStart children. Add its marker to CorridorManager's Destinations
Instance field. Door entry registers the corridor with the look-back loop as needed.

To export an existing corridor, select its marked root and run Export Selected
Corridor Prefab. The helper saves a new prefab under Assets/DontLookBack/Prefabs,
normalizes its origin to SpawnPoint, embeds the corridor's relevant lighting-cache
bindings, and clears scene-manager references on its doors. It refuses to overwrite
an existing prefab. Assign that asset to a new destination's Prefab field, leaving
Instance empty. The prefab must have a root CorridorDestination and internal spawn
references, and must contain no player, gameplay camera or AudioListener. It is
instantiated once at the configured distant staging location and then reused.
Door managers reconnect at runtime. Imported prefabs should carry their own valid
URP materials and baked-lighting solution; start with loaded instances when possible.

## Verification and rollback

Tools > Don't Look Back > Black Doors > Audit Setup validates wiring and shaders.
In Play mode, Run Play Mode Checks uses actual physics crossings and ray interactions,
bright-light renders, destination selection and a temporary prefab fixture. It resets
the player afterward and removes temporary objects. Existing look-back checks remain
under Tools > Don't Look Back > Run Play Mode Mechanic Checks.

Reports and render captures are in IntegrationBackups/BlackDoors. The pre-mechanic
scene, including its unsaved state, is Assets/DontLookBack/Backups/BeforeBlackDoors.unity;
the earlier on-disk scene and loop source are also backed up in IntegrationBackups/BlackDoors.
To revert the scene, save later edits separately, close DontLookBack, replace only its
.unity file with the backup, keep its .meta, refresh assets and reopen. Restoring that
scene removes the new components/objects while preserving the pre-existing prototype.
The added scripts can remain unused; the loop's additional public APIs are inert when
no door manager is present. Original demo scenes and original texture/material assets
were not edited by this mechanic.

