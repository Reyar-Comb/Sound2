using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public sealed partial class LevelDesignerWindow
    {
        [SerializeField] bool propColorPreview = true;
        MeshRenderer[] previewRenderers;
        readonly Dictionary<Material, Material> propPreviewMaterials = new();
        readonly List<Material> previewSlots = new();
        readonly HashSet<Material> refreshedPreviewMaterials = new();

        void InvalidatePropPreview() => previewRenderers = null;

        void DisposePropPreview()
        {
            foreach (var material in propPreviewMaterials.Values)
                if (material != null) DestroyImmediate(material);
            propPreviewMaterials.Clear();
            previewRenderers = null;
        }

        // Scene-only overlay: never replace renderer materials or change scene lighting.
        // Repaint + DrawMeshNow is the SceneView drawing path; normal depth testing
        // keeps props behind walls. The shader and temporary materials are Editor-only.
        void DrawPropColorPreview(SceneView view)
        {
            if (!propColorPreview || Event.current.type != EventType.Repaint) return;
            previewRenderers ??= layout.GetComponentsInChildren<LevelProp>(true)
                .SelectMany(p => p.GetComponentsInChildren<MeshRenderer>(true)).Distinct().ToArray();
            var planes = GeometryUtility.CalculateFrustumPlanes(view.camera);
            refreshedPreviewMaterials.Clear();
            foreach (var renderer in previewRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || SceneVisibilityManager.instance.IsHidden(renderer.gameObject)
                    || (view.camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                    || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || filter.sharedMesh.subMeshCount == 0) continue;
                renderer.GetSharedMaterials(previewSlots);
                for (int i = 0; i < previewSlots.Count; i++)
                {
                    var source = previewSlots[i];
                    if (source == null) continue;
                    if (!propPreviewMaterials.TryGetValue(source, out var material) || material == null)
                    {
                        var shader = Shader.Find("Hidden/Sound2/PropColorPreview");
                        if (shader == null) return;
                        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                        propPreviewMaterials[source] = material;
                    }
                    if (refreshedPreviewMaterials.Add(source))
                    {
                        string map = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                        material.SetColor("_Color", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                            : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
                        material.SetTexture("_MainTex", source.HasProperty(map) ? source.GetTexture(map) : null);
                        material.SetTextureScale("_MainTex", source.HasProperty(map) ? source.GetTextureScale(map) : Vector2.one);
                        material.SetTextureOffset("_MainTex", source.HasProperty(map) ? source.GetTextureOffset(map) : Vector2.zero);
                        material.SetFloat("_Cutoff", source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0
                            && source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0);
                        material.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2);
                    }
                    if (material.SetPass(0)) Graphics.DrawMeshNow(filter.sharedMesh, renderer.localToWorldMatrix,
                        Mathf.Min(i, filter.sharedMesh.subMeshCount - 1));
                }
            }
        }
    }
}
