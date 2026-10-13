using UnityEngine;

// The authored Light.lightmapBakeType property exists only in Editor assemblies.
// Players expose the bake mode through the runtime LightBakingOutput structure.
public static class LightBakeTypeUtility
{
    public static LightmapBakeType GetBakeType(Light light)
    {
#if UNITY_EDITOR
        return light.lightmapBakeType;
#else
        return light.bakingOutput.lightmapBakeType;
#endif
    }
}
