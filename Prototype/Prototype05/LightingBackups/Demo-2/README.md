# Demo 2 corridor lighting repair

Repaired the currently open Demo 2 scene and its Corridor2 extension. The Demo 1
repair and its backups were retained separately.

## Cause

All 163 Corridor2 renderers lacked baked-lightmap assignments (index -1, UV scale
and offset 1,1,0,0). Original geometry uses atlas 0. The three copied Point lamps
were set to Baked but their actual LightBakingOutput.isBaked was false; originals
were true. The copies therefore added direct light instead of relying on the
authored bake. Two copied reflection probes also had no baked cubemap.

All seven lights match the originals: intensity 1, range 3, soft shadows, Baked
mode. Lamp spacing and positions showed no coincident light duplication. Ambient
is Flat black, intensity 0; there is no directional light. URP PC settings, the
Demo 2 LightingData.asset and shared Lighting Settings references were retained.

## Changes

- Restored original atlas/UV assignments for 104 matching translated surfaces.
- Matched original probe sample locations for 59 details. Of these, 49 are moved
  cable decorations or a detached support, switched to LightProbes GI because
  their positions differ from the original's baked geometry. Their own transforms
  were not changed. Ten other unlightmapped details retain their original GI mode.
- Assigned the original two cubemaps to the copied reflection probes in Custom mode.
- Matched the three copied lamps' bake-output state to the originals, removing
  their extra direct-light contribution without changing intensity or range.
- Added one CorridorBakedLightingReuse rendering component on Corridor2 to restore
  lightmap assignments and lamp bake states after loading, including player builds.
- Saved Demo 2, preserving its pre-existing unsaved extension edits.

The Editor utility now uses separate output/backup paths for each scene and checks
mesh, material, rotation, scale and translated position when matching surfaces.
Different corner/support names are resolved by geometry rather than names alone.

## Verification

Original renderer data unchanged; original transforms/colliders unchanged;
protected material, texture, baked-lighting and project-settings hashes unchanged.
Console/compilation checks found no errors after the repair.
OriginalReference.png and DuplicateMatched.png use identical temporary-camera
settings and corresponding positions and show matching darkness on the main
surfaces. Distant layouts and repositioned cables naturally differ. The temporary
cameras were removed. Inspection.json, Before.json, After.json and Changes.txt
contain the actual light, renderer and probe measurements.

## Revert

Assets/CorridorLightingRepair/BeforeLightingRepair-Demo-2.unity is a loadable scene
backup containing unsaved edits before this repair. A copy is stored beside this
README as BeforeLightingRepair.unity. OriginalOnDisk.unity is the earlier disk
version. Save any subsequent edits separately before reverting. To restore the
original scene fully, close Demo 2, replace only its .unity file with the backup,
keep Demo 2.unity.meta, refresh Unity assets and reopen Demo 2.

## Rebaking

No new bake was performed: the fix reuses the original dark lighting as your
visual reference. This is an appearance-preserving approximation for the moved
decorations and new junction, not a newly computed physical bake. A complete bake
is needed for exact occlusion at changed geometry and fresh probe coverage for
moving characters in the extension.

If you choose to rebake: back up the Demo 2 baked-lighting folder and scene first;
disable/remove CorridorBakedLightingReuse, clear the reused renderer probe anchors,
set the copied reflection probes to Baked, and set the 49 probe-only decorations
back to Lightmaps GI if they should be baked. Use Window > Rendering > Lighting >
Generate Lighting with the existing Lighting Settings. Keep existing ambient and
lamp intensities. Compare against OriginalReference.png because a fresh URP bake
can differ from the pack's original bake.

To inspect again: Tools > Corridor Lighting > Inspect.
