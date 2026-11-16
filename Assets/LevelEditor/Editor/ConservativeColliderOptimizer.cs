using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Sound2.LevelEditing.Editor
{
    /// <summary>Only bakes unchanged static collision surfaces; never fills a concavity.</summary>
    public static class ConservativeColliderOptimizer
    {
        public const string Folder = "Assets/LevelEditor/CollisionMeshes";
        public sealed class Change
        {
            public string name, operation;
            public int before, after;
        }

        public static HashSet<Object> ReferencedColliders(IEnumerable<GameObject> roots)
        {
            var result = new HashSet<Object>();
            foreach (var component in roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)))
            {
                if (component == null) continue;
                using var serialized = new SerializedObject(component);
                var p = serialized.GetIterator();
                while (p.Next(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null)
                        result.Add(p.objectReferenceValue);
            }
            return result;
        }

        public static string Ineligible(LevelProp prop)
        {
            if (prop.GetComponentInParent<Rigidbody>(true) != null || prop.GetComponentInParent<Animator>(true) != null
                || prop.GetComponentInParent<Animation>(true) != null) return "moving parent";
            foreach (var t in prop.GetComponentsInChildren<Transform>(true))
            {
                // A disabled level root is fine; authored disabled children are not.
                if (t != prop.transform && !t.gameObject.activeSelf) return "inactive child";
                foreach (var c in t.GetComponents<Component>())
                    if (c == null || !(c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider
                        || c == prop)) return "gameplay, movement or parametric component";
                var matrix = ToRoot(t, prop.transform);
                if (matrix.determinant <= 0 || !Orthogonal(matrix) || !Orthogonal(t.localToWorldMatrix)) return "mirrored or sheared transform";
            }
            return null;
        }

        static bool Orthogonal(Matrix4x4 m)
        {
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            return Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) < .00001f
                && Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) < .00001f
                && Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) < .00001f;
        }

        static string Settings(Collider c) => $"{c.gameObject.layer}/{c.tag}/{c.sharedMaterial?.GetEntityId().ToString() ?? "none"}/{c.contactOffset:R}/{c.includeLayers.value}/{c.excludeLayers.value}/{c.layerOverridePriority}/{c.providesContacts}";

        public static List<Change> Optimize(GameObject root, HashSet<Object> referenced)
        {
            var changes = new List<Change>();
            foreach (var prop in root.GetComponentsInChildren<LevelProp>(true))
            {
                if (Ineligible(prop) != null) continue;
                // A source prefab can acquire the same bake as an older instance override.
                // Retain the inherited copy and remove only identical generated duplicates.
                var duplicates = prop.GetComponentsInChildren<MeshCollider>(true)
                    .Where(c => c.sharedMesh != null && AssetDatabase.GetAssetPath(c.sharedMesh).StartsWith(Folder + "/", StringComparison.Ordinal)
                        && c.transform.parent == prop.transform && c.transform.localPosition == Vector3.zero
                        && c.transform.localRotation == Quaternion.identity && c.transform.localScale == Vector3.one)
                    .GroupBy(c => $"{c.sharedMesh.GetEntityId()}/{Settings(c)}/{c.enabled}/{c.isTrigger}").Where(g => g.Count() > 1).ToArray();
                foreach (var group in duplicates)
                {
                    var copies = group.OrderBy(c => PrefabUtility.IsAddedGameObjectOverride(c.gameObject) ? 1 : 0).ToArray();
                    int removed = 0;
                    foreach (var copy in copies.Skip(1))
                    {
                        if (referenced.Contains(copy) || referenced.Contains(copy.gameObject) || referenced.Contains(copy.transform)) continue;
                        if (copy.transform.childCount == 0 && copy.GetComponents<Component>().Length == 2)
                            Object.DestroyImmediate(copy.gameObject);
                        else Object.DestroyImmediate(copy);
                        removed++;
                    }
                    if (removed > 0) changes.Add(new Change { name = prop.name, operation = "identical inherited bake", before = copies.Length, after = copies.Length - removed });
                }
                // Box surfaces are reproduced exactly. Curved primitives and convex meshes
                // stay separate: their rendered mesh need not equal their cooked shape.
                var groups = prop.GetComponentsInChildren<BoxCollider>(true)
                    .Where(c => c.enabled && !c.isTrigger && !referenced.Contains(c))
                    .GroupBy(Settings).Where(g => g.Count() >= 8).Select(g => g.ToArray()).ToArray();
                foreach (var boxes in groups)
                {
                    var vertices = new List<Vector3>(); var triangles = new List<int>();
                    foreach (var box in boxes) AddBox(box, prop.transform, vertices, triangles);
                    string key = Hash(vertices, triangles);
                    string path = Folder + "/StaticBoxes_" + key + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null)
                    {
                        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/LevelEditor", "CollisionMeshes");
                        mesh = new Mesh { name = "Static collision " + key, indexFormat = IndexFormat.UInt32 };
                        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
                        AssetDatabase.CreateAsset(mesh, path);
                    }
                    var first = boxes[0];
                    var go = new GameObject("Static collision " + key.Substring(0, 8));
                    go.transform.SetParent(prop.transform, false);
                    go.layer = first.gameObject.layer; go.tag = first.tag;
                    var combined = go.AddComponent<MeshCollider>();
                    combined.sharedMesh = mesh;
                    CopySettings(first, combined);
                    foreach (var box in boxes) Object.DestroyImmediate(box);
                    changes.Add(new Change { name = prop.name, operation = "identical static box surfaces", before = boxes.Length, after = 1 });
                }
            }
            return changes;
        }

        static void CopySettings(Collider from, Collider to)
        {
            to.enabled = from.enabled; to.isTrigger = from.isTrigger; to.sharedMaterial = from.sharedMaterial;
            to.contactOffset = from.contactOffset; to.includeLayers = from.includeLayers; to.excludeLayers = from.excludeLayers;
            to.layerOverridePriority = from.layerOverridePriority; to.providesContacts = from.providesContacts;
        }

        static void AddBox(BoxCollider box, Transform root, List<Vector3> vertices, List<int> triangles)
        {
            var matrix = ToRoot(box.transform, root);
            int offset = vertices.Count;
            for (int i = 0; i < 8; i++) vertices.Add(matrix.MultiplyPoint3x4(box.center + Vector3.Scale(box.size * .5f,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            // Outward winding on all six faces, including the back of thin panels.
            int[] indices = { 0,4,6, 0,6,2, 1,3,7, 1,7,5, 0,1,5, 0,5,4,
                2,6,7, 2,7,3, 0,2,3, 0,3,1, 4,5,7, 4,7,6 };
            foreach (int i in indices) triangles.Add(offset + i);
        }

        static Matrix4x4 ToRoot(Transform node, Transform root)
        {
            var result = Matrix4x4.identity;
            while (node != root)
            {
                result = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale) * result;
                node = node.parent;
            }
            return result;
        }

        static string Hash(List<Vector3> vertices, List<int> triangles)
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                foreach (var v in vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                foreach (int i in triangles) writer.Write(i);
            }
            using var hash = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").Substring(0, 24).ToLowerInvariant();
        }
    }
}
