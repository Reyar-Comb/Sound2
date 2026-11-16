using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sound2.LevelEditing.Editor
{
    // Collider-only probes: never instantiate a prop's scripts or move the real instance.
    [InitializeOnLoad]
    public static class LevelPropOverlap
    {
        const float Tolerance = .001f;
        static GameObject probeRoot;
        static readonly Dictionary<System.Type, Collider> probes = new();

        static LevelPropOverlap()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
            EditorApplication.playModeStateChanged += _ => Cleanup();
        }

        static void Cleanup()
        {
            probes.Clear();
            if (probeRoot != null) Object.DestroyImmediate(probeRoot);
            probeRoot = null;
        }

        public static Vector3 PreviewScale(LevelLayout layout, GameObject source) =>
            source.GetComponent<EmbeddedDoor>() != null ? layout.transform.lossyScale
                : source.scene.IsValid() ? source.transform.lossyScale : source.transform.localScale;

        static bool Solid(Collider collider) => collider.enabled && !collider.isTrigger
            && collider.gameObject.activeInHierarchy;

        static Collider Probe(Collider source, Matrix4x4 matrix, Scene scene)
        {
            if (probeRoot == null)
            {
                probeRoot = new GameObject("Prop collision probes") { hideFlags = HideFlags.HideAndDontSave };
            }
            if (probeRoot.scene != scene) SceneManager.MoveGameObjectToScene(probeRoot, scene);
            var type = source.GetType();
            if (!probes.TryGetValue(type, out var probe) || probe == null)
            {
                var go = new GameObject(type.Name) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(probeRoot.transform, false);
                probe = (Collider)go.AddComponent(type);
                probes[type] = probe;
            }
            EditorUtility.CopySerialized(source, probe);
            probe.hideFlags = HideFlags.HideAndDontSave;
            probe.enabled = true;
            probe.transform.SetPositionAndRotation(matrix.GetColumn(3), matrix.rotation);
            probe.transform.localScale = matrix.lossyScale;
            return probe;
        }

        public static bool HasOverlap(LevelLayout layout, GameObject source, Pose pose)
        {
            if (Application.isPlaying || layout == null || source == null) return false;
            try { return CheckOverlap(layout, source, pose); }
            finally { foreach (var probe in probes.Values) if (probe != null) probe.enabled = false; }
        }

        static bool CheckOverlap(LevelLayout layout, GameObject source, Pose pose)
        {
            Physics.SyncTransforms();
            var obstacles = new List<Collider>();
            var props = LevelPropPlacement.PropsRoot(layout);
            if (props != null) obstacles.AddRange(props.GetComponentsInChildren<Collider>());
            // Embedded doors cut their host partition during commit; its current mesh is not an obstacle.
            if (layout.generatedRoot != null && source.GetComponent<EmbeddedDoor>() == null)
                obstacles.AddRange(layout.generatedRoot.GetComponentsInChildren<Collider>());
            if (layout.manualGeometry && source.GetComponent<EmbeddedDoor>() == null)
            {
                obstacles.Clear();
                foreach (var collider in layout.GetComponentsInChildren<Collider>())
                    if (collider.GetComponentInParent<LevelLayout>() == layout) obstacles.Add(collider);
            }
            obstacles.RemoveAll(c => !Solid(c) || c.transform.IsChildOf(source.transform));
            if (obstacles.Count == 0) return false;
            var rootMatrix = Matrix4x4.TRS(pose.position, pose.rotation, PreviewScale(layout, source));
            var nearby = new List<Collider>();
            foreach (var collider in source.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                var matrix = rootMatrix * source.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                var bounds = WorldBounds(collider, matrix);
                bounds.Expand(-Tolerance * 2);
                nearby.Clear();
                foreach (var other in obstacles) if (bounds.Intersects(other.bounds)) nearby.Add(other);
                if (nearby.Count == 0) continue;
                var probe = Probe(collider, matrix, layout.gameObject.scene);
                Physics.SyncTransforms();
                foreach (var other in nearby)
                {
                    var intersection = probe.bounds;
                    intersection.Expand(-Tolerance * 2);
                    if (!intersection.Intersects(other.bounds)) continue;
                    if (probe is MeshCollider a && !a.convex && other is MeshCollider b && !b.convex)
                    {
                        if (MeshOverlap(a, b)) return true;
                    }
                    else if (Physics.ComputePenetration(probe, probe.transform.position, probe.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out float depth)
                        && depth > Tolerance) return true;
                }
            }
            return false;
        }

        static Bounds WorldBounds(Collider collider, Matrix4x4 matrix)
        {
            Bounds local;
            if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
            else if (collider is SphereCollider sphere)
            {
                var scale = matrix.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                return new Bounds(matrix.MultiplyPoint3x4(sphere.center), Vector3.one * radius * 2);
            }
            else if (collider is CapsuleCollider capsule)
            {
                var scale = matrix.lossyScale;
                float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[(capsule.direction + 1) % 3]),
                    Mathf.Abs(scale[(capsule.direction + 2) % 3]));
                float halfHeight = Mathf.Max(radius, capsule.height * Mathf.Abs(scale[capsule.direction]) / 2);
                var axis = Vector3.zero; axis[capsule.direction] = 1;
                var extent = Abs(matrix.rotation * axis) * (halfHeight - radius) + Vector3.one * radius;
                return new Bounds(matrix.MultiplyPoint3x4(capsule.center), extent * 2);
            }
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
            else return new Bounds(matrix.GetColumn(3), Vector3.one * 100000);
            var x = matrix.MultiplyVector(Vector3.right * local.extents.x);
            var y = matrix.MultiplyVector(Vector3.up * local.extents.y);
            var z = matrix.MultiplyVector(Vector3.forward * local.extents.z);
            Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), (Abs(x) + Abs(y) + Abs(z)) * 2);
        }

        // PhysX cannot test two non-convex meshes with ComputePenetration. Test actual
        // surfaces and enclosed volumes instead, retaining gaps in baked compound props.
        static bool MeshOverlap(MeshCollider a, MeshCollider b)
        {
            if (a.sharedMesh == null || b.sharedMesh == null) return false;
            ReadMesh(a, out var av, out var at); ReadMesh(b, out var bv, out var bt);
            return Crosses(av, at, b) || Crosses(bv, bt, a)
                || InsideSamples(av, at, bv, bt) || InsideSamples(bv, bt, av, at);
        }

        static void ReadMesh(MeshCollider collider, out Vector3[] points, out int[] triangles)
        {
            // The editor can inspect imported collision meshes without changing Read/Write import settings.
            using var data = MeshUtility.AcquireReadOnlyMeshData(collider.sharedMesh);
            using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
            data[0].GetVertices(vertices);
            points = vertices.ToArray();
            var indices = new List<int>();
            for (int submesh = 0; submesh < data[0].subMeshCount; submesh++)
            {
                using var part = new NativeArray<int>(data[0].GetSubMesh(submesh).indexCount, Allocator.Temp);
                data[0].GetIndices(part, submesh);
                indices.AddRange(part.ToArray());
            }
            triangles = indices.ToArray();
            var matrix = collider.transform.localToWorldMatrix;
            for (int i = 0; i < points.Length; i++) points[i] = matrix.MultiplyPoint3x4(points[i]);
        }

        static bool Crosses(Vector3[] vertices, int[] triangles, MeshCollider target)
        {
            for (int i = 0; i < triangles.Length; i += 3)
                for (int edge = 0; edge < 3; edge++)
                {
                    var start = vertices[triangles[i + edge]];
                    var end = vertices[triangles[i + (edge + 1) % 3]];
                    var delta = end - start; float length = delta.magnitude;
                    if (length <= Tolerance * 2) continue;
                    var direction = delta / length;
                    if (CrossesSurface(start, direction, length, target)
                        || CrossesSurface(end, -direction, length, target)) return true;
                }
            return false;
        }

        static bool CrossesSurface(Vector3 start, Vector3 direction, float length, MeshCollider target) =>
            target.Raycast(new Ray(start + direction * Tolerance, direction), out var hit, length - Tolerance * 2)
            && Mathf.Abs(Vector3.Dot(hit.normal, direction)) > .0001f;

        static bool InsideSamples(Vector3[] vertices, int[] triangles, Vector3[] other, int[] otherTriangles)
        {
            // With surface crossings already excluded, one interior sample per connected
            // shell is enough. A baked bookshelf can contain hundreds of separate boxes.
            var parents = new int[triangles.Length / 3];
            var sharedVertices = new Dictionary<Vector3, int>();
            int Find(int i)
            {
                while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; }
                return i;
            }
            for (int face = 0; face < parents.Length; face++)
            {
                parents[face] = face;
                for (int corner = 0; corner < 3; corner++)
                {
                    var vertex = vertices[triangles[face * 3 + corner]];
                    if (sharedVertices.TryGetValue(vertex, out int neighbor)) parents[Find(face)] = Find(neighbor);
                    else sharedVertices.Add(vertex, face);
                }
            }
            var sampled = new HashSet<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (!sampled.Add(Find(i / 3))) continue;
                var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                var normal = Vector3.Cross(b - a, c - a).normalized;
                var point = (a + b + c) / 3 - normal * Tolerance * 2;
                if (Inside(point, other, otherTriangles)) return true;
            }
            return false;
        }

        static bool Inside(Vector3 point, Vector3[] vertices, int[] triangles)
        {
            double winding = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]] - point;
                var b = vertices[triangles[i + 1]] - point;
                var c = vertices[triangles[i + 2]] - point;
                double denominator = (double)a.magnitude * b.magnitude * c.magnitude
                    + Vector3.Dot(a, b) * (double)c.magnitude + Vector3.Dot(b, c) * (double)a.magnitude
                    + Vector3.Dot(c, a) * (double)b.magnitude;
                winding += 2 * System.Math.Atan2(Vector3.Dot(a, Vector3.Cross(b, c)), denominator);
            }
            return System.Math.Abs(winding) > System.Math.PI * 2;
        }
    }
}
