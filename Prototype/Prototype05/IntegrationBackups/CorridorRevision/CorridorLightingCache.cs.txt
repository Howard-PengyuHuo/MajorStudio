using System;
using UnityEngine;
using UnityEngine.Rendering;

// Keeps both scenes' authored atlases and probe lighting in the integrated scene.
[ExecuteAlways]
[DefaultExecutionOrder(-500)]
public sealed class CorridorLightingCache : MonoBehaviour
{
    [Serializable] public struct Atlas { public Texture2D color, direction, shadowMask; }
    [Serializable] public struct Surface { public Renderer renderer; public int atlas; public Vector4 scaleOffset; }
    [Serializable] public struct Lamp { public Light light; public int probeOcclusionLightIndex, occlusionMaskChannel; public LightmapBakeType bakeType; public MixedLightingMode mixedMode; public bool isBaked; }
    [Serializable] public struct Probe { public Renderer renderer; public float[] coefficients; }
    public Atlas[] atlases = Array.Empty<Atlas>();
    public Surface[] surfaces = Array.Empty<Surface>();
    public Lamp[] lamps = Array.Empty<Lamp>();
    public Probe[] probes = Array.Empty<Probe>();
    void OnEnable() => Apply();
    void Start() => Apply();
    void LateUpdate()
    {
        var maps = LightmapSettings.lightmaps;
        foreach (var surface in surfaces)
            if (surface.renderer && (surface.renderer.lightmapIndex < 0 || surface.renderer.lightmapIndex >= maps.Length
                || maps[surface.renderer.lightmapIndex].lightmapColor != atlases[surface.atlas].color))
            { Apply(); break; }
    }
    public void Apply()
    {
        var maps = new System.Collections.Generic.List<LightmapData>(LightmapSettings.lightmaps);
        var indices = new int[atlases.Length];
        for (int i = 0; i < atlases.Length; i++)
        {
            int index = maps.FindIndex(m => m.lightmapColor == atlases[i].color);
            if (index < 0) { index = maps.Count; maps.Add(new LightmapData { lightmapColor = atlases[i].color, lightmapDir = atlases[i].direction, shadowMask = atlases[i].shadowMask }); }
            indices[i] = index;
        }
        if (maps.Count != LightmapSettings.lightmaps.Length) LightmapSettings.lightmaps = maps.ToArray();
        foreach (var surface in surfaces) if (surface.renderer && surface.atlas >= 0 && surface.atlas < indices.Length)
        {
            surface.renderer.lightmapIndex = indices[surface.atlas];
            // Static batches already contain the UV scale/offset baked in Edit mode.
            if (!surface.renderer.isPartOfStaticBatch) surface.renderer.lightmapScaleOffset = surface.scaleOffset;
        }
        foreach (var lamp in lamps) if (lamp.light) lamp.light.bakingOutput = new LightBakingOutput {
            isBaked = lamp.isBaked, lightmapBakeType = lamp.bakeType, mixedLightingMode = lamp.mixedMode,
            probeOcclusionLightIndex = lamp.probeOcclusionLightIndex, occlusionMaskChannel = lamp.occlusionMaskChannel };
        foreach (var probe in probes)
        {
            if (!probe.renderer || probe.coefficients == null || probe.coefficients.Length != 27) continue;
            var sh = new SphericalHarmonicsL2();
            for (int rgb = 0; rgb < 3; rgb++) for (int k = 0; k < 9; k++) sh[rgb, k] = probe.coefficients[rgb * 9 + k];
            var block = new MaterialPropertyBlock();
            probe.renderer.GetPropertyBlock(block);
            block.CopySHCoefficientArraysFrom(new[] { sh });
            block.CopyProbeOcclusionArrayFrom(new[] { Vector4.one });
            probe.renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            probe.renderer.SetPropertyBlock(block);
        }
    }
}
