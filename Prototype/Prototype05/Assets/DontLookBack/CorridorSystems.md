# Current corridor systems

> Black Door routing now alternates Main and Prefab categories. See [Alternating Black Doors](AlternatingBlackDoors.md) for current routing, references, and validation. The original systems and prior validation below provide background.

Open `Assets/DontLookBack/DontLookBack.unity`. All current scene references and audio
clips are assigned. The user-added prefab instances and their original world transforms
are preserved. No original demo scene, third-party prefab, material, texture, lightmap,
render pipeline asset, player movement script or camera control script was overwritten.

## Four distinct environments

| Environment in scene | Role | Audio profile | Door exits |
| --- | --- | --- | --- |
| Corridor_A | Main CorridorA, first/second look-back loop | Main A - Regular | Main doors are entrances, not random destinations |
| Corridor_B | Main CorridorB, first/second look-back loop | Main B - Independent | Main doors are entrances, not random destinations |
| CorridorA_Prefabs | Independent loaded instance of the pack's Corridor prefab | Prefab A - Echo | 14 |
| CorridorB_Prefabs | Independent loaded instance of the pack's Corridor2 prefab | Prefab B - Mismatch | 14 |

Identity comes from explicit component references and the `EnvironmentKind` field,
never name similarity. The main roots retain their existing underscore names.
`CorridorLoopManager.corridors` contains exactly the two main roots and their own starts.
First completed back/forward turn returns to that main corridor's start; second maps
position and rotation through the source/destination local frames. Angle hysteresis,
physics clearance and mouse-look synchronization remain intact. Unknown environments
cannot be appended to this loop. In prefab environments `CurrentCorridor` is -1,
`CurrentEnvironment` holds the actual instance, and look-back teleportation is disabled.

## Black doors: independent registry

The existing `CorridorManager` remains the only interaction/input/fade/physics-lock owner.
The new `BlackDoorTransitionManager`, on **Corridor Loop System**, owns only the separate
weighted destination registry. Each entry has its own instance, optional prefab asset,
group weight and explicit door-exit list on `CorridorDestination`. Default group weights
are 1:1; exits have weight 1 each. Zero weight excludes an entry. Invalid, inactive,
missing, wrong-role or obstructed exits are rejected. Selection first draws a valid
group by weight, then an exit within that group by weight. It does not prefer a different
group: consecutive visits to the same group are valid.

Every black-door arrival uses a `DoorExitPoint`, never the general `SpawnPoint`.
The points sit on the corridor side, face away from their door into the corridor, and
are checked against the actual player capsule and floor. The destination's existing
opening animation runs while the screen is black, so the player emerges with the door
open behind them. The screen then fades back. Arrival cooldown, overlap locks and
entrance-side crossing checks prevent immediate loops. Opening a door alone does not
transition. Existing main-door animations and E/crosshair interaction are preserved;
28 prefab doors were configured with the same opaque unlit black effect and triggers.
All 28 have valid exits; none were excluded. Individual paths are listed in
`IntegrationBackups/CorridorRevision/Configured.txt`.

Loaded instances are preferred. Optional self-contained assets were created at:

- `Assets/DontLookBack/Prefabs/CorridorA_Prefabs.prefab`
- `Assets/DontLookBack/Prefabs/CorridorB_Prefabs.prefab`

They contain explicit exits, audio references, colliders and captured lighting caches.
If an instance is absent, the registry instantiates its configured asset once in its
staging area and selects only the clone's world-space exit transforms. The original
third-party assets are unchanged. The current scene uses its already loaded instances.
The exported assets are snapshots: later scene edits are not automatically applied to them.

For a new environment, use a `CorridorDestination` with kind `DoorPrefab`, assign its
own audio profile and door-exit list, and add it to the registry. Do not add it to the
main look-back array. Each exit must reference its own environment and portal, remain
outside the entrance trigger, and face corridor-side. Check clearance with the Play
checks or `DoorExitPoint.IsValid`. No general spawn fallback is used if exits are invalid.

## Noticeable global darkness

Previously Dread stopped at -0.65 EV, and repeated same-environment door visits did
not advance the counter. The revised manager advances after every successful black-door
commit, including same-group revisits. Opening, failed transitions and look-backs do
not count. Four stages are configured on **AtmosphereManager**: 1.00, 0.75, 0.55 and 0.35
perceived brightness. The final stage clamps; no further progression makes it darker.
Progress lasts throughout the play session across all four environments. A new session
starts at Stage 0; this is not a save-game system.

URP 17.0.4 is active. The scene has 26 authored Baked lights; the main corridors and
prefab instances have distinct baseline lighting. Runtime light intensity changes alone
cannot reliably dim their bakes. A camera-specific URP pass scales display-referred
brightness after post-processing, converting to/from linear color as appropriate.
This keeps already-dark scenes near the same percentage of their own baseline rather
than crushing their shadows through uniform EV changes. Exact black stays black.
There are no new Volumes, duplicate cameras, global renderer-asset edits or rebakes.
The shader is explicitly referenced in the scene for build inclusion. Compatibility
Mode was tested; a Render Graph implementation is also included.

Realtime/mixed light and ambient baseline caching remains available, but their default
stage multipliers are 1 to avoid double-darkening. The optional tint is disabled by
default. Lightmaps, material properties and baseline light intensities remain intact.
Runtime resources/settings are released/restored when the system disables.

Measured screenshot brightness ratios across the four environments were approximately
71-75%, 52-55%, and 33-35%; opened test doors account for small image-content differences.
Stage 3 captures were visually inspected: floor patterns, doorway silhouettes and
fixtures remain visible, and the existing white crosshair/prompt stays unaffected.
Monitor/headphone calibration remains an artistic tuning consideration.

## Follower timing and audibility

`FollowerAudioManager` uses one invisible 3D AudioSource. It measures grounded movement
independently of the player's clip playback and requires **2 consecutive seconds** of
movement before starting. It continues at the profile's cadence while moving. Stopping
starts a **1-second grace period**; resuming during that period cancels the pending stop.
After the grace period the voice fades out, and subsequent movement starts a new delay.
A backward glance immediately cancels following and fades the voice (0.06 seconds);
looking forward requires a new movement delay. Teleports/fades clear all transient
state and stop audio. No backlog or stacked voices is replayed.

Profile assets are under `Assets/DontLookBack/Audio`:

- Main A - Regular: Tile clips, 0.56s beat, slight echo and 0.025s timing jitter.
- Main B - Independent: Wood clips, 0.62s beat and 0.12s timing jitter.
- Prefab A - Echo: separate editable asset, Tile clips, 0.60s beat and subtle echo.
- Prefab B - Mismatch: separate editable asset, Wood clips, 0.69s beat and timing jitter.

The four assignments are independent, even where the same imported clip pool is used.
Each exposes activation delay, stop delay, interval, jitter, clips, pitch, volume, echo,
distance and lateral offsets. Existing real clips from Footsteps - Essentials are assigned.
No additional audio assets are required; empty clip lists intentionally remain silent.

Follower volume is 0.85, distance 2.2m behind movement, spatialBlend 1, linear rolloff
(min 2m/max 14m), and Doppler 0. Player walking/running/crouch source volumes are 0.55
on this scene instance only. One active AudioListener remains. No mixer or mute setting
was blocking audio; the former low profile gain/logarithmic attenuation and sparse
player-clip timing reduced its presence. Subtle echo uses a filter on the follower's
single source only. There is no visible character, attack, damage or additional mechanic.
Perceived loudness was not assessed with the user's headphones; spatial placement,
playback, gain, timing and source counts were verified in Unity.

## Verification and reversible setup

**Tools > Don't Look Back > Corridor Revision > Run Fresh Play Checks** restarts Play
and launches the suite automatically. **Run Play Checks** can also run in an already
fresh Play session. It tests both main look-back cycles and relative positions, all 28 exit clearances,
center-camera detection of prefab doors, weighted random choices, prefab instantiation
and reuse, actual trigger crossings, same-group revisits, open-door arrivals, disabled
prefab look-backs, trigger guards, all darkness stages, pure black portals under an
intensity-10000 light, all four follower profiles and their start/stop/restart timing.
It opens doors and advances atmosphere during the test; exit Play afterward.
The older black-door/follower/all-door test menus route to this updated suite when the
new registry exists. The original **Run Play Mode Mechanic Checks** still tests the
main loop, hysteresis, Rigidbody/CharacterController handling and rotated transfer frames.

Reports and stage screenshots: `IntegrationBackups/CorridorRevision/`.
**Configure Systems** reruns the scoped setup and default tuning; it is an explicit
reset of those defaults, so save custom Inspector tuning before rerunning it.
**Export Configured Prefab Groups** creates missing snapshots without overwriting
existing exported assets. Future source-asset updates require a deliberate export/update.

The scene before this revision, including the user's unsaved prefab instances, is
`Assets/DontLookBack/Backups/BeforeCorridorRevision.unity`. Previous runtime scripts and
edited setup utilities are preserved as `.cs.txt` files in the revision backup folder.
For full rollback, restore that scene and those files together, restore the previous
AtmosphereMultiply shader from its archived previous implementation, and remove the new DoorExitPoint,
BlackDoorTransitionManager, CorridorRevisionSetup and CorridorRevisionVerifier files
and their metadata. New profile/prefab assets can be retained unused or removed.
Restore the prior source and scene together rather than combining old APIs with new
registry components. The original demo scenes remain available unchanged.

