using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Exercise all three real shader draw passes on the GPU, independently of scene density.
public static class VerifyReticleRendering
{
    public static object Main()
    {
        var results = new List<object>();
        var material = new Material(Shader.Find("Sound2/Screen Grid Face And Corner Points"));
        var grid = new Texture2D(512, 512, TextureFormat.RGBA32, false);
        var pixels = new Color32[512 * 512];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 0, 0);
        grid.SetPixels32(pixels);
        grid.Apply();
        var previous = RenderTexture.active;
        try
        {
            foreach (int height in new[] { 720, 1080 })
            {
                int width = height * 16 / 9;
                var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                var read = new Texture2D(width, height, TextureFormat.RGB24, false);
                try
                {
                    for (int pass = 1; pass <= 3; pass++)
                    {
                        int unmasked = 0, masked = 0, outside = 0;
                        for (int enabled = 0; enabled <= 1; enabled++)
                        {
                            var props = new MaterialPropertyBlock();
                            props.SetTexture("_PointGrid", grid);
                            props.SetVector("_PointViewport", new Vector4(width, height, 1f / width, 1f / height));
                            props.SetVector("_PointGridSize", new Vector4(512, 512, 1f / 512, 1f / 512));
                            props.SetVector("_CrosshairClearZone", new Vector4(.5f, .5f, enabled * 14, 6));
                            props.SetFloat("_ScreenDotSpacing", 12);
                            props.SetFloat("_ScreenDotRadius", 3);
                            props.SetFloat("_BlackBoxPlayerActive", 1);
                            props.SetFloat("_StarsEnabled", 1);
                            props.SetFloat("_StarChance", 1);
                            props.SetFloat("_StarBrightness", 1);
                            props.SetFloat("_TwinkleEnabled", 0);
                            props.SetVector("_BlackBoxRevealModes", new Vector4(1, 0, 0, 0));
                            props.SetVector("_BlackBoxStyleVisibility", Vector4.one);
                            // Project all forward stars onto the reticle for a deterministic overlap test.
                            var projection = new Matrix4x4();
                            projection.m32 = 1;
                            props.SetMatrix("_StarViewProjection", projection);
                            using (var commands = new CommandBuffer())
                            {
                                commands.SetRenderTarget(rt);
                                commands.ClearRenderTarget(false, true, Color.black);
                                commands.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles,
                                    pass == 3 ? 6 : 3, pass == 3 ? 128 : 1, props);
                                Graphics.ExecuteCommandBuffer(commands);
                            }
                            RenderTexture.active = rt;
                            read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                            read.Apply();
                            var data = read.GetPixels32();
                            for (int y = height / 2 - height / 12; y < height / 2 + height / 12; y++)
                                for (int x = width / 2 - height / 12; x < width / 2 + height / 12; x++)
                                {
                                    float distance = new Vector2(x + .5f - width / 2f, y + .5f - height / 2f).magnitude * 720 / height;
                                    if (data[y * width + x].r < 10) continue;
                                    if (distance < 14) { if (enabled == 0) unmasked++; else masked++; }
                                    if (enabled == 1 && distance > 26) outside++;
                                }
                        }
                        if (unmasked == 0 || masked != 0 || (pass != 3 && outside == 0))
                            throw new InvalidOperationException($"GPU clear-zone failure: height={height}, pass={pass}, before={unmasked}, after={masked}, outside={outside}");
                        results.Add(new { height, pass, unmaskedPixels = unmasked, maskedPixels = masked, outsidePixels = outside });
                    }
                }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(read); RenderTexture.ReleaseTemporary(rt); }
            }
            return new { passed = true, results };
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(grid); UnityEngine.Object.DestroyImmediate(material); }
    }
}
