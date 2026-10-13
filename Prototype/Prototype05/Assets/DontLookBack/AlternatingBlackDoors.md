# Alternating Black Door routes

The saved `DontLookBack` scene now alternates categories on every Black Door crossing:

| Scene environment | Category | Eligible destinations | Configured exits |
| --- | --- | --- | --- |
| Corridor_A | Main | CorridorA_Prefabs or CorridorB_Prefabs | 40 |
| Corridor_B | Main | CorridorA_Prefabs or CorridorB_Prefabs | 40 |
| CorridorA_Prefabs | Prefab | Corridor_A or Corridor_B | 14 |
| CorridorB_Prefabs | Prefab | Corridor_A or Corridor_B | 14 |

These are the actual scene names. The A prefab instance is still named
`CorridorA_Prefabs` (plural); it is the separate loaded instance referenced by the
registry, not an alias of the main A corridor. No environments were renamed or merged.
All 108 active Black Doors have valid corridor-side arrivals. The five pre-existing
inactive main connector doors remain inactive and are not destination candidates.

## Runtime changes

- `BlackDoorTransitionManager.TrySelectFrom` filters by the entrance environment's
  opposite category, then makes the existing weighted group and weighted exit draws.
  Both eligible environments have weight 1 by default; each exit also has weight 1.
  The existing `EnvironmentKind.MainLookBack` and `DoorPrefab` values identify Main
  and Prefab. Names are not used to select a destination.
- `CorridorManager` passes the actual entrance door's parent `CorridorDestination`
  to that selector. Existing E interaction, opaque black mesh, opening animations,
  fade, Rigidbody/CharacterController handling and arrival guards remain in use.
- `CorridorLoopManager.EnterDoorExit` restores the exact registered main-corridor
  index on a main arrival, or uses -1 in a prefab environment. It teleports to the
  selected exit transform and resets the look-back counter. It never substitutes
  the main StartPoint for a Black Door arrival.

The selector still reuses loaded instances, with the existing self-contained prefab
assets available as fallback for prefab entries. It never instantiates a new main
corridor from an asset: main destinations must match the two existing loop references.
If the opposite category has no valid weighted exits, the transition is rejected;
it does not fall back to a same-category environment or general SpawnPoint.

The destination door opens during the black screen, with its moving-panel colliders
released using the existing behavior. Exits face away from the door, and both arrival
clearance and the first 0.3m forward step were checked. Existing 0.45s fade-out/fade-in,
1s arrival cooldown and overlap guards are unchanged.

## Inspector and scene changes

On **Corridor Loop System > BlackDoorTransitionManager > Groups**, the two main
`CorridorDestination` instances were added alongside the two existing prefab entries.
The main entries have instance references and no prefab asset fallback.

On **Corridor_A** and **Corridor_B > CorridorDestination > Door Exits**, 40 explicit
`DoorExitPoint` references were assigned per root. Each new point is parented under
its existing Black Door entrance frame and references that root and portal. Existing
prefab lists (14 each), categories, exported prefab assets, audio profile assignments,
lighting, meshes, colliders and environment transforms were preserved.

The main look-back array still contains exactly A/B and their own StartPoints. First
completed look-back returns to the current main start; second preserves local position
and orientation in the other main corridor. Prefab look-back teleportation stays disabled.
Every successful Black Door commit still increments the global atmosphere once, clamped
at Stage 3. Look-back operations do not increment it. Follower reset notifications and
all four audio profiles continue through the existing events; their timing/settings
were not retuned in this change.

## Verification and backup

**Tools > Don't Look Back > Alternating Doors > Run Fresh Play Checks** starts a fresh
session and exercises all eight directed main/prefab pairs through real trigger crossings.
It also checks all 108 arrivals, random category/door choices, no same-category fallback,
opening without entering, pure-black door pixels, fade/cooldown behavior, immediate
forward clearance, audio-profile/reset state, darkness counting/clamping, and both
look-back steps after returning to each main corridor. It opens doors and advances
atmosphere during testing; stop Play afterward. Older revision test menus route here
when the four-environment registry is configured.

Results: `IntegrationBackups/AlternatingDoors/PlayChecks.txt`.
Exit paths: `IntegrationBackups/AlternatingDoors/Configured.txt`.
Saved-scene audit: `IntegrationBackups/AlternatingDoors/SceneAudit.txt`.
No manual Inspector assignments are required for the current scene.

The pre-change scene is `Assets/DontLookBack/Backups/BeforeAlternatingDoors.unity`.
Prior versions of the three runtime scripts and the revised editor utility are saved
as `.cs.txt` files under `IntegrationBackups/AlternatingDoors`. Restore those together
with the backup scene for rollback; remove the new AlternatingDoorSetup and
AlternatingDoorVerifier scripts and metadata if fully reverting this revision.
Original third-party prefabs and both exported prefab corridor assets were not modified.
