using System;
using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sound2.LevelEditing.Editor
{
    public static class LevelGeometry
    {
        const double Scale = 10000;
        public static Path64 Path(IEnumerable<Vector2> points)
        {
            var path = new Path64();
            foreach (var p in points) path.Add(new Point64(p.x * Scale, p.y * Scale));
            return path;
        }
        public static Vector2 Point(Point64 p) => new Vector2((float)(p.X / Scale), (float)(p.Y / Scale));
        public static Paths64 Union(Paths64 paths) => Clipper.Union(paths, FillRule.NonZero);
        public static Paths64 Difference(Paths64 a, Paths64 b) => Clipper.Difference(a, b, FillRule.NonZero);
        public static Paths64 Offset(Paths64 a, float width, EndType end = EndType.Polygon) => Clipper.InflatePaths(a, width * Scale, JoinType.Round, end, 2, .01 * Scale);
        public static double Area(Paths64 paths) => Math.Abs(Clipper.Area(paths)) / (Scale * Scale);

        public static List<Vector2> RoomOutline(LevelRoom room, int segments)
        {
            var points = new List<Vector2>();
            int count = room.shape == RoomShape.Circle ? segments : 4;
            for (int i = 0; i < count; i++) points.Add(room.Edge(i / (float)count, out _));
            return points;
        }

        public static List<Vector2> PitOutline(LevelPit pit, float padding = 0)
        {
            var r = new LevelRoom { center = pit.center, size = Vector2.one * (pit.size + padding * 2), rotation = pit.rotation };
            return RoomOutline(r, 4);
        }

        public static Vector2 Endpoint(LevelLayout layout, LevelCorridor corridor, bool end)
        {
            var connection = end ? corridor.end : corridor.start;
            var room = layout.FindRoom(connection.roomId);
            return room != null ? room.Edge(connection.edge, out _) : corridor.points[end ? corridor.points.Count - 1 : 0];
        }

        public static List<Vector2> CorridorPath(LevelLayout layout, LevelCorridor corridor, bool extendIntoRooms = false)
        {
            var control = new List<Vector2>(corridor.points);
            if (control.Count < 2) return control;
            control[0] = Endpoint(layout, corridor, false);
            control[control.Count - 1] = Endpoint(layout, corridor, true);
            var startRoom = layout.FindRoom(corridor.start.roomId);
            var endRoom = layout.FindRoom(corridor.end.roomId);
            Vector2 startNormal = Vector2.zero, endNormal = Vector2.zero;
            if (startRoom != null) startRoom.Edge(corridor.start.edge, out startNormal);
            if (endRoom != null) endRoom.Edge(corridor.end.edge, out endNormal);
            if (corridor.doorOnly) return new List<Vector2> { control[0] - startNormal, control[control.Count - 1] - endNormal };
            var points = new List<Vector2>();
            if (extendIntoRooms && startRoom != null) points.Add(control[0] - startNormal * corridor.width);
            // A short normal-aligned throat ensures a full-width doorway, even on round rooms.
            if (startRoom != null) { points.Add(control[0]); control[0] += startNormal * corridor.width * .5f; }
            if (endRoom != null) control[control.Count - 1] += endNormal * corridor.width * .5f;
            for (int i = 0; i < control.Count - 1; i++)
            {
                if (!corridor.curved) { points.Add(control[i]); continue; }
                Vector2 p1 = control[i], p2 = control[i + 1];
                Vector2 p0 = i == 0 ? p1 - (p2 - p1) : control[i - 1];
                Vector2 p3 = i + 2 < control.Count ? control[i + 2] : p2 + p2 - p1;
                Vector2 m1 = (p2 - p0) * .5f, m2 = (p3 - p1) * .5f;
                if (i == 0 && startRoom != null) m1 = startNormal * Vector2.Distance(p1, p2);
                if (i == control.Count - 2 && endRoom != null) m2 = -endNormal * Vector2.Distance(p1, p2);
                for (int j = 0; j < layout.curveSamples; j++)
                {
                    float t = j / (float)layout.curveSamples, t2 = t * t, t3 = t2 * t;
                    points.Add((2 * t3 - 3 * t2 + 1) * p1 + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * p2 + (t3 - t2) * m2);
                }
            }
            points.Add(control[control.Count - 1]);
            if (endRoom != null) points.Add(Endpoint(layout, corridor, true));
            if (extendIntoRooms && endRoom != null) points.Add(Endpoint(layout, corridor, true) - endNormal * corridor.width);
            return points;
        }

        public static Paths64 Footprint(LevelLayout layout)
        {
            var all = new Paths64();
            foreach (var room in layout.rooms) all.Add(Path(RoomOutline(room, layout.circleSegments)));
            foreach (var corridor in layout.corridors)
            {
                if (corridor.doorOnly) continue;
                if (corridor.points.Count < 2) throw new InvalidOperationException(corridor.name + " 至少需要两个控制点。");
                var line = Path(CorridorPath(layout, corridor, true));
                all.AddRange(Offset(new Paths64 { line }, corridor.width * .5f, EndType.Butt));
            }
            return Union(all);
        }

        public static void Validate(LevelLayout layout, Paths64 footprint)
        {
            if (!float.IsFinite(layout.wallHeight) || layout.wallHeight < .1f || layout.wallThickness < .05f || layout.floorThickness < .05f)
                throw new InvalidOperationException("墙高、墙厚与地板厚度必须大于最小值。");
            var holes = new Paths64();
            foreach (var pit in layout.pits)
            {
                if (pit.size < .3f || pit.depth < layout.floorThickness + .1f) throw new InvalidOperationException(pit.name + " 的尺寸或深度过小。");
                var p = new Paths64 { Path(PitOutline(pit, layout.wallThickness)) };
                if (Area(Difference(p, footprint)) > .0001) throw new InvalidOperationException(pit.name + " 必须完整位于房间/走廊内部，并为管壁留出空间。");
                if (Area(Clipper.Intersect(p, holes, FillRule.NonZero)) > .0001) throw new InvalidOperationException("竖井及管壁不能相互重叠。");
                holes.AddRange(p);
            }
        }

        public static Paths64 WallFootprint(LevelLayout layout, Paths64 footprint, out Paths64 lintels, float minHeight = 0)
        {
            var ring = Difference(Offset(footprint, layout.wallThickness), footprint);
            lintels = new Paths64();
            if (layout.architecturalPartitions)
            {
                var partitions = new Paths64();
                foreach (var room in layout.rooms)
                {
                    if (room.openPlan || room.Height(layout) < minHeight) continue;
                    var outline = new Paths64 { Path(RoomOutline(room, layout.circleSegments)) };
                    partitions.AddRange(Difference(Offset(outline, layout.wallThickness * .5f), Offset(outline, -layout.wallThickness * .5f)));
                }
                ring.AddRange(partitions); ring = Union(ring);
                foreach (var corridor in layout.corridors)
                {
                    // Every attached endpoint opens its room partition, including elevator vestibules.
                    foreach (var connection in new[] { corridor.start, corridor.end })
                    {
                        var room = layout.FindRoom(connection.roomId); if (room == null || room.Height(layout) < minHeight) continue;
                        var point = room.Edge(connection.edge, out var normal);
                        var cut = new LevelRoom { center = point, size = new Vector2(layout.wallThickness + .4f, corridor.width),
                            rotation = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg };
                        var opening = new Paths64 { Path(RoomOutline(cut, 4)) };
                        lintels.AddRange(Clipper.Intersect(ring, opening, FillRule.NonZero));
                        ring = Difference(ring, opening);
                    }
                }
            }
            foreach (var doorway in layout.doorways)
                ring = Difference(ring, new Paths64 { Path(RoomOutline(new LevelRoom { center=doorway.center, size=doorway.size },4)) });
            return ring;
        }

        // At each ceiling height, keep the footprint of all rooms that still reach it.
        // Corridor extensions inside rooms belong to the room, including at low doorways.
        static SortedList<float, Paths64> HeightFootprints(LevelLayout layout, Paths64 footprint)
        {
            var levels = new SortedList<float, Paths64>();
            if (layout.rooms.TrueForAll(room => room.Height(layout) == layout.wallHeight))
            {
                levels.Add(layout.wallHeight, footprint);
                return levels;
            }

            Paths64 RegionAt(float height)
            {
                if (!levels.TryGetValue(height, out var region))
                {
                    region = new Paths64();
                    levels.Add(height, region);
                }
                return region;
            }
            var rooms = new Paths64();
            foreach (var room in layout.rooms)
            {
                var outline = Path(RoomOutline(room, layout.circleSegments));
                rooms.Add(outline);
                RegionAt(room.Height(layout)).Add(outline);
            }
            var corridors = Difference(footprint, Union(rooms));
            if (corridors.Count > 0) RegionAt(layout.wallHeight).AddRange(corridors);

            var higher = new Paths64();
            for (int i = levels.Count - 1; i > 0; i--)
            {
                var outlines = levels.Values[i];
                outlines.AddRange(higher);
                higher = Union(outlines);
                levels[levels.Keys[i]] = higher;
            }
            levels[levels.Keys[0]] = footprint;
            return levels;
        }

        static void BuildShell(LevelLayout layout, Paths64 footprint, Paths64 baseWalls, Paths64 baseLintels,
            Dictionary<string, Mesh> meshes)
        {
            var walls = new List<(Paths64, float, float)>();
            var ceilings = new List<(Paths64, float, float)>();
            var doorLintels = new List<(Paths64, float, float)>();
            var levels = HeightFootprints(layout, footprint);
            var higherRoof = new Paths64();
            for (int i = levels.Count - 1; i >= 0; i--)
            {
                float top = levels.Keys[i], bottom = i == 0 ? 0 : levels.Keys[i - 1];
                var area = levels.Values[i];
                var roof = Offset(area, layout.wallThickness);
                ceilings.Add((higherRoof.Count == 0 ? roof : Difference(roof, higherRoof), top, top + layout.floorThickness));
                higherRoof = roof;

                var ring = baseWalls;
                var lintels = baseLintels;
                if (i > 0)
                {
                    ring = WallFootprint(layout, area, out lintels, top);
                    // Close the step above the lower ceiling, even at an open-plan join or doorway.
                    var step = Clipper.Intersect(Difference(roof, area), footprint, FillRule.NonZero);
                    ring.AddRange(step);
                    ring = Union(ring);
                    lintels = Difference(Union(lintels), step);
                }
                walls.Add((ring, bottom, top));
                float lintelBottom = Mathf.Max(bottom, 2.6f);
                if (lintels.Count > 0 && top > lintelBottom)
                    doorLintels.Add((Union(lintels), lintelBottom, top));
            }
            meshes.Add("Walls", Extrude(walls, "Walls"));
            meshes.Add("Ceiling", Extrude(ceilings, "Ceiling"));
            if (doorLintels.Count > 0) meshes.Add("DoorLintels", Extrude(doorLintels, "DoorLintels"));
        }

        public static Dictionary<string, Mesh> Build(LevelLayout layout)
        {
            foreach (var r in layout.rooms)
            {
                if (r.size.x < .5f || r.size.y < .5f) throw new InvalidOperationException("房间尺寸至少为 0.5 米。");
                float height = r.Height(layout);
                if (!float.IsFinite(height) || height < .1f)
                    throw new InvalidOperationException(r.name + " 的净高必须至少为 0.1 米。");
            }
            foreach (var c in layout.corridors)
            {
                if (c.width < .3f) throw new InvalidOperationException("走廊宽度至少为 0.3 米。");
                if(c.doorOnly && (layout.FindRoom(c.start.roomId)==null || layout.FindRoom(c.end.roomId)==null ||
                    Vector2.Distance(Endpoint(layout,c,false),Endpoint(layout,c,true))>.02f))
                    throw new InvalidOperationException(c.name+" 的共墙门洞不再对齐。请将两侧房间贴合，或取消共墙门洞以使用走廊连接。");
            }
            var footprint = Footprint(layout);
            Validate(layout, footprint);
            var holes = new Paths64();
            foreach (var pit in layout.pits) holes.Add(Path(PitOutline(pit)));
            var meshes = new Dictionary<string, Mesh>();
            try
            {
                meshes.Add("Floor", Extrude(Difference(footprint, Union(holes)), -layout.floorThickness, 0, "Floor"));
                var ring = WallFootprint(layout, footprint, out var lintels);
                var doors = EmbeddedDoorPlacement.ResolveAll(layout, ring);
                BuildShell(layout, footprint, ring, lintels, meshes);
                foreach (var host in LevelPartitions.All(layout))
                {
                    var partition = host.data;
                    if (partition.thickness < .05f || partition.height < .1f || Vector2.Distance(host.Start, host.End) < .05f)
                        throw new InvalidOperationException(partition.name + " 的长度、厚度或高度过小。");
                    var strip = EmbeddedDoorPlacement.Cut(host.Strip, host.key, partition.height, doors, meshes);
                    meshes.Add(host.key, Extrude(strip, 0, partition.height, host.key));
                }
                for (int i = 0; i < layout.pits.Count; i++)
                {
                    var pit = layout.pits[i];
                    var inner = new Paths64 { Path(PitOutline(pit)) };
                    var outer = new Paths64 { Path(PitOutline(pit, layout.wallThickness)) };
                    // The shaft is a ring, never a capped box. Its top is below the walking surface.
                    meshes.Add("Shaft_" + i, Extrude(Difference(outer, inner), -pit.depth, -layout.floorThickness, "Shaft_" + i));
                }
                EmbeddedDoorPlacement.ApplyAll(doors);
                return meshes;
            }
            catch { foreach (var mesh in meshes.Values) UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }

        public static Mesh Extrude(Paths64 paths, float bottom, float top, string name) =>
            Extrude(new[] { (paths, bottom, top) }, name);

        static Mesh Extrude(IEnumerable<(Paths64 paths, float bottom, float top)> sections, string name)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) return;
                int index = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
                var normal = Vector3.Cross(b - a, c - a).normalized;
                foreach (var v in new[] { a, b, c }) uv.Add(Mathf.Abs(normal.y) > .5f ? new Vector2(v.x, v.z) : new Vector2(Vector3.Dot(v, Vector3.Cross(normal, Vector3.up)), v.y));
            }
            Vector3 V(Point64 p, float y) { var v = Point(p); return new Vector3(v.x, y, v.y); }
            foreach (var (paths, bottom, top) in sections)
            {
                if (paths.Count == 0) continue;
                var result = Clipper.Triangulate(paths, out Paths64 faces);
                if (result != TriangulateResult.success) throw new InvalidOperationException("轮廓三角化失败：" + result + "。请调整重合的控制点。");
                foreach (var f in faces)
                {
                    Vector3 a = V(f[0], top), b = V(f[1], top), c = V(f[2], top);
                    if (Vector3.Cross(b - a, c - a).y < 0) (b, c) = (c, b);
                    Triangle(a, b, c);
                    a.y = b.y = c.y = bottom;
                    Triangle(c, b, a);
                }
                foreach (var path in paths)
                for (int i = 0; i < path.Count; i++)
                {
                    Vector3 a = V(path[i], bottom), b = V(path[(i + 1) % path.Count], bottom);
                    Vector3 c = b + Vector3.up * (top - bottom), d = a + Vector3.up * (top - bottom);
                    Triangle(a, c, b); Triangle(a, d, c);
                }
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
