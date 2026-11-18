using System.Collections.Generic;
using UnityEngine;

// Fade the original URP material. Sonar samples this mesh separately from the unobstructed room.
internal sealed class HeldItemFade
{
    static readonly int LodFade = Shader.PropertyToID("unity_LODFade");
    readonly Dictionary<Material, Material> variants = new();
    readonly List<(Renderer renderer, Material[] materials, MaterialPropertyBlock properties)> originals = new();
    readonly MaterialPropertyBlock properties = new();
    public float Opacity { get; private set; } = 1f;
    public readonly List<(Renderer renderer, int submeshes)> meshes = new();

    public HeldItemFade(PickupItem item)
    {
        foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh != null) meshes.Add((renderer, mesh.subMeshCount));
            var faded = new Material[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null) continue;
                if (!variants.TryGetValue(source, out faded[i]))
                {
                    faded[i] = new Material(source) { hideFlags = HideFlags.DontSave };
                    faded[i].EnableKeyword("LOD_FADE_CROSSFADE");
                    faded[i].SetFloat("_ZWrite", 0);
                    faded[i].SetShaderPassEnabled("DepthOnly", false);
                    faded[i].SetShaderPassEnabled("DepthNormals", false);
                    faded[i].SetShaderPassEnabled("DepthNormalsOnly", false);
                    if (source.renderQueue <= (int)UnityEngine.Rendering.RenderQueue.GeometryLast)
                        faded[i].renderQueue = (int)UnityEngine.Rendering.RenderQueue.GeometryLast;
                    variants.Add(source, faded[i]);
                }
            }
            var original = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(original);
            originals.Add((renderer, materials, original.isEmpty ? null : original));
            renderer.sharedMaterials = faded;
        }
    }

    public void SetOpacity(float opacity)
    {
        if (opacity == Opacity) return;
        Opacity = opacity;
        // At zero even the lowest dither texel must fail the signed LOD clip test.
        var fade = new Vector4(opacity > 0 ? opacity : -2f, 0, 0, 0);
        foreach (var entry in originals)
        {
            if (entry.renderer == null) continue;
            entry.renderer.GetPropertyBlock(properties);
            properties.SetVector(LodFade, fade);
            entry.renderer.SetPropertyBlock(properties);
        }
    }

    public void Restore()
    {
        foreach (var entry in originals)
        {
            if (entry.renderer == null) continue;
            entry.renderer.sharedMaterials = entry.materials;
            entry.renderer.SetPropertyBlock(entry.properties);
        }
        foreach (var variant in variants.Values) Object.Destroy(variant);
    }
}
