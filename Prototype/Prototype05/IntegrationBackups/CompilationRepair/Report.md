# Compilation repair validation

Unity: 6000.0.59f2 (ef281c76c3c1). Installed URP: 17.0.4, confirmed from
ProjectVersion.txt, packages-lock.json, package.json and the running Editor.
Active pipeline: Assets/Settings/PC_RPAsset.asset. The PC renderer/pipeline assets
were not edited.

## Changes

- Assets/CorridorLightingRepair/CorridorBakedLightingReuse.cs:44 — use LightBakeTypeUtility.GetBakeType(binding.target).
- Assets/DontLookBack/Scripts/AtmosphereManager.cs:81 — use LightBakeTypeUtility.GetBakeType(light).
- Assets/DontLookBack/Tests/AtmosphereFollowerPlayVerifier.cs:33 — repair the same Player-incompatible API in a runtime verification component.
- Assets/DontLookBack/Scripts/LightBakeTypeUtility.cs:1-15 — new conditional compatibility helper. Lines 8-10 use the authored property only under UNITY_EDITOR; lines 11-12 use runtime bakingOutput.lightmapBakeType in Players.
- Assets/Editor/CompilationRepairValidation.cs — new Editor-only inspection and Windows Player build utility. It does not save scenes or change build/pipeline settings.

Light.lightmapBakeType is Editor-only, rather than an unsupported URP feature.
It exists in Editor assemblies and disappears from the Player API surface, producing
CS1061 in unguarded runtime scripts. Light.bakingOutput and LightBakingOutput.lightmapBakeType
are available to Players. The LightmapBakeType enum remains supported.

The authored Editor setting is deliberately preserved during Editor execution.
bakingOutput describes the last bake, so an unbaked light can have authored mode Baked
but runtime bake output Realtime. The light audit identified 20 baked main lamps and
six prefab lamps with that unbaked distinction. Their lighting data and authoring modes
were not rewritten. Runtime classification uses the actual bake output. Baked lights
remain excluded from intensity multipliers; Mixed and Realtime lights retain the existing
cached-multiplier path. The current progressive darkness stages use camera brightness
scaling with light multipliers of 1, so their visuals and progression rules are unchanged.

## Complete original authoring-API inventory

The three runtime occurrences above were changed. These valid Editor-only usages remain:

- Assets/Editor/AtmosphereFollowerSetup.cs:26
- Assets/Editor/CorridorRevisionSetup.cs:218
- Assets/Editor/DontLookBackVerification.cs:66
- Assets/Editor/CorridorLightingUtility.cs:205 (source and target checks), :267

Existing uses of Light.bakingOutput.lightmapBakeType and the LightBakingOutput initializer
in CorridorLightingCache, CorridorRevisionSetup and DontLookBackSceneBuilder are already
Player-compatible and were left intact. The new validation utility's authoring API calls
are under Assets/Editor; the compatibility helper's authoring call is preprocessor-guarded.

## Results

Editor recompilation: passed, no errors or warnings.
Authored/runtime enum checks: Baked, Mixed and Realtime passed.
Windows x64 Player build: Succeeded, 0 errors and 0 warnings.
Duration: 00:02:36.0444552. Output size reported: 280643663 bytes.
Output: Builds/CompilationRepair/DontLookBack.exe (keep its neighboring Data/DLL files).
Build summary: IntegrationBackups/CompilationRepair/BuildStatus.txt.
Light audit: IntegrationBackups/CompilationRepair/LightModeValidation.txt.

The earlier UnityEditor.Graphs exception was reproduced during Editor assembly reload
with Edge.WakeUp -> Graph.WakeUpEdges -> Graph.OnEnable. It contains no project script
frame. The saved Editor layout includes Animator graph state. This points to Editor graph
initialization; an exact defective graph asset was not established from that stack.
It did not recur during the successful validation build and was not a Player compiler
error. Animation/controllers, graph assets and Editor layouts were not modified.
The Editor log also records a later separate Build And Run cancellation; that was not
the successful validation build summarized above.

No manual compilation/build verification is required for the tested Windows x64 target.
Other targets were not built. The executable was built but was not playtested in this
repair task, which intentionally leaves the gameplay systems unchanged.

Previous copies of the three edited runtime files are saved beside this report as
.cs.txt files for rollback. No corridor/door/player/audio/material/scene data was edited.
