> Current destination and audio behavior is documented in [Corridor systems](CorridorSystems.md). The historical setup below predates the separate prefab-door registry.

# Universal doors in DontLookBack

Open `DontLookBack.unity` and press Play. Aim the centered white plus at a door
within three meters. The plus turns green and `[E] Open Door` appears. E opens
that panel once, using its existing Open01/Open02 animation where available, or
its own hinge otherwise. Walk into the black opening after it fully opens.
The trigger fades out, selects a valid loaded corridor (preferring another one),
arrives at its SpawnPoint facing forward, resets the count on corridor changes,
and fades back in. The original first/second look-back behavior remains.

## Inventory and preservation

There are 85 panel meshes: 43 in Corridor_A and 42 in Corridor_B. All have one
DoorInteraction and a linked portal. **80 active doors (40 per corridor)** are
playable. Five previously inactive connectors retain their inactive state and
have dormant portal roots; they do not insert darkness into the continuous route:

- A / Assets / Wall with Door A Group / Wall with Door A 09 / Door 2 B / Door
- A / Corridor / Wall with Door A 09 / Door 2 B / Door
- A / Corridor / Wall with Door A 09 (1) / Door 2 B / Door
- B / Original Environment / Wall with Door B Group / Wall with Door B 10 / Door 1 B / Door
- B / Corridor2 / Wall with Door B 10 / Door 1 B / Door

The two previous portals were reused, including `Black Door - Door 1 B - Wall with
Door B 01`. Its enclosure uses the original opaque, depth-writing, double-sided
unlit black shader. No flashlight can illuminate the void. Masks are behind the
closed panels and activate before opening exposes a gap. Entrance-side crossing
is required, and triggers cannot teleport through closed or opening doors.

One empty duplicate interaction component was removed from A's initial door.
No walls, transforms, movement settings, textures, materials, lamps, post effects,
source prefabs, original scenes, or Animator clips/controllers were changed.
Moving panel/handle meshes are excluded from static batching so their visuals can
rotate; their existing lighting references remain.

The player's capsule had been changed to radius 0.4320523m, wider than the narrow
door frames. Measuring all 80 passages at 5cm intervals found a 0.37m fitting
limit. Its radius is now 0.36m with 1cm margin. Height (1.8m), existing offset
center, movement speed and mouse-look settings are unchanged. The scene backup
includes the original capsule settings; PlayerClearance.txt records measurements.

Panel colliders stay intact and enabled in Edit mode and while closed. They are
disabled at runtime after fully opening, because some existing clips stop at
60–70 degrees and can otherwise leave the opening blocked. A capsule overlap
check also identified **28 decorative decal colliders** spanning doorways.
Only collisions between those specific decals and the player are ignored after
the corresponding door opens. Decal meshes, poses, materials, hierarchy and other
collisions remain unchanged. Previous collision exclusions are restored when the
door component is disabled. Teleport clearance checks respect these exclusions.

The crosshair has its own Screen Space Overlay canvas below the fade canvas.
Neither UI consumes raycasts or requires an EventSystem. DoorCrosshair displays
state only; CorridorManager remains the sole door input handler using existing
legacy input support. There is one active player, camera and AudioListener.

## Inspector and newly added doors

No manual wiring is required for the saved scene. On Corridor Loop System, edit
CorridorManager's camera, interaction range/key/mask, fade timings, cooldown and
destination pool. DoorCrosshair exposes prompt and idle/target colors. Door panels
expose hinge/angle/duration, Animator/state, collider-release and decorative
obstacle references. Each entrance frame's +Z points into the void.

For additional loaded corridors, add CorridorDestination with valid internal
SpawnPoint and LookBackStart references, then add it to the shared manager's pool.
In Edit mode run **Tools > Don't Look Back > Universal Doors > Configure All
Corridor Doors**. It scans nested panel meshes named Door and panels with an
explicit DoorInteraction, repairs existing components, preserves valid portals,
and records scene overrides rather than applying changes to third-party prefabs.
The same command can be repeated safely. Enable a dormant panel and rerun it if
you deliberately want that connector to become a portal.

For a differently named new mesh, add DoorInteraction to the actual moving panel
first, or use Black Doors > Configure Selected Door Panel. Review its frame's
entrance direction and available space. Future corridor prefab export remains
under Black Doors > Export Selected Corridor Prefab; it carries door setups and
lighting references and reconnects to the shared scene manager on loading.

## Checks and rollback

Universal Doors > Audit All Doors validates every panel, portal, shader, spawn,
crosshair and singleton. In Play mode, Run All Door Play Checks exercises targeting,
prompts, opening, capsule clearance and actual trigger crossings for all 80 active
panels (shorter fade timings only during this exhaustive test). It temporarily
enables background simulation for testing and restores that runtime preference.
Black Doors > Run Play Mode Checks tests fades, physics crossings, wrong-side
entries, random/missing/single destinations and arrival locks. Run Play Mode
Mechanic Checks verifies look-back loops and Rigidbody/CharacterController transfers.
Tests use temporary objects; stop Play mode to restore closed panels afterward.

The complete per-object inventory and validation reports are in
`IntegrationBackups/UniversalDoors`, with transition reports in
`IntegrationBackups/BlackDoors` and look-back results in `IntegrationBackups/PlayModeChecks.txt`.

`Assets/DontLookBack/Backups/BeforeUniversalDoors.unity` preserves the unsaved scene
as it existed before this bulk setup. To revert, save later work separately, close
the scene, replace only DontLookBack's `.unity` file with this backup, retain the
DontLookBack `.meta`, refresh and reopen. The bulk command also creates one Undo
group. The added UI script can remain unused after reverting the scene.

