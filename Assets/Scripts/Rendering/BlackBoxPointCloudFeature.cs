using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// One small texel per screen-grid point stores face/edge/star brightness.
// Independent composition passes draw whole circles, never wall-clipped fragments.
public sealed class BlackBoxPointCloudFeature : ScriptableRendererFeature
{
    [Tooltip("Material using the Screen Grid Face And Corner Points shader.")]
    public Material settings;
    private PointPass pass;

    public override void Create()
    {
        pass = new PointPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings == null || renderingData.cameraData.cameraType == CameraType.Preview
            || renderingData.cameraData.cameraType == CameraType.Reflection) return;
        var cameraSettings = settings;
        float sharedDotRadius = -1;
        if (renderingData.cameraData.camera.TryGetComponent<SphericalSonar>(out var sonar)
            && sonar.isActiveAndEnabled)
        {
            if (sonar.renderingSettings != null) cameraSettings = sonar.renderingSettings;
            // Apply the same cap to both grids so their shader clamps cannot produce different sizes.
            float tightestSpacing = Mathf.Min(Mathf.Max(1, settings.GetFloat("_ScreenDotSpacing")),
                Mathf.Max(1, cameraSettings.GetFloat("_ScreenDotSpacing")));
            sharedDotRadius = Mathf.Clamp(sonar.dotRadius, .25f, tightestSpacing * .75f);
        }
        pass.sharedDotRadius = sharedDotRadius;
        pass.material = cameraSettings;
        // Preserve the teammate's original face cloud independently of sonar contour spacing.
        pass.faceMaterial = settings;
        renderer.EnqueuePass(pass);
    }

    private sealed class PointPass : ScriptableRenderPass
    {
        public Material material;
        public Material faceMaterial;
        public float sharedDotRadius;
        private sealed class Data
        {
            public Material material;
            public TextureHandle grid;
            public TextureHandle depth;
            public int index;
            public float animationTime;
            public float sharedDotRadius;
            public Matrix4x4 starViewProjection;
            public Vector4 viewport, gridSize;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            if (!resources.cameraDepthTexture.IsValid()) return;
            bool separateFaces = faceMaterial != null && faceMaterial != material;
            if (separateFaces)
                RecordPoints(graph, resources, camera, faceMaterial, true, false, false, sharedDotRadius);
            RecordPoints(graph, resources, camera, material, !separateFaces, true, true, sharedDotRadius);
        }

        // Both groups use the original sampling and drawing passes. Only the material differs.
        static void RecordPoints(RenderGraph graph, UniversalResourceData resources, UniversalCameraData camera,
            Material drawMaterial, bool faces, bool corners, bool stars, float sharedDotRadius)
        {
            int width = camera.cameraTargetDescriptor.width, height = camera.cameraTargetDescriptor.height;
            float pitch = Mathf.Max(1, drawMaterial.GetFloat("_ScreenDotSpacing")) * height / 720f;
            int columns = Mathf.CeilToInt(width / pitch) + 1, rows = Mathf.CeilToInt(height / pitch) + 1;
            var grid = graph.CreateTexture(new TextureDesc(columns, rows)
            {
                name = faces ? "BlackBox face decisions" : "BlackBox contour decisions", colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                filterMode = FilterMode.Point, clearBuffer = false
            });
            for (int index = 0; index < 4; index++)
            {
                if ((index == 1 && !faces) || (index == 2 && !corners) || (index == 3 && !stars)) continue;
                string name = index == 0 ? "BlackBox sample grid" : index == 1 ? "BlackBox face dots" :
                    index == 2 ? "BlackBox corner dots" : "BlackBox ambient stars";
                using (var builder = graph.AddRasterRenderPass<Data>(name, out var data))
                {
                    data.material = drawMaterial;
                    data.sharedDotRadius = sharedDotRadius;
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
                        if (d.sharedDotRadius >= 0)
                            properties.SetFloat("_ScreenDotRadius", d.sharedDotRadius);
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
