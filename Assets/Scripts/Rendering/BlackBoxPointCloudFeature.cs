using System.Collections.Generic;
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
    static readonly System.Collections.Generic.Dictionary<ScriptableRenderer, BlackBoxPointCloudFeature> activeRenderers = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRenderers() => activeRenderers.Clear();

    public static float GetDotSpacing(Camera camera,float fallback=5.5f)
    {
        if(camera!=null && camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)
            && activeRenderers.TryGetValue(data.scriptableRenderer,out var feature)
            && feature!=null && feature.settings!=null)return Mathf.Max(1,feature.settings.GetFloat("_ScreenDotSpacing"));
        return fallback;
    }

    public static float GetDotDiameter(Camera camera, float fallback = 3)
    {
        if (camera == null || !camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) return fallback;
        // Renderer features own the authoritative radius/spacing, including the non-sonar path.
        if (activeRenderers.TryGetValue(data.scriptableRenderer, out var points) && points != null && points.isActive && points.settings != null)
            {
                Material alternate = points.settings;
                if (camera.TryGetComponent<SphericalSonar>(out var sonar) && sonar.isActiveAndEnabled)
                {
                    if (sonar.renderingSettings != null) alternate = sonar.renderingSettings;
                    float pitch = Mathf.Min(Mathf.Max(1, points.settings.GetFloat("_ScreenDotSpacing")), Mathf.Max(1, alternate.GetFloat("_ScreenDotSpacing")));
                    return 2 * Mathf.Clamp(sonar.dotRadius, .25f, pitch * .75f);
                }
                return 2 * Mathf.Clamp(alternate.GetFloat("_ScreenDotRadius"), 0, Mathf.Max(1, alternate.GetFloat("_ScreenDotSpacing")) * .75f);
            }
        return fallback;
    }

    public override void Create()
    {
        pass = new PointPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        activeRenderers[renderer] = this;
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
            public TextureHandle grid, dangerGrid;
            public TextureHandle depth;
            public int index, palette;
            public float animationTime;
            public float sharedDotRadius;
            public Matrix4x4 starViewProjection;
            public Vector4 viewport, gridSize, crosshairClearZone;
            public int uiCount;
            public Vector4[] uiRects, uiOptions;
            public int coinCount;
            public Matrix4x4[] coinSpaces;
            public Vector4[] coinWaves;
            public Vector4 cloudCenter,cloudParameters;
            public int pointCount;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            if (!resources.cameraDepthTexture.IsValid()) return;
            var dangerGroups = new List<Data>();
            bool separateFaces = faceMaterial != null && faceMaterial != material;
            var uiRects = new Vector4[PlayerUiPointMask.Capacity];
            var uiOptions = new Vector4[PlayerUiPointMask.Capacity];
            int uiCount = PlayerUiPointMask.Collect(camera.camera, uiRects, uiOptions);
            var coinSpaces = new Matrix4x4[CoinPickup.SurfaceCapacity];
            var coinWaves = new Vector4[CoinPickup.SurfaceCapacity];
            int coinCount = CoinPickup.CollectSurfaceWaves(camera.camera, coinSpaces, coinWaves);
            if (separateFaces)
                RecordPoints(graph, resources, camera, faceMaterial, true, false, false, sharedDotRadius, dangerGroups, uiCount, uiRects, uiOptions, coinCount, coinSpaces, coinWaves);
            RecordPoints(graph, resources, camera, material, !separateFaces, true, true, sharedDotRadius, dangerGroups, uiCount, uiRects, uiOptions, coinCount, coinSpaces, coinWaves);
            // Clear each palette before drawing to preserve color identity at overlaps and AA edges.
            int colors = SonarPulseField.ActiveColoredPalettes;
            for (int palette = 3; palette >= 1; palette--)
            {
                if ((colors & (1 << palette)) == 0) continue;
                foreach (var group in dangerGroups) RecordDanger(graph, resources, group, true, palette);
                foreach (var group in dangerGroups) RecordDanger(graph, resources, group, false, palette);
            }
            RecordLightningClouds(graph,resources,camera,faceMaterial!=null?faceMaterial:material,sharedDotRadius,uiCount,uiRects,uiOptions);
        }

        static void RecordLightningClouds(RenderGraph graph,UniversalResourceData resources,UniversalCameraData camera,
            Material drawMaterial,float radius,int uiCount,Vector4[] uiRects,Vector4[] uiOptions)
        {
            var interaction=camera.camera.GetComponentInParent<PlayerInteraction>();
            var clearZone=interaction!=null && interaction.viewCamera==camera.camera?interaction.PointClearZone:Vector4.zero;
            int width=camera.cameraTargetDescriptor.width,height=camera.cameraTargetDescriptor.height;
            var planes=GeometryUtility.CalculateFrustumPlanes(camera.camera);
            for(int phase=0;phase<2;phase++)foreach(var cloud in BallLightning.Instances)
            {
                if(cloud==null || !cloud.isActiveAndEnabled || (camera.camera.cullingMask & (1<<cloud.gameObject.layer))==0
                    || !GeometryUtility.TestPlanesAABB(planes,new Bounds(cloud.Center,Vector3.one*cloud.CloudRadius*2)))continue;
                using var builder=graph.AddRasterRenderPass<Data>(phase==0?"Clear lightning volume dots":"Lightning volume point cloud",out var data);
                data.material=drawMaterial;data.depth=resources.cameraDepthTexture;data.index=phase==0?8:9;
                data.pointCount=Mathf.Clamp(cloud.cloudPointCount,64,1024);
                var c=cloud.Center;data.cloudCenter=new Vector4(c.x,c.y,c.z,cloud.CloudRadius);
                data.cloudParameters=new Vector4(cloud.CloudPhase,cloud.State==LightningState.Active?1:.65f,data.pointCount,0);
                data.sharedDotRadius=radius>=0?radius:drawMaterial.GetFloat("_ScreenDotRadius");
                data.animationTime=Time.time;data.viewport=new Vector4(width,height,1f/width,1f/height);
                data.starViewProjection=GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(),!resources.isActiveTargetBackBuffer)*camera.GetViewMatrix();
                data.crosshairClearZone=clearZone;data.uiCount=uiCount;data.uiRects=uiRects;data.uiOptions=uiOptions;
                builder.UseTexture(resources.cameraDepthTexture,AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (Data d,RasterGraphContext context)=>{
                    var properties=new MaterialPropertyBlock();
                    properties.SetTexture("_PointDepth",(RTHandle)d.depth);
                    properties.SetVector("_PointViewport",d.viewport);properties.SetMatrix("_StarViewProjection",d.starViewProjection);
                    properties.SetFloat("_PointAnimationTime",d.animationTime);properties.SetFloat("_ScreenDotRadius",d.sharedDotRadius);
                    properties.SetVector("_LightningCloudCenter",d.cloudCenter);properties.SetVector("_LightningCloudParameters",d.cloudParameters);
                    properties.SetVector("_CrosshairClearZone",d.crosshairClearZone);properties.SetInt("_PlayerUiCount",d.uiCount);
                    properties.SetVectorArray("_PlayerUiRects",d.uiRects);properties.SetVectorArray("_PlayerUiOptions",d.uiOptions);
                    context.cmd.DrawProcedural(Matrix4x4.identity,d.material,d.index,MeshTopology.Triangles,6,d.pointCount,properties);
                });
            }
        }

        // Both groups use the original sampling and drawing passes. Only the material differs.
        static void RecordPoints(RenderGraph graph, UniversalResourceData resources, UniversalCameraData camera,
            Material drawMaterial, bool faces, bool corners, bool stars, float sharedDotRadius, List<Data> dangerGroups,
            int uiCount, Vector4[] uiRects, Vector4[] uiOptions, int coinCount, Matrix4x4[] coinSpaces, Vector4[] coinWaves)
        {
            int width = camera.cameraTargetDescriptor.width, height = camera.cameraTargetDescriptor.height;
            var interaction = camera.camera.GetComponentInParent<PlayerInteraction>();
            Vector4 clearZone = interaction != null && interaction.viewCamera == camera.camera
                ? interaction.PointClearZone : Vector4.zero;
            float pitch = Mathf.Max(1, drawMaterial.GetFloat("_ScreenDotSpacing")) * height / 720f;
            int columns = Mathf.CeilToInt(width / pitch) + 1, rows = Mathf.CeilToInt(height / pitch) + 1;
            var grid = graph.CreateTexture(new TextureDesc(columns, rows)
            {
                name = faces ? "BlackBox face decisions" : "BlackBox contour decisions", colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                filterMode = FilterMode.Point, clearBuffer = false
            });
            var dangerGrid = graph.CreateTexture(new TextureDesc(columns, rows) {
                name = "BlackBox danger decisions", colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
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
                    data.uiCount = uiCount;
                    data.uiRects = uiRects;
                    data.uiOptions = uiOptions;
                    data.coinCount = coinCount;
                    data.coinSpaces = coinSpaces;
                    data.coinWaves = coinWaves;
                    data.sharedDotRadius = sharedDotRadius;
                    data.grid = index == 0 ? TextureHandle.nullHandle : grid;
                    data.dangerGrid = index == 0 ? TextureHandle.nullHandle : dangerGrid;
                    data.depth = index == 0 ? resources.cameraDepthTexture : TextureHandle.nullHandle;
                    data.index = index;
                    data.animationTime = Time.time;
                    data.starViewProjection = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(),
                        !resources.isActiveTargetBackBuffer) * camera.GetViewMatrix();
                    data.viewport = new Vector4(width, height, 1f / width, 1f / height);
                    data.crosshairClearZone = clearZone;
                    data.gridSize = new Vector4(columns, rows, 1f / columns, 1f / rows);
                    if (index == 0) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    else { builder.UseTexture(grid, AccessFlags.Read); builder.UseTexture(dangerGrid, AccessFlags.Read); }
                    builder.SetRenderAttachment(index == 0 ? grid : resources.activeColorTexture, 0,
                        index == 0 ? AccessFlags.Write : AccessFlags.ReadWrite);
                    if (index == 0) builder.SetRenderAttachment(dangerGrid, 1, AccessFlags.Write);
                    if (index == 1 || index == 2)
                        dangerGroups.Add(new Data { material = data.material, dangerGrid = dangerGrid,
                            index = index + 3, sharedDotRadius = sharedDotRadius, viewport = data.viewport,
                            gridSize = data.gridSize, crosshairClearZone = clearZone });
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                    {
                        var properties = new MaterialPropertyBlock();
                        properties.SetVector("_PointViewport", d.viewport);
                        properties.SetVector("_CrosshairClearZone", d.crosshairClearZone);
                        properties.SetInt("_PlayerUiCount", d.uiCount);
                        properties.SetVectorArray("_PlayerUiRects", d.uiRects);
                        properties.SetVectorArray("_PlayerUiOptions", d.uiOptions);
                        if(d.index==0)
                        {
                            properties.SetInt("_CoinSurfaceCount",d.coinCount);
                            properties.SetMatrixArray("_CoinSurfaceSpaces",d.coinSpaces);
                            properties.SetVectorArray("_CoinSurfaceWaves",d.coinWaves);
                        }
                        properties.SetVector("_PointGridSize", d.gridSize);
                        properties.SetFloat("_PointAnimationTime", d.animationTime);
                        if (d.sharedDotRadius >= 0)
                            properties.SetFloat("_ScreenDotRadius", d.sharedDotRadius);
                        properties.SetMatrix("_StarViewProjection", d.starViewProjection);
                        if (d.grid.IsValid()) properties.SetTexture("_PointGrid", (RTHandle)d.grid);
                        if (d.dangerGrid.IsValid()) properties.SetTexture("_DangerPointGrid", (RTHandle)d.dangerGrid);
                        if (d.depth.IsValid()) properties.SetTexture("_PointDepth", (RTHandle)d.depth);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.index,
                            MeshTopology.Triangles, d.index == 3 ? 6 : 3, d.index == 3 ? 128 : 1, properties);
                    });
                }
            }
        }
        static void RecordDanger(RenderGraph graph, UniversalResourceData resources, Data input, bool clear = false, int palette = 1)
        {
            using var builder = graph.AddRasterRenderPass<Data>("BlackBox danger dots", out var data);
            data.palette=palette; data.material=input.material; data.dangerGrid=input.dangerGrid; data.index=input.index + (clear ? 2 : 0);
            data.sharedDotRadius=input.sharedDotRadius; data.viewport=input.viewport;
            data.gridSize=input.gridSize; data.crosshairClearZone=input.crosshairClearZone;
            builder.UseTexture(data.dangerGrid, AccessFlags.Read);
            builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
            builder.SetRenderFunc(static (Data d, RasterGraphContext context) => {
                var properties = new MaterialPropertyBlock();
                properties.SetVector("_PointViewport",d.viewport); properties.SetVector("_PointGridSize",d.gridSize);
                properties.SetVector("_CrosshairClearZone",d.crosshairClearZone);
                if(d.sharedDotRadius>=0) properties.SetFloat("_ScreenDotRadius",d.sharedDotRadius);
                properties.SetTexture("_DangerPointGrid",(RTHandle)d.dangerGrid);
                properties.SetFloat("_PulsePalette",d.palette);
                properties.SetColor("_PulseColor",d.palette==1?Color.red:d.palette==2?Color.blue:Color.white);
                context.cmd.DrawProcedural(Matrix4x4.identity,d.material,d.index,MeshTopology.Triangles,3,1,properties);
            });
        }
    }
}
