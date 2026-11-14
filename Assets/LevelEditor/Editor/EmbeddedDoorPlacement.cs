using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using UnityEditor;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public static class EmbeddedDoorPlacement
    {
        public const string Help = "门只能嵌入隔板。先用“隔板”工具画出隔板，再指向隔板放门；房间边缘、共享边界和走廊本身不能放门。门洞须避开其他墙体和竖井。";
        public sealed class Attachment
        {
            public string roomId, partitionId, mesh;
            public float along, depth, height;
            public int facing;
            public Pose pose;
            public Paths64 cut;
            public EmbeddedDoor instance;
        }

        public static EmbeddedDoor[] Doors(LevelLayout layout) =>
            LevelPropPlacement.PropsRoot(layout)?.GetComponentsInChildren<EmbeddedDoor>(true) ?? Array.Empty<EmbeddedDoor>();
        static Paths64 Walls(LevelLayout layout) => LevelGeometry.WallFootprint(layout, LevelGeometry.Footprint(layout), out _);
        static bool Intersects(Paths64 a, Paths64 b) => LevelGeometry.Area(Clipper.Intersect(a, b, FillRule.NonZero)) > .00001;
        static Paths64 Rectangle(Vector2 point, Vector2 right, Vector2 normal, float width, float depth) =>
            new Paths64 { LevelGeometry.Path(new[] { point - right * width / 2 - normal * depth / 2,
                point + right * width / 2 - normal * depth / 2, point + right * width / 2 + normal * depth / 2,
                point - right * width / 2 + normal * depth / 2 }) };

        static bool Resolve(LevelLayout layout, string roomId, string partitionId, float along, int facing, Paths64 walls, out Attachment result)
        {
            result = null;
            if (string.IsNullOrEmpty(partitionId)) return false;
            var host = LevelPartitions.All(layout).FirstOrDefault(h => h.data.id == partitionId && (h.room?.id ?? "") == (roomId ?? ""));
            if (host == null) return false;
            var partition = host.data; var delta = host.End - host.Start; float length = delta.magnitude;
            float half = EmbeddedDoor.Width / 2;
            if (along * length < half + .04f || (1 - along) * length < half + .04f
                || partition.height < EmbeddedDoor.Height || partition.thickness < .05f) return false;
            var point = Vector2.Lerp(host.Start, host.End, along); var tangent = delta.normalized;
            var normal = new Vector2(-tangent.y, tangent.x);
            var cut = Rectangle(point, tangent, normal, EmbeddedDoor.Width, partition.thickness + .04f);
            if (Intersects(cut, walls)) return false;
            foreach (var other in LevelPartitions.All(layout))
                if (other.key != host.key && Intersects(cut, other.Strip)) return false;
            var threshold = Rectangle(point, tangent, normal, EmbeddedDoor.Width, partition.thickness + .15f);
            foreach (var pit in layout.pits)
                if (Intersects(threshold, new Paths64 { LevelGeometry.Path(LevelGeometry.PitOutline(pit, layout.wallThickness)) })) return false;
            var rotation = Quaternion.LookRotation(layout.transform.TransformDirection(new Vector3(normal.x, 0, normal.y)) * facing, layout.transform.up);
            result = new Attachment { roomId = host.room?.id ?? "", partitionId = partitionId, along = along, facing = facing,
                depth = partition.thickness, height = partition.height, mesh = host.key, pose = new Pose(layout.World(point), rotation), cut = cut };
            return true;
        }

        static bool ResolveDoor(LevelLayout layout, EmbeddedDoor door, Paths64 walls, out Attachment a) =>
            Resolve(layout, door.roomId, door.partitionId, door.along, door.facing, walls, out a);
        static bool Overlaps(LevelLayout layout, Attachment candidate, EmbeddedDoor ignore, Paths64 walls)
        {
            foreach (var door in Doors(layout))
                if (door != ignore && ResolveDoor(layout, door, walls, out var other)
                    && Intersects(LevelGeometry.Offset(candidate.cut, .055f), LevelGeometry.Offset(other.cut, .055f))) return true;
            return false;
        }

        public static bool TryPose(LevelLayout layout, GameObject source, Ray ray, out Pose pose)
        {
            pose = default; float nearestDistance = float.PositiveInfinity; Collider nearest = null;
            Physics.SyncTransforms();
            var surfaces = layout.generatedRoot.GetComponentsInChildren<Collider>().AsEnumerable();
            var props = LevelPropPlacement.PropsRoot(layout);
            if (props != null) surfaces = surfaces.Concat(props.GetComponentsInChildren<Collider>());
            foreach (var collider in surfaces)
                if (collider.enabled && !collider.isTrigger && !collider.transform.IsChildOf(source.transform)
                    && collider.Raycast(ray, out var hit, 10000) && hit.distance < nearestDistance)
                { nearest = collider; nearestDistance = hit.distance; }
            var origin = layout.transform.InverseTransformPoint(ray.origin);
            var direction = layout.transform.InverseTransformVector(ray.direction);
            var origin2 = new Vector2(origin.x, origin.z); var direction2 = new Vector2(direction.x, direction.z);
            var walls = Walls(layout); float bestDistance = float.PositiveInfinity;
            foreach (var host in LevelPartitions.All(layout))
            {
                var delta = host.End - host.Start; float length = delta.magnitude;
                if (length < .05f) continue;
                var normal = new Vector2(-delta.y, delta.x).normalized;
                float denominator = Vector2.Dot(direction2, normal), t;
                if (Mathf.Abs(denominator) > .00001f) t = Vector2.Dot(host.Start - origin2, normal) / denominator;
                else
                {
                    if (Mathf.Abs(direction.y) < .00001f) continue;
                    float surfaceY = direction.y < 0 ? host.data.height : 0;
                    t = (surfaceY - origin.y) / direction.y;
                }
                float allowance = nearest != null && nearest.transform.parent == layout.generatedRoot.transform && nearest.name == host.key
                    ? host.data.thickness / (2 * Mathf.Max(.001f, Mathf.Abs(denominator))) + .03f : .03f;
                if (t < 0 || t > nearestDistance + allowance || t >= bestDistance) continue;
                var hit = origin + direction * t; var point = new Vector2(hit.x, hit.z);
                if (hit.y < -.03f || hit.y > host.data.height + .03f || Mathf.Abs(Vector2.Dot(point - host.Start, normal)) > host.data.thickness / 2 + .12f) continue;
                float along = Vector2.Dot(point - host.Start, delta) / delta.sqrMagnitude;
                int facing = Vector2.Dot(direction2, normal) > 0 ? -1 : 1;
                if (!Resolve(layout, host.room?.id, host.data.id, along, facing, walls, out var a)
                    || Overlaps(layout, a, source.GetComponent<EmbeddedDoor>(), walls)) continue;
                pose = a.pose; bestDistance = t;
            }
            return bestDistance < float.PositiveInfinity;
        }

        public static Attachment ValidatePose(LevelLayout layout, GameObject source, Pose pose)
        {
            var walls = Walls(layout); var point = layout.Local(pose.position);
            foreach (var host in LevelPartitions.All(layout))
            {
                var delta = host.End - host.Start;
                if (delta.sqrMagnitude < .0025f) continue;
                float along = Vector2.Dot(point - host.Start, delta) / delta.sqrMagnitude;
                foreach (int facing in new[] { 1, -1 })
                    if (Resolve(layout, host.room?.id, host.data.id, along, facing, walls, out var a)
                        && Vector3.Distance(a.pose.position, pose.position) < .015f
                        && Quaternion.Angle(a.pose.rotation, pose.rotation) < .5f
                        && !Overlaps(layout, a, source.GetComponent<EmbeddedDoor>(), walls)) return a;
            }
            throw new InvalidOperationException(Help);
        }

        public static void Bind(EmbeddedDoor door, Attachment a)
        {
            Undo.RecordObject(door, "嵌入隔板门");
            door.roomId = a.roomId; door.partitionId = a.partitionId; door.along = a.along; door.facing = a.facing;
            door.corridorId = ""; door.sharedBoundaryId = "";
            PrefabUtility.RecordPrefabInstancePropertyModifications(door); Apply(door, a);
        }
        static void Apply(EmbeddedDoor door, Attachment a)
        {
            door.transform.SetPositionAndRotation(a.pose.position, a.pose.rotation);
            door.transform.localScale = Vector3.one; door.FitDepth(a.depth);
            foreach (var t in new[] { door.transform, door.frontTrim, door.backTrim, door.jamb })
                if (t != null) { EditorUtility.SetDirty(t); PrefabUtility.RecordPrefabInstancePropertyModifications(t); }
        }
        public static List<Attachment> ResolveAll(LevelLayout layout, Paths64 walls)
        {
            var result = new List<Attachment>();
            foreach (var door in Doors(layout))
            {
                if (!ResolveDoor(layout, door, walls, out var a))
                    throw new InvalidOperationException(door.name + " 未绑定有效隔板，或门洞被其他墙体遮挡。门现在仅能放在隔板上，请重新吸附到隔板、修正隔板尺寸或删除此门。");
                a.instance = door;
                if (result.Any(b => Intersects(LevelGeometry.Offset(a.cut, .055f), LevelGeometry.Offset(b.cut, .055f))))
                    throw new InvalidOperationException("门不能相互重叠。");
                result.Add(a);
            }
            return result;
        }
        public static void ApplyAll(List<Attachment> doors) { foreach (var door in doors) Apply(door.instance, door); }
        public static Paths64 Cut(Paths64 source, string mesh, float height, List<Attachment> doors, Dictionary<string, Mesh> meshes)
        {
            for (int i = 0; i < doors.Count; i++)
            {
                var door = doors[i]; if (door.mesh != mesh) continue;
                var lintel = Clipper.Intersect(source, door.cut, FillRule.NonZero);
                source = LevelGeometry.Difference(source, door.cut);
                if (height > EmbeddedDoor.Height + .001f)
                {
                    string name = "EmbeddedLintel_" + i;
                    meshes.Add(name, LevelGeometry.Extrude(lintel, EmbeddedDoor.Height, height, name));
                }
            }
            return source;
        }
    }
}
