using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class OceanRendererFeature : ScriptableRendererFeature
{
    sealed class OceanPass : ScriptableRenderPass
    {
        public OceanPass() { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            ScriptableRenderer renderer = data.cameraData.renderer;
            ConfigureTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
            ConfigureClear(ClearFlag.None, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            Camera camera = data.cameraData.camera;
            CommandBuffer cmd = CommandBufferPool.Get("Ocean Surface");
            try
            {
                foreach (OceanDomain domain in OceanDomain.Active)
                    if (domain != null && domain.isActiveAndEnabled
                        && (camera.cullingMask & (1 << domain.gameObject.layer)) != 0)
                        domain.Draw(cmd, camera);
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }
    }

    OceanPass pass;
    public override void Create() { pass = new OceanPass(); }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        CameraData camera = data.cameraData;
        if (OceanDomain.Active.Count > 0 && camera.renderType != CameraRenderType.Overlay
            && (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView))
            renderer.EnqueuePass(pass);
    }
}
