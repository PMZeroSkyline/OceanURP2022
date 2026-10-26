using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class OceanRendererFeature : ScriptableRendererFeature
{
    class OceanPass : ScriptableRenderPass
    {
        public OceanPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ScriptableRenderer renderer = renderingData.cameraData.renderer;
            ConfigureTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
            ConfigureClear(ClearFlag.None, Color.clear);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            CommandBuffer cmd = CommandBufferPool.Get("Ocean Surface");

            try
            {
                foreach (OceanDomain domain in OceanDomain.Active)
                {
                    if (domain == null || !domain.isActiveAndEnabled)
                        continue;
                    int layerMask = 1 << domain.gameObject.layer;
                    if ((camera.cullingMask & layerMask) == 0)
                        continue;
                    domain.Draw(cmd, camera);
                }
                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }
    };
    OceanPass pass;
    public override void Create()
    {
        pass = new OceanPass();
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (OceanDomain.Active.Count == 0)
            return;

        CameraData cameraData = renderingData.cameraData;
        if (cameraData.renderType == CameraRenderType.Overlay)
            return;

        if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView)
            return;

        renderer.EnqueuePass(pass);
    }
}