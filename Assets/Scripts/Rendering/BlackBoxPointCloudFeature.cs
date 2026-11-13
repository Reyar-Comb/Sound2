using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// One small texel per screen-grid point stores face/edge/star brightness.
// Independent composition passes draw whole circles, never wall-clipped fragments.
public sealed class BlackBoxPointCloudFeature : ScriptableRendererFeature
{
    public Material settings;
    public Shader pointShader;
    private Material material;
    private PointPass pass;

    public override void Create()
    {
        CoreUtils.Destroy(material);
        if (pointShader != null) material = CoreUtils.CreateEngineMaterial(pointShader);
        pass = new PointPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null || settings == null || renderingData.cameraData.cameraType == CameraType.Preview
            || renderingData.cameraData.cameraType == CameraType.Reflection) return;
        material.CopyPropertiesFromMaterial(settings);
        pass.material = material;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

    private sealed class PointPass : ScriptableRenderPass
    {
        public Material material;
        private sealed class Data
        {
            public Material material;
            public TextureHandle grid;
            public TextureHandle depth;
            public int index;
            public float animationTime;
            public Matrix4x4 starViewProjection;
            public Vector4 viewport, gridSize;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            if (!resources.cameraDepthTexture.IsValid()) return;
            int width = camera.cameraTargetDescriptor.width, height = camera.cameraTargetDescriptor.height;
            float pitch = Mathf.Max(1, material.GetFloat("_ScreenDotSpacing")) * height / 720f;
            int columns = Mathf.CeilToInt(width / pitch) + 1, rows = Mathf.CeilToInt(height / pitch) + 1;
            var grid = graph.CreateTexture(new TextureDesc(columns, rows)
            {
                name = "BlackBox point decisions", colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                filterMode = FilterMode.Point, clearBuffer = false
            });
            for (int index = 0; index < 4; index++)
            {
                string name = index == 0 ? "BlackBox sample grid" : index == 1 ? "BlackBox face dots" :
                    index == 2 ? "BlackBox corner dots" : "BlackBox ambient stars";
                using (var builder = graph.AddRasterRenderPass<Data>(name, out var data))
                {
                    data.material = material;
                    data.grid = index == 0 ? TextureHandle.nullHandle : grid;
                    data.depth = index == 0 ? resources.cameraDepthTexture : TextureHandle.nullHandle;
                    data.index = index;
                    data.animationTime = Time.time;
                    data.starViewProjection = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(),
                        !resources.isActiveTargetBackBuffer) * camera.GetViewMatrix();
                    data.viewport = new Vector4(width, height, 1f / width, 1f / height);
                    data.gridSize = new Vector4(columns, rows, 1f / columns, 1f / rows);
                    if (index == 0) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    else builder.UseTexture(grid, AccessFlags.Read);
                    builder.SetRenderAttachment(index == 0 ? grid : resources.activeColorTexture, 0,
                        index == 0 ? AccessFlags.Write : AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                    {
                        var properties = new MaterialPropertyBlock();
                        properties.SetVector("_PointViewport", d.viewport);
                        properties.SetVector("_PointGridSize", d.gridSize);
                        properties.SetFloat("_PointAnimationTime", d.animationTime);
                        properties.SetMatrix("_StarViewProjection", d.starViewProjection);
                        if (d.grid.IsValid()) properties.SetTexture("_PointGrid", (RTHandle)d.grid);
                        if (d.depth.IsValid()) properties.SetTexture("_PointDepth", (RTHandle)d.depth);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.index,
                            MeshTopology.Triangles, d.index == 3 ? 6 : 3, d.index == 3 ? 128 : 1, properties);
                    });
                }
            }
        }
    }
}
