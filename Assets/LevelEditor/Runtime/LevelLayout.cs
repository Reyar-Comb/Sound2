using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sound2.LevelEditing
{
    public enum RoomShape { Rectangle, Circle }
    public enum RoomUse { Hall, Living, Bedroom, Kitchen, Utility, Bathroom, Study, Classroom, Office, Shelves, Reading, CartBay, Checkout, Produce, ColdStore, Dining, Ward, Clinic, Treatment, Arena, Chase }

    [Serializable]
    public sealed class RoomPartition
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "隔板";
        public Vector2 start = new(-2, 0), end = new(2, 0);
        [Min(.05f)] public float thickness = .15f;
        [Min(.1f)] public float height = 2.4f;
    }

    [Serializable]
    public sealed class LevelRoom
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "房间";
        public RoomShape shape;
        public RoomUse use;
        public bool openPlan;
        public Vector2 center;
        public Vector2 size = new Vector2(10, 10);
        public float rotation;
        public List<RoomPartition> partitions = new();
        public Vector2 PartitionPoint(Vector2 point) => center + Rotate(point);
        public Vector2 PartitionLocal(Vector2 point)
        {
            float a = -rotation * Mathf.Deg2Rad; var p = point - center;
            return new Vector2(p.x * Mathf.Cos(a) - p.y * Mathf.Sin(a), p.x * Mathf.Sin(a) + p.y * Mathf.Cos(a));
        }

        public Vector2 Rotate(Vector2 p)
        {
            float a = rotation * Mathf.Deg2Rad;
            return new Vector2(p.x * Mathf.Cos(a) - p.y * Mathf.Sin(a), p.x * Mathf.Sin(a) + p.y * Mathf.Cos(a));
        }

        public Vector2 Edge(float t, out Vector2 normal)
        {
            t = Mathf.Repeat(t, 1);
            Vector2 p;
            if (shape == RoomShape.Circle)
            {
                float a = t * Mathf.PI * 2;
                p = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                normal = Rotate(p);
                return center + Rotate(p * size.x * .5f);
            }
            float u = t * 4;
            int side = Mathf.FloorToInt(u);
            float f = u - side;
            switch (side)
            {
                case 0: p = new Vector2(size.x * .5f, Mathf.Lerp(-size.y, size.y, f) * .5f); normal = Vector2.right; break;
                case 1: p = new Vector2(Mathf.Lerp(size.x, -size.x, f) * .5f, size.y * .5f); normal = Vector2.up; break;
                case 2: p = new Vector2(-size.x * .5f, Mathf.Lerp(size.y, -size.y, f) * .5f); normal = Vector2.left; break;
                default: p = new Vector2(Mathf.Lerp(-size.x, size.x, f) * .5f, -size.y * .5f); normal = Vector2.down; break;
            }
            normal = Rotate(normal);
            return center + Rotate(p);
        }

        public float NearestEdge(Vector2 point)
        {
            Vector2 delta = point - center;
            float a = -rotation * Mathf.Deg2Rad;
            Vector2 p = new Vector2(delta.x * Mathf.Cos(a) - delta.y * Mathf.Sin(a), delta.x * Mathf.Sin(a) + delta.y * Mathf.Cos(a));
            if (shape == RoomShape.Circle) return Mathf.Repeat(Mathf.Atan2(p.y, p.x) / (2 * Mathf.PI), 1);
            float best = float.MaxValue, result = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector2 start = Edge(i / 4f, out _), end = Edge((i + 1) / 4f, out _);
                Vector2 d = end - start;
                float f = Mathf.Clamp01(Vector2.Dot(point - start, d) / d.sqrMagnitude);
                float distance = (point - start - d * f).sqrMagnitude;
                if (distance < best) { best = distance; result = (i + f) / 4f; }
            }
            return result;
        }
    }

    [Serializable]
    public sealed class RoomConnection
    {
        public string roomId;
        [Range(0, 1)] public float edge;
    }

    [Serializable]
    public sealed class LevelCorridor
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "走廊";
        public bool curved;
        public bool doorOnly;
        public float width = 3;
        public List<Vector2> points = new List<Vector2>();
        public RoomConnection start = new RoomConnection();
        public RoomConnection end = new RoomConnection();
    }

    [Serializable]
    public sealed class LevelPit
    {
        public string name = "下落竖井";
        public Vector2 center;
        public float size = 2;
        public float depth = 8;
        public float rotation;
    }

    [Serializable]
    public sealed class LevelDoorway
    {
        public Vector2 center;
        public Vector2 size = new(2.6f, .8f);
    }

    [DefaultExecutionOrder(-300), DisallowMultipleComponent]
    public sealed class LevelLayout : MonoBehaviour
    {
        public List<RoomPartition> partitions = new();
        public List<LevelRoom> rooms = new List<LevelRoom>();
        public List<LevelCorridor> corridors = new List<LevelCorridor>();
        public List<LevelPit> pits = new List<LevelPit>();
        public List<LevelDoorway> doorways = new();
        [Range(0, 1.8f), Tooltip("本关敌人可跨上的单级台阶高度（米），不是绝对海拔或爬墙高度。")]
        public float enemyStepHeight = .3f;
        [Min(.1f)] public float wallHeight = 3.2f;
        [Min(.05f)] public float wallThickness = .2f;
        [Min(.05f)] public float floorThickness = .25f;
        public bool openSky;
        public bool architecturalPartitions;
        [Range(4, 64)] public int curveSamples = 16;
        [Range(16, 128)] public int circleSegments = 64;
        public Material floorMaterial;
        public Material wallMaterial;
        public Material pitMaterial;
        [Tooltip("Gameplay surface material. Preview materials above remain unchanged while editing.")]
        public Material gameplayMaterial;
        [HideInInspector] public GameObject generatedRoot;
        [HideInInspector] public string geometryAsset;

        void OnEnable() => SetGameplayPresentation(Application.isPlaying);
        void OnDisable() => SetGameplayPresentation(false);

        public void SetGameplayPresentation(bool gameplay)
        {
            if (generatedRoot == null) return;
            foreach (var renderer in generatedRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var preview = renderer.name == "Floor" ? floorMaterial
                    : renderer.name == "Walls" || renderer.name == "Ceiling" || renderer.name == "DoorLintels" || renderer.name.StartsWith("EmbeddedLintel_") || renderer.name.StartsWith("Partition_") ? wallMaterial : pitMaterial;
                renderer.sharedMaterial = gameplay ? gameplayMaterial : preview;
            }
            var ceiling = generatedRoot.transform.Find("Ceiling");
            if (ceiling != null) ceiling.gameObject.SetActive(gameplay && !openSky);
            var lintels = generatedRoot.transform.Find("DoorLintels");
            if (lintels != null) lintels.gameObject.SetActive(gameplay);
        }

        public LevelRoom FindRoom(string id) => rooms.Find(r => r.id == id);
        public Vector3 World(Vector2 point, float y = 0) => transform.TransformPoint(new Vector3(point.x, y, point.y));
        public Vector2 Local(Vector3 point) { var p = transform.InverseTransformPoint(point); return new Vector2(p.x, p.z); }
    }
}
