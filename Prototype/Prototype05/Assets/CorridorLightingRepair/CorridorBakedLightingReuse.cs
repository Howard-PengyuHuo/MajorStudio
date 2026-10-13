using System;
using UnityEngine;

// Reuses authored lighting for an identical, translated corridor section.
// Renderer lightmap assignments alone are not saved by Unity's scene serializer;
// this component restores them after scene loading and in player builds.
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class CorridorBakedLightingReuse : MonoBehaviour
{
    [Serializable] public struct Binding
    {
        public Renderer target;
        public Texture2D lightmap;
        public Vector4 scaleOffset;
    }
    public Binding[] bindings = Array.Empty<Binding>();
    [Serializable] public struct LightBinding
    {
        public Light target;
        public Light source;
    }
    public LightBinding[] lights = Array.Empty<LightBinding>();
    void OnEnable() => Apply();
    void Start() => Apply();
    void Update() => Apply();
    public void Apply()
    {
        var maps = LightmapSettings.lightmaps;
        foreach (var binding in bindings)
        {
            // A later proper bake takes precedence over the reuse fallback.
            if (!binding.target || binding.target.lightmapIndex >= 0 || !binding.lightmap) continue;
            for (int index = 0; index < maps.Length; index++)
                if (maps[index].lightmapColor == binding.lightmap)
                {
                    binding.target.lightmapScaleOffset = binding.scaleOffset;
                    binding.target.lightmapIndex = index;
                    break;
                }
        }
        foreach (var binding in lights)
        {
            if (!binding.target || !binding.source || LightBakeTypeUtility.GetBakeType(binding.target) != LightmapBakeType.Baked
                || binding.target.bakingOutput.isBaked || !binding.source.bakingOutput.isBaked) continue;
            // Duplicated Baked lights have no bake output and otherwise contribute
            // extra direct lighting until baked. Their contribution is already
            // contained in the original atlas reused above.
            binding.target.bakingOutput = binding.source.bakingOutput;
        }
    }
}
