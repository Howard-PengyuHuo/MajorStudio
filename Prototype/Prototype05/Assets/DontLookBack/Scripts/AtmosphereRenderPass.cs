using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// Scene-owned camera pass. Scale display-referred brightness so authored dark
// environments follow the same perceptual stages as bright ones; preserve zero.
public sealed class AtmosphereRenderPass : ScriptableRenderPass
{
    readonly Material material;
    RTHandle temporary;
    public AtmosphereRenderPass(Material material)
    {
        this.material=material;
        renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;
        requiresIntermediateTexture=true;
    }
    public void Dispose() { temporary?.Release();temporary=null; }
#pragma warning disable CS0618, CS0672
    public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData renderingData)
    {
        ResetTarget();var descriptor=renderingData.cameraData.cameraTargetDescriptor;
        descriptor.depthBufferBits=0;descriptor.msaaSamples=1;
        RenderingUtils.ReAllocateHandleIfNeeded(ref temporary,descriptor,FilterMode.Bilinear,TextureWrapMode.Clamp,name:"Corridor atmosphere temporary");
    }
    public override void Execute(ScriptableRenderContext context,ref RenderingData renderingData)
    {
        var cmd=CommandBufferPool.Get("Corridor atmosphere");
        var source=renderingData.cameraData.renderer.cameraColorTargetHandle;
        Blitter.BlitCameraTexture(cmd,source,temporary,material,0);
        Blitter.BlitCameraTexture(cmd,temporary,source);
        context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
    }
#pragma warning restore CS0618, CS0672
    public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
    {
        var resources=frameData.Get<UniversalResourceData>();
        if(resources.isActiveTargetBackBuffer) return;
        var source=resources.activeColorTexture;var descriptor=graph.GetTextureDesc(source);
        descriptor.name="Corridor atmosphere color";descriptor.clearBuffer=false;
        var destination=graph.CreateTexture(descriptor);
        graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source,destination,material,0),passName:"Corridor atmosphere");
        resources.cameraColor=destination;
    }
}
