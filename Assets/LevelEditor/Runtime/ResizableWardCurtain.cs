using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sound2.LevelEditing
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ResizableWardCurtain : MonoBehaviour
    {
        [Min(.5f), InspectorName("长度（米）")] public float length = 2.5f;
        public const float FoldStep = .059f;
        public const float FoldDepth = .055f;
        [SerializeField, HideInInspector] MeshFilter model;
        [SerializeField, HideInInspector] Mesh cylinder;
        [SerializeField, HideInInspector] Mesh sphere;
        [SerializeField, HideInInspector] BoxCollider body;
        Mesh ownedMesh;
        float appliedLength = float.NaN;
        bool needsRebuild = true;

        public MeshFilter Model => model;
        public void Configure(MeshFilter target, Mesh rodMesh, Mesh wheelMesh, BoxCollider collision)
        { model = target; cylinder = rodMesh; sphere = wheelMesh; body = collision; Rebuild(); }
        void OnEnable()
        {
            needsRebuild = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
            UnityEditor.EditorApplication.update += EditorUpdate;
#endif
        }
        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
        }
#if UNITY_EDITOR
        void EditorUpdate() { if (this != null && !Application.isPlaying) Update(); }
#endif
        void OnValidate() => needsRebuild = true;
        void Update()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this)) return;
#endif
            if (model == null || cylinder == null || sphere == null || body == null) return;
            if (needsRebuild || appliedLength != length)
            {
                Rebuild();
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
#endif
            }
        }
        void OnDestroy() => ReleaseOwnedMesh();
        public static float FoldZ(float distanceFromLeft) => Mathf.Sin(distanceFromLeft / FoldStep * Mathf.PI * .5f) * FoldDepth;
        public void Rebuild()
        {
            length = float.IsNaN(length) || float.IsInfinity(length) ? 2.5f : Mathf.Clamp(length, .5f, 50);
            if (model == null || cylinder == null || sphere == null || body == null) return;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var triangles = new[] { new List<int>(), new List<int>(), new List<int>() };
            void Part(Mesh source, Matrix4x4 matrix, int slot)
            {
                int offset = vertices.Count; var sourceVertices = source.vertices; var sourceNormals = source.normals; var sourceUV = source.uv;
                var normalMatrix = matrix.inverse.transpose;
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    vertices.Add(matrix.MultiplyPoint3x4(sourceVertices[i])); normals.Add(normalMatrix.MultiplyVector(sourceNormals[i]).normalized);
                    uv.Add(sourceUV.Length == sourceVertices.Length ? sourceUV[i] : Vector2.zero);
                }
                foreach (int index in source.triangles) triangles[slot].Add(index + offset);
            }
            void Rod(Vector3 a, Vector3 b, float diameter) => Part(cylinder,
                Matrix4x4.TRS((a + b) * .5f, Quaternion.FromToRotation(Vector3.up, b - a), new Vector3(diameter, Vector3.Distance(a, b) * .5f, diameter)), 0);
            float half = length * .5f;
            foreach (int side in new[] { -1, 1 })
            {
                Rod(new Vector3(side * half, .07f, 0), new Vector3(side * half, 2.32f, 0), .04f);
                Rod(new Vector3(side * half, .07f, -.36f), new Vector3(side * half, .07f, .36f), .055f);
                foreach (int z in new[] { -1, 1 }) Part(sphere, Matrix4x4.TRS(new Vector3(side * half, .055f, z * .3f), Quaternion.identity, Vector3.one * .1f), 1);
            }
            Rod(new Vector3(-half, 2.32f, 0), new Vector3(half, 2.32f, 0), .04f);
            float start = -half + .07f, span = length - .14f;
            int segments = Mathf.CeilToInt(span / FoldStep);
            for (int i = 0; i < segments; i++)
            {
                float a = i * FoldStep, b = Mathf.Min(span, (i + 1) * FoldStep);
                if (b - a < .000001f) continue;
                var quad = new[] { new Vector3(start + a, .35f, FoldZ(a)), new Vector3(start + a, 2.23f, FoldZ(a)),
                    new Vector3(start + b, 2.23f, FoldZ(b)), new Vector3(start + b, .35f, FoldZ(b)) };
                var normal = Vector3.Cross(quad[1] - quad[0], quad[2] - quad[0]).normalized;
                for (int side = 0; side < 2; side++)
                {
                    int offset = vertices.Count;
                    for (int j = 0; j < 4; j++) { vertices.Add(quad[j]); normals.Add(side == 0 ? normal : -normal); uv.Add(new Vector2(j < 2 ? a : b, j == 0 || j == 3 ? 0 : 1.88f)); }
                    foreach (int index in side == 0 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 2, 1, 0, 3, 2, 0 }) triangles[2].Add(offset + index);
                }
            }
            float hookStart = -half + .15f, hookEnd = half - .15f, lastHook = hookStart;
            for (int i = 0; hookStart + i * .2f <= hookEnd + .00001f; i++)
            { lastHook = hookStart + i * .2f; Rod(new Vector3(lastHook, 2.22f, 0), new Vector3(lastHook, 2.32f, 0), .018f); }
            if (hookEnd - lastHook > .03f) Rod(new Vector3(hookEnd, 2.22f, 0), new Vector3(hookEnd, 2.32f, 0), .018f);
            var mesh = new Mesh { name = "Ward curtain (generated)", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.subMeshCount = 3;
            for (int slot = 0; slot < 3; slot++) mesh.SetTriangles(triangles[slot], slot); mesh.RecalculateBounds();
            model.sharedMesh = mesh; ReleaseOwnedMesh(); ownedMesh = mesh;
            body.center = new Vector3(0, 1.3f, 0); body.size = new Vector3(length, 2.05f, .16f);
            appliedLength = length; needsRebuild = false;
        }
        public void UseStoredMesh(Mesh mesh) { model.sharedMesh = mesh; ReleaseOwnedMesh(); appliedLength = length; needsRebuild = false; }
        void ReleaseOwnedMesh()
        {
            if (ownedMesh == null) return;
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.Contains(ownedMesh)) { ownedMesh = null; return; }
#endif
            if (Application.isPlaying) Destroy(ownedMesh); else DestroyImmediate(ownedMesh); ownedMesh = null;
        }
    }
}
