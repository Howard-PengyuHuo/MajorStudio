# Corridor material repair

The active pipeline is URP (Unity 6000.0.59f2, URP 17.0.4, PC quality).
All 19 imported materials used Built-in Standard, which does not render correctly in URP.
Only material contents were replaced with the publisher's matching URP/Lit variants from
`Assets/AK Studio Art/Modular Horror Corridor/URP Extract Me.unitypackage`.
The entire package was NOT imported: its demo scenes and any project settings were left alone.

Original texture references, tiling/offset, base tint/alpha, normal strength, metallic,
emission, occlusion and transparent surfaces were checked before the replacement.
Publisher URP smoothness settings were retained (metallic-map alpha multiplied by the
original GlossMapScale, rather than the standalone Glossiness value).
Material .meta files/GUIDs stayed unchanged, retaining existing scene and prefab assignments.

Concrete Wall had an optional height map reference whose GUID does not exist in the
project. This broken reference was cleared; no height texture was deleted or substituted.
The original reference remains in the backed-up material.

## Verification

Unity's read-only audit found 19 valid URP/Lit materials, 66 prefab renderers, and
129 renderers in the loaded Demo 1 scene, with zero failures. Assigned normal maps
have normal-map import settings. A Scene view render confirmed textured surfaces
without magenta. See `UnityVerification.txt` and `SceneViewAfter.png`.
SHA-256 checks against `manifest.json` confirmed all original non-material files
under Assets, Packages and ProjectSettings were unchanged, including textures,
material GUIDs, scenes, prefabs, transforms, lights, colliders and gameplay scripts.

To rerun the audit in Unity: **Tools > Corridor Materials > Verify URP Repair**.
The added Editor script only audits assets and optionally captures the Scene view.
It does not modify scene objects or material assignments.

## Revert

From the project directory in PowerShell:

```powershell
& ./Tools/Repair-CorridorMaterials.ps1 -Restore
```

Then use **Assets > Refresh** in Unity. This copies only the original 19 material
files from this backup into their original paths. No textures or scenes are changed.
Reverting restores the original Built-in shaders, so the original pink rendering
will return while this project uses URP. The backup is never overwritten by the
repair script.
