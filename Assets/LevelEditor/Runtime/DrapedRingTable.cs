using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sound2.LevelEditing
{
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class DrapedRingTable : MonoBehaviour
    {
        [Min(.2f), InspectorName("内半径（米）")] public float innerRadius = 1.8f;
        [Min(.45f), InspectorName("外半径（米）")] public float outerRadius = 3.4f;
        [Min(.2f), InspectorName("桌高（米）")] public float height = .85f;
        [Range(0, .18f), InspectorName("褶皱深度（米）")] public float foldDepth = .085f;
        [InspectorName("桌布纹理种子")] public int clothSeed = 137;
        [Serializable]
        public struct Gap
        {
            [InspectorName("起点角度")] public float startAngle;
            [Range(0, 360), InspectorName("擦除角度")] public float sweepAngle;
            public Gap(float start, float sweep) { startAngle = start; sweepAngle = sweep; }
        }
        [InspectorName("擦除区段（0° 为本地 +X，朝 +Z 增加）")]
        public List<Gap> gaps = new();
        Mesh ownedVisual, ownedCollision;
        bool needsMeshCheck = true;
        int appliedHash;
        [SerializeField, HideInInspector] Mesh storedVisual, storedCollision;
        [SerializeField, HideInInspector] int storedHash;
        [SerializeField, HideInInspector] int storedVersion;
        const int MeshVersion = 1;

        void OnEnable()
        {
            needsMeshCheck = !TryReuseMeshes(ParameterHash());
        }
        void OnValidate() => needsMeshCheck = true;
        void Update()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this)) return;
#endif
            int hash = ParameterHash();
            if (!needsMeshCheck && appliedHash == hash) return;
            needsMeshCheck = false;
            if (!TryReuseMeshes(hash)) Rebuild();
        }
        bool TryReuseMeshes(int hash)
        {
            if (storedVersion == MeshVersion && storedHash == hash && storedVisual != null && storedCollision != null)
            {
                ApplyMeshes(storedVisual, storedCollision);
                ReleaseOwnedMeshes();
                appliedHash = hash;
                return true;
            }
            if (appliedHash != hash || ownedVisual == null || ownedCollision == null) return false;
            ApplyMeshes(ownedVisual, ownedCollision);
            return true;
        }
        void OnDestroy() => ReleaseOwnedMeshes();
        static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        int ParameterHash()
        {
            unchecked
            {
                int hash = innerRadius.GetHashCode();
                hash = hash * 31 + outerRadius.GetHashCode(); hash = hash * 31 + height.GetHashCode();
                hash = hash * 31 + foldDepth.GetHashCode(); hash = hash * 31 + clothSeed;
                if (gaps != null) foreach (var gap in gaps) { hash = hash * 31 + gap.startAngle.GetHashCode(); hash = hash * 31 + gap.sweepAngle.GetHashCode(); }
                return hash;
            }
        }
        public void Normalize()
        {
            innerRadius = Mathf.Clamp(Finite(innerRadius, 1.8f), .2f, 49.75f);
            outerRadius = Mathf.Clamp(Finite(outerRadius, 3.4f), innerRadius + .25f, 50);
            height = Mathf.Clamp(Finite(height, .85f), .2f, 10);
            foldDepth = Mathf.Clamp(Finite(foldDepth, .085f), 0, Mathf.Min(.18f, innerRadius * .3f));
            gaps ??= new List<Gap>();
            for (int i = 0; i < gaps.Count; i++) gaps[i] = new Gap(Mathf.Repeat(Finite(gaps[i].startAngle, 0), 360), Mathf.Clamp(Finite(gaps[i].sweepAngle, 0), 0, 360));
        }
        public List<Vector2> ErasedIntervals()
        {
            var ranges = new List<Vector2>();
            if (gaps != null) foreach (var gap in gaps)
            {
                float width = Mathf.Clamp(Finite(gap.sweepAngle, 0), 0, 360);
                if (width <= .0001f) continue;
                float start = Mathf.Repeat(Finite(gap.startAngle, 0), 360), end = start + width;
                if (width >= 360) return new List<Vector2> { new Vector2(0, 360) };
                ranges.Add(new Vector2(start, Mathf.Min(end, 360)));
                if (end > 360) ranges.Add(new Vector2(0, end - 360));
            }
            ranges.Sort((a, b) => a.x.CompareTo(b.x));
            var merged = new List<Vector2>();
            foreach (var range in ranges)
            {
                if (merged.Count == 0 || range.x > merged[merged.Count - 1].y + .0001f) merged.Add(range);
                else merged[merged.Count - 1] = new Vector2(merged[merged.Count - 1].x, Mathf.Max(merged[merged.Count - 1].y, range.y));
            }
            return merged;
        }
        public List<Vector2> VisibleIntervals()
        {
            var result = new List<Vector2>(); float start = 0;
            foreach (var gap in ErasedIntervals()) { if (gap.x > start + .0001f) result.Add(new Vector2(start, gap.x)); start = gap.y; }
            if (start < 359.9999f) result.Add(new Vector2(start, 360));
            // Join through 0 degrees so an intact seam never gets a fake end curtain.
            if (result.Count > 1 && result[0].x == 0 && result[result.Count - 1].y == 360)
            {
                result[0] = new Vector2(result[result.Count - 1].x, result[0].y + 360); result.RemoveAt(result.Count - 1);
            }
            return result;
        }
        public void Erase(float start, float sweep)
        {
            gaps ??= new List<Gap>(); gaps.Add(new Gap(start, sweep));
            var merged = ErasedIntervals(); gaps.Clear();
            foreach (var range in merged) gaps.Add(new Gap(range.x, range.y - range.x));
            Rebuild();
        }
        public bool IsErased(float angle)
        {
            angle = Mathf.Repeat(angle, 360);
            foreach (var range in ErasedIntervals()) if (angle >= range.x && angle <= range.y) return true;
            return false;
        }
        float Pleat(float angle, float drop, bool inner)
        {
            float phase = (clothSeed % 10007) * .173f + (inner ? 2.71f : 0);
            float radius = inner ? innerRadius : outerRadius;
            int waves = Mathf.Clamp(Mathf.RoundToInt(radius * Mathf.PI * 2 / .28f), 8, 700);
            float warp = angle + .018f * Mathf.Sin(angle * 5 + phase) + .011f * Mathf.Sin(angle * 11 - phase);
            float broad = Mathf.PerlinNoise(11 + Mathf.Cos(angle) * 2.73f + phase, 17 + Mathf.Sin(angle) * 2.73f + phase);
            float narrow = .64f * Mathf.Sin(waves * warp + phase + drop * .5f * Mathf.Sin(angle * 3 + phase))
                + .24f * Mathf.Sin((waves + 7) * angle - phase + drop * .8f) + .12f * Mathf.Cos((waves * 2 - 3) * angle + phase);
            return narrow * Mathf.Lerp(.58f, 1.15f, broad);
        }
        public Vector3 ClothEdge(float degrees, float drop, bool inner)
        {
            float angle = Mathf.Repeat(degrees, 360) * Mathf.Deg2Rad;
            float eased = Mathf.Sin(Mathf.Min(1, drop * 2.5f) * Mathf.PI * .5f);
            float pooled = Mathf.Pow(drop, 9) * foldDepth * .65f;
            float displacement = foldDepth * eased * (Pleat(angle, drop, inner) + .55f) + pooled;
            float radius = inner ? innerRadius - displacement : outerRadius + displacement;
            return new Vector3(Mathf.Cos(angle) * radius, height * (1 - drop), Mathf.Sin(angle) * radius);
        }
        public void Rebuild()
        {
            Normalize(); needsMeshCheck = false; appliedHash = ParameterHash();
            var visual = new MeshData(); var collision = new MeshData();
            foreach (var arc in VisibleIntervals())
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt((arc.y - arc.x) * Mathf.Deg2Rad * outerRadius / .045f), 1, 2048);
                visual.Surface(steps, 1, (u, v) => Vector3.Lerp(ClothEdge(Mathf.Lerp(arc.x, arc.y, u), 0, true), ClothEdge(Mathf.Lerp(arc.x, arc.y, u), 0, false), v), false);
                visual.Surface(steps, 18, (u, v) => ClothEdge(Mathf.Lerp(arc.x, arc.y, u), v, false), false);
                visual.Surface(steps, 18, (u, v) => ClothEdge(Mathf.Lerp(arc.x, arc.y, u), v, true), true);
                visual.Surface(steps, 1, (u, v) => Vector3.Lerp(ClothEdge(Mathf.Lerp(arc.x, arc.y, u), 1, true), ClothEdge(Mathf.Lerp(arc.x, arc.y, u), 1, false), v), true);
                int collisionSteps = Mathf.Clamp(Mathf.CeilToInt((arc.y - arc.x) / 2), 1, 720);
                collision.Surface(collisionSteps, 1, (u, v) => Vector3.Lerp(ClothEdge(Mathf.Lerp(arc.x, arc.y, u), 0, true), ClothEdge(Mathf.Lerp(arc.x, arc.y, u), 0, false), v), false);
                collision.Surface(collisionSteps, 1, (u, v) => ClothEdge(Mathf.Lerp(arc.x, arc.y, u), v, false), false);
                collision.Surface(collisionSteps, 1, (u, v) => ClothEdge(Mathf.Lerp(arc.x, arc.y, u), v, true), true);
                collision.Surface(collisionSteps, 1, (u, v) => Vector3.Lerp(ClothEdge(Mathf.Lerp(arc.x, arc.y, u), 1, true), ClothEdge(Mathf.Lerp(arc.x, arc.y, u), 1, false), v), true);
                if (arc.y - arc.x < 359.9999f)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        float angle = side == 0 ? arc.x : arc.y; int sign = side == 0 ? 1 : -1;
                        Vector3 End(float u, float v)
                        {
                            var p = Vector3.Lerp(ClothEdge(angle, v, true), ClothEdge(angle, v, false), u);
                            float a = angle * Mathf.Deg2Rad;
                            float wrinkle = foldDepth * .5f * Mathf.Sin(u * Mathf.PI) * Mathf.Sin(v * Mathf.PI) * (1 + .6f * Mathf.Sin(u * 29 + v * 3 + clothSeed));
                            return p + new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * (sign * wrinkle);
                        }
                        visual.Surface(24, 18, End, side == 1);
                        collision.Surface(1, 1, (u, v) => Vector3.Lerp(ClothEdge(angle, v, true), ClothEdge(angle, v, false), u), side == 1);
                    }
                }
            }
            var newVisual = visual.Build("Ring cloth (generated)"); var newCollision = collision.Build("Ring collision (generated)");
            var filter = GetComponent<MeshFilter>(); var collider = GetComponent<MeshCollider>();
            filter.sharedMesh = newVisual; collider.sharedMesh = null;
            if (newCollision.vertexCount > 0) collider.sharedMesh = newCollision;
            ReleaseOwnedMeshes(); ownedVisual = newVisual; ownedCollision = newCollision;
        }
        public void UseStoredMeshes(Mesh visual, Mesh collision)
        {
            Normalize();
            ApplyMeshes(visual, collision);
            ReleaseOwnedMeshes(); appliedHash = ParameterHash(); needsMeshCheck = false;
            storedVisual = visual; storedCollision = collision;
            storedHash = appliedHash; storedVersion = MeshVersion;
        }
        void ApplyMeshes(Mesh visual, Mesh collision)
        {
            var filter = GetComponent<MeshFilter>();
            var collider = GetComponent<MeshCollider>();
            if (filter.sharedMesh != visual) filter.sharedMesh = visual;
            var target = collision != null && collision.vertexCount > 0 ? collision : null;
            // Reassigning an unchanged collider mesh needlessly repeats PhysX cooking.
            if (collider.sharedMesh != target) collider.sharedMesh = target;
        }
        void ReleaseOwnedMeshes()
        {
            Release(ownedVisual); Release(ownedCollision); ownedVisual = null; ownedCollision = null;
        }
        static void Release(Mesh mesh)
        {
            if (mesh == null) return;
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.Contains(mesh)) return;
#endif
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
        sealed class MeshData
        {
            readonly List<Vector3> vertices = new(); readonly List<int> indices = new(); readonly List<Vector2> uv = new();
            public void Surface(int columns, int rows, Func<float, float, Vector3> point, bool reverse)
            {
                int first = vertices.Count;
                for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
                { float u = (float)x / columns, v = (float)y / rows; vertices.Add(point(u, v)); uv.Add(new Vector2(u, v)); }
                for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
                {
                    int a = first + y * (columns + 1) + x, b = a + 1, c = a + columns + 1, d = c + 1;
                    if (reverse) { indices.Add(a); indices.Add(c); indices.Add(b); indices.Add(b); indices.Add(c); indices.Add(d); }
                    else { indices.Add(a); indices.Add(b); indices.Add(c); indices.Add(b); indices.Add(d); indices.Add(c); }
                }
            }
            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}
