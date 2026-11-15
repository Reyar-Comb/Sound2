using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Sound2.LevelEditing.Editor
{
    // Bake once per source prefab, never once per placed instance or on entering Play mode.
    public static class LevelPropOptimizer
    {
        public const string Folder = "Assets/LevelEditor/OptimizedProps";

        static string BakedFolder(GameObject source, string sourcePath) => Folder + "/" + source.name + "_"
            + AssetDatabase.AssetPathToGUID(sourcePath).Substring(0, 8) + "_" + AssetDatabase.GetAssetDependencyHash(sourcePath);

        // New placements reuse an existing, current bake; changes to the original invalidate this lookup.
        public static GameObject ForPlacement(GameObject source)
        {
            string path = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(path) || path.StartsWith(Folder + "/", StringComparison.Ordinal)) return source;
            return AssetDatabase.LoadAssetAtPath<GameObject>(BakedFolder(source, path) + "/" + source.name + ".prefab") ?? source;
        }

        public static string Analyze(LevelLayout layout)
        {
            var props = layout.GetComponentsInChildren<LevelProp>(true);
            return string.Join("\n", props.GroupBy(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.gameObject))
                .OrderByDescending(g => g.Sum(p => p.GetComponentsInChildren<Renderer>(true).Length))
                .Select(g => $"{Path.GetFileNameWithoutExtension(g.Key)}: {g.Count()} instances, " +
                    $"{g.Sum(p => p.GetComponentsInChildren<Renderer>(true).Length)} renderers, " +
                    $"{g.Sum(p => p.GetComponentsInChildren<Collider>(true).Length)} colliders"));
        }

        public static string Optimize(LevelLayout layout)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            var report = new List<string>();
            // References to child objects must not be silently broken by a bake.
            var referenced = new HashSet<Object>();
            foreach (var root in layout.gameObject.scene.GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component is LevelProp) continue;
                using var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                        referenced.Add(property.objectReferenceValue);
            }
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("优化静态道具");
            try
            {
                foreach (var group in layout.GetComponentsInChildren<LevelProp>(true)
                    .GroupBy(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.gameObject)).ToArray())
                {
                    string path = group.Key;
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (source == null || path.StartsWith(Folder + "/", StringComparison.Ordinal)) continue;
                    string reason = Eligibility(source);
                    if (reason != null) { report.Add($"跳过 {source.name}: {reason}"); continue; }
                    var candidates = group.Where(p => SafeInstance(p, source, referenced)).Select(p => p.gameObject).ToArray();
                    if (candidates.Length == 0) { report.Add($"跳过 {source.name}: 实例含子对象覆盖、引用或动态父级"); continue; }
                    var baked = Bake(source, path);
                    PrefabUtility.ReplacePrefabAssetOfPrefabInstances(candidates, baked, new PrefabReplacingSettings {
                        objectMatchMode = ObjectMatchMode.ByHierarchy,
                        prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides,
                        changeRootNameToAssetName = false, logInfo = false
                    }, InteractionMode.UserAction);
                    report.Add($"已优化 {source.name} × {candidates.Length}（跳过 {group.Count() - candidates.Length}）");
                }
                EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            }
            finally { Undo.CollapseUndoOperations(undo); }
            return string.Join("\n", report);
        }

        static bool SafeInstance(LevelProp prop, GameObject source, HashSet<Object> referenced)
        {
            var root = prop.gameObject;
            if (PrefabUtility.GetOutermostPrefabInstanceRoot(root) != root || root.GetComponentInParent<Rigidbody>() != null
                || root.GetComponentInParent<Animator>() != null || root.GetComponentInParent<Animation>() != null) return false;
            if (PrefabUtility.GetAddedComponents(root).Count != 0 || PrefabUtility.GetRemovedComponents(root).Count != 0
                || PrefabUtility.GetAddedGameObjects(root).Count != 0 || PrefabUtility.GetRemovedGameObjects(root).Count != 0) return false;
            foreach (var modification in PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>())
            {
                var target = modification.target;
                if (target != source && target != source.transform && target != source.GetComponent<LevelProp>()) return false;
                if (target == source && modification.propertyPath != "m_Name" && modification.propertyPath != "m_IsActive") return false;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root.transform && (referenced.Contains(t.gameObject) || t.GetComponents<Component>().Any(referenced.Contains))) return false;
            return true;
        }

        public static string Eligibility(GameObject root)
        {
            if (root.GetComponent<LevelProp>() == null) return "不是道具";
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0 || renderers.Length == 1 && root.GetComponentsInChildren<Collider>(true).Length < 2) return "已是简单道具";
            var first = renderers[0];
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.gameObject.activeSelf || t.gameObject.layer != root.layer || t.gameObject.tag != root.tag
                    || (root.transform.worldToLocalMatrix * t.localToWorldMatrix).determinant <= 0) return "含隐藏、不同层/标签或镜像部件";
                foreach (var c in t.GetComponents<Component>())
                    if (c == null || !(c is Transform || c is MeshFilter || c is MeshRenderer || c is BoxCollider || c is MeshCollider
                        || c is LevelProp && t == root.transform)) return "含交互、动态或参数化组件";
            }
            foreach (var r in renderers)
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (!r.enabled || r.HasPropertyBlock() || mesh == null || !mesh.isReadable
                    || mesh.subMeshCount != r.sharedMaterials.Length || r.sharedMaterials.Any(m => m == null || m.renderQueue > 2500)
                    || r.lightmapIndex >= 0 || r.probeAnchor != null || r.additionalVertexStreams != null
                    || r.shadowCastingMode != first.shadowCastingMode || r.receiveShadows != first.receiveShadows
                    || r.lightProbeUsage != first.lightProbeUsage || r.reflectionProbeUsage != first.reflectionProbeUsage
                    || r.renderingLayerMask != first.renderingLayerMask || r.motionVectorGenerationMode != first.motionVectorGenerationMode
                    || r.sortingLayerID != first.sortingLayerID || r.sortingOrder != first.sortingOrder)
                    return "渲染设置不兼容";
                for (int s = 0; s < mesh.subMeshCount; s++) if (mesh.GetTopology(s) != MeshTopology.Triangles) return "非三角网格";
            }
            var colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (var c in colliders)
                if (!c.enabled || c.isTrigger || c.sharedMaterial != colliders[0].sharedMaterial
                    || c.includeLayers.value != 0 || c.excludeLayers.value != 0 || c.layerOverridePriority != 0
                    || c is MeshCollider mc && (mc.convex || mc.sharedMesh == null || !mc.sharedMesh.isReadable)) return "碰撞设置不兼容";
            return null;
        }

        public static GameObject Bake(GameObject source, string sourcePath)
        {
            // Versioned dependencies keep older saved scenes valid; rebuilding never overwrites their meshes.
            string folder = BakedFolder(source, sourcePath);
            string path = folder + "/" + source.name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            string reason = Eligibility(source);
            if (reason != null) throw new InvalidOperationException(reason);
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var copy = PrefabUtility.LoadPrefabContents(sourcePath);
            var temporary = new List<Mesh>();
            try
            {
                var renderers = copy.GetComponentsInChildren<MeshRenderer>(true);
                var groups = new Dictionary<Material, List<CombineInstance>>();
                foreach (var r in renderers)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var material = r.sharedMaterials[s];
                        if (!groups.TryGetValue(material, out var list)) groups[material] = list = new();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s,
                            transform = copy.transform.worldToLocalMatrix * r.transform.localToWorldMatrix });
                    }
                }
                Mesh Combine(IEnumerable<CombineInstance> items, string name, bool merge = true)
                {
                    var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(items.ToArray(), merge, true); temporary.Add(mesh); return mesh;
                }
                var parts = groups.Select(g => Combine(g.Value, g.Key.name)).ToArray();
                var preview = Combine(parts.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }), "Preview", false);
                var gameplay = Combine(groups.SelectMany(g => g.Value), "Gameplay");
                var boxes = copy.GetComponentsInChildren<BoxCollider>(true);
                var cubeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var cube = cubeObject.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(cubeObject);
                var collisionParts = new List<CombineInstance>();
                foreach (var c in boxes) collisionParts.Add(new CombineInstance { mesh = cube,
                    transform = copy.transform.worldToLocalMatrix * c.transform.localToWorldMatrix * Matrix4x4.TRS(c.center, Quaternion.identity, c.size) });
                foreach (var c in copy.GetComponentsInChildren<MeshCollider>(true))
                    for (int s = 0; s < c.sharedMesh.subMeshCount; s++) collisionParts.Add(new CombineInstance { mesh = c.sharedMesh, subMeshIndex = s,
                        transform = copy.transform.worldToLocalMatrix * c.transform.localToWorldMatrix });
                var physicsMaterial = copy.GetComponentInChildren<Collider>(true)?.sharedMaterial;
                var collision = collisionParts.Count > 0 ? Combine(collisionParts, "Collision") : null;
                AssetDatabase.CreateAsset(preview, folder + "/Meshes.asset");
                AssetDatabase.AddObjectToAsset(gameplay, preview);
                if (collision != null) AssetDatabase.AddObjectToAsset(collision, preview);
                var renderer = copy.GetComponent<MeshRenderer>();
                if (renderer == null) { renderer = copy.AddComponent<MeshRenderer>(); EditorUtility.CopySerialized(renderers[0], renderer); }
                renderer.sharedMaterials = groups.Keys.ToArray();
                var filter = copy.GetComponent<MeshFilter>();
                if (filter == null) filter = copy.AddComponent<MeshFilter>();
                filter.sharedMesh = preview;
                foreach (var c in copy.GetComponents<Collider>()) Object.DestroyImmediate(c);
                while (copy.transform.childCount > 0) Object.DestroyImmediate(copy.transform.GetChild(0).gameObject);
                if (collision != null) { var collider = copy.AddComponent<MeshCollider>(); collider.sharedMesh = collision; collider.sharedMaterial = physicsMaterial; }
                copy.GetComponent<LevelProp>().combinedGameplayMesh = gameplay;
                AssetDatabase.SaveAssets();
                return PrefabUtility.SaveAsPrefabAsset(copy, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(copy);
                foreach (var mesh in temporary) if (mesh != null && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
            }
        }
    }
}
