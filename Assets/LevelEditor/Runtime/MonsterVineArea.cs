using UnityEngine;

namespace Sound2.LevelEditing
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MonsterVines))]
    public sealed class MonsterVineArea : MonoBehaviour
    {
        [Min(.2f), InspectorName("矩形宽度（米）")] public float width = 6;
        [Min(.2f), InspectorName("矩形长度（米）")] public float depth = 4;
        [Min(.1f), InspectorName("攀爬高度（米）")] public float height = 3;
        [Range(1, 8), InspectorName("每次生长源数量")] public int growthSources = 3;
        [Min(0), InspectorName("接触伤害距离（米）")] public float contactDamageRadius = .65f;
        [Min(.1f), InspectorName("接触攻击间隔（秒）")] public float contactDamageInterval = .5f;
        [SerializeField, HideInInspector] Transform previewRoot;
        [SerializeField, HideInInspector] Transform[] previewEdges;
        Vector3 appliedSize;
        bool dirty = true;
        MonsterVines vines;
        PlayerVitality playerVitality;
        float nextContactDamage;
        public Vector3 Size => new Vector3(width, height, depth);
        public Bounds WorldBounds
        {
            get
            {
                var bounds = new Bounds(transform.TransformPoint(new Vector3(0, height * .5f, 0)), Vector3.zero);
                for (int i = 0; i < 8; i++) bounds.Encapsulate(transform.TransformPoint(new Vector3(
                    (i & 1) == 0 ? -width * .5f : width * .5f, (i & 2) == 0 ? -.06f : height, (i & 4) == 0 ? -depth * .5f : depth * .5f)));
                bounds.Expand(.1f); return bounds;
            }
        }
        public bool ContainsWorld(Vector3 point)
        {
            var p = transform.InverseTransformPoint(point);
            return Mathf.Abs(p.x) <= width * .5f + .0001f && Mathf.Abs(p.z) <= depth * .5f + .0001f && p.y >= -.06f && p.y <= height + .0001f;
        }
        public void ConfigurePreview(Transform root, Transform[] edges) { previewRoot = root; previewEdges = edges; RefreshPreview(); }
        void OnEnable()
        {
            vines = GetComponent<MonsterVines>();
            vines.BindArea(this); nextContactDamage = 0; dirty = true; RefreshPreview();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
            UnityEditor.EditorApplication.update += EditorUpdate;
#endif
        }
        void OnDisable()
        {
            var vines = GetComponent<MonsterVines>(); if (vines != null) vines.ClearTrail();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
        }
#if UNITY_EDITOR
        void EditorUpdate() { if (this != null && !Application.isPlaying) Update(); }
#endif
        void OnValidate() => dirty = true;
        void Update()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this)) return;
#endif
            if (dirty || appliedSize != Size) RefreshPreview();
            if (Application.isPlaying) TryContactDamage();
        }
        void TryContactDamage()
        {
            if (Time.time < nextContactDamage || contactDamageRadius <= 0 || vines == null
                || !vines.isActiveAndEnabled) return;
            if (playerVitality == null) playerVitality = FindAnyObjectByType<PlayerVitality>();
            if (playerVitality == null || !playerVitality.TryGetComponent<CharacterController>(out var capsule)) return;
            // Use the nearest point in the oriented area, rather than just its central pivot.
            Vector3 local = transform.InverseTransformPoint(capsule.bounds.center);
            Vector3 origin = transform.TransformPoint(new Vector3(
                Mathf.Clamp(local.x, -width * .5f, width * .5f),
                Mathf.Clamp(local.y, -.06f, height),
                Mathf.Clamp(local.z, -depth * .5f, depth * .5f)));
            if (!playerVitality.CanReceiveContactFrom(transform, origin, contactDamageRadius)) return;
            nextContactDamage = Time.time + Mathf.Max(.1f, contactDamageInterval);
            playerVitality.TakeLethalDamage();
        }
        static float Dimension(float value, float fallback, float min, float max) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        public void RefreshPreview()
        {
            width = Dimension(width, 6, .2f, 100); depth = Dimension(depth, 4, .2f, 100); height = Dimension(height, 3, .1f, 30);
            growthSources = Mathf.Clamp(growthSources, 1, 8); appliedSize = Size; dirty = false;
            if (previewRoot == null) return;
            previewRoot.gameObject.SetActive(!Application.isPlaying);
            if (previewEdges == null || previewEdges.Length != 12) return;
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = new Vector3((i & 1) == 0 ? -width * .5f : width * .5f,
                (i & 2) == 0 ? .015f : height, (i & 4) == 0 ? -depth * .5f : depth * .5f);
            int edge = 0;
            for (int i = 0; i < 8; i++) foreach (int bit in new[] { 1, 2, 4 }) if ((i & bit) == 0)
            {
                var part = previewEdges[edge++]; if (part == null) continue;
                var a = corners[i]; var b = corners[i | bit];
                part.localPosition = (a + b) * .5f; part.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                part.localScale = new Vector3(.018f, Vector3.Distance(a, b), .018f);
            }
        }
        void OnDrawGizmosSelected()
        {
            var previous = Gizmos.matrix; var color = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix; Gizmos.color = new Color(1, .12f, .1f, .85f);
            Gizmos.DrawWireCube(new Vector3(0, height * .5f, 0), Size);
            Gizmos.matrix = previous; Gizmos.color = color;
        }
    }
}
