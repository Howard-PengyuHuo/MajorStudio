# Corridor lighting repair

The original corridor is under `Assets`; the extension is under `Corridor`.
The extension contains 80 renderers and is an identical translation by (0, 0, -16.58).
The repair checked matching meshes, materials, rotations, scales and relative positions.

## Root cause

- The original has 114 renderers using baked lightmap atlas 0. All 80 extension
  renderers initially had index -1 and default lightmap UV scale/offset (1,1,0,0).
- The original four Point lights have valid bake output (`isBaked=true`). The three
  copied Point lights were set to Baked but had no bake output (`isBaked=false`),
  so they contributed extra direct lighting. Matching-view renders confirmed this:
  assigning the atlas alone did not remove the bright lamp halos.
- The two copied reflection probes had no baked cubemaps. Original probes use the
  publisher's reflection textures.
- All seven lights retain intensity 1, range 3, no shadows, Baked mode and indirect
  multiplier 1. Lights are separated, not coincident. Ambient is Flat black with
  intensity 0. No directional light exists. URP PC settings and the original
  LightingData.asset/Lighting Settings asset references remain unchanged.

## Applied changes

1. Reused each matching original surface's atlas texture and UV scale/offset for
   72 extension renderers. This copies the original dark authored bake exactly.
2. Matched the probe sample anchors of eight unlightmapped extension details to
   their originals, rather than sampling extrapolated lighting beyond the old bake.
3. Set only the two copied reflection probes to Custom and assigned the matching
   original cubemaps. Probe transforms, bounds, intensity and blend settings remain.
4. Added one `CorridorBakedLightingReuse` rendering component to `Corridor`. It
   restores the lightmap assignments and three copied lamps' bake-output state in
   Edit mode and player builds. These values otherwise do not persist reliably
   through scene serialization. A subsequent valid bake takes precedence.
5. Saved the repaired Demo 1 scene, including the user's existing extension and
   first-person controller. No existing gameplay components were edited.

No geometry, transforms, lights' authored settings, materials, textures, ambient,
URP or original lighting-data assets were changed. Protected asset hashes matched.
Original renderer lighting data was unchanged. Compilation/Console checks passed.
`OriginalReference.png` and `DuplicateMatched.png` use matching temporary camera
views and show matching darkness near the camera. Their distant geometry differs
because the extension opens into the existing corridor. Temporary cameras are removed.

## Backup and revert

`Assets/CorridorLightingRepair/BeforeLightingRepair.unity` is a Unity-loadable copy
of the scene before repair, including unsaved edits. The current scene was not
unloaded during backup. An additional copy is stored beside this README as
`BeforeLightingRepair.unity`. `OriginalOnDisk.unity` is the earlier on-disk version.

To inspect/revert: save any later changes separately, then open the BeforeLightingRepair
scene. To replace Demo 1 completely, close it first, copy the backup .unity file over
the existing Demo 1.unity file (keep Demo 1.unity.meta), refresh assets and reopen it.
This also reverts later scene edits, so use the backup for comparison if those must remain.

## Rebaking

A full rebake is not required for this exact translated-copy appearance fix. This
is deliberate reuse of the publisher's original lighting, not a new physical bake.
It preserves the original dark reference instead of recomputing it under URP.
It does not recompute indirect light or occlusion at the newly connected junction,
and the old light-probe bake does not cover the extension for moving characters.
For changed geometry, moved lamps, rotated modules or accurate dynamic-character
lighting, rebake the complete extended scene after backing it up:

1. Disable/remove the CorridorBakedLightingReuse component.
2. Clear the eight reused probe anchors (the assignments are visible in the Inspector).
3. Set the two extension reflection probes back to Baked.
4. Open Window > Rendering > Lighting. Keep the current Lighting Settings asset,
   existing lamp intensities, black ambient and Contribute GI flags. Generate Lighting.
5. Check the result against OriginalReference.png before replacing the authored bake;
   a new URP bake may differ from the pack's original bake. Back up the lighting-data
   folder first, since Generate Lighting may overwrite its lightmap/probe assets.

The Editor inspection tool is under Tools > Corridor Lighting > Inspect.
`Before.json`, `After.json`, `Inspection.json`, `Changes.txt` and `ProtectedFiles.json`
record the changes. The repair menu is project-specific and refuses to overwrite its
existing backup or repair an already configured Corridor.
