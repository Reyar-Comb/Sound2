using System;
using System.Collections.Generic;
using Clipper2Lib;
using UnityEditor;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public static class PartitionAlignment
    {
        static bool Inside(Paths64 paths, Vector2 point)
        {
            var p = new Point64(point.x * 10000, point.y * 10000); bool inside = false;
            foreach (var path in paths)
            {
                var state = Clipper.PointInPolygon(p, path);
                if (state == PointInPolygonResult.IsOn) return true;
                if (state == PointInPolygonResult.IsInside) inside = !inside;
            }
            return inside;
        }

        static bool Section(Paths64 walls, Vector2 point, Vector2 tangent, Vector2 normal, float reach, out Vector2 span)
        {
            var cuts = new List<float>(); span = default;
            foreach (var path in walls)
                for (int i = 0; i < path.Count; i++)
                {
                    var a = LevelGeometry.Point(path[i]) - point; var b = LevelGeometry.Point(path[(i + 1) % path.Count]) - point;
                    float delta = Vector2.Dot(b - a, tangent);
                    if (Mathf.Abs(delta) < .000001f) continue;
                    float t = -Vector2.Dot(a, tangent) / delta;
                    if (t < 0 || t > 1) continue;
                    float value = Vector2.Dot(Vector2.Lerp(a, b, t), normal);
                    if (Mathf.Abs(value) < reach) cuts.Add(value);
                }
            cuts.Sort(); float best = float.PositiveInfinity;
            for (int i = 1; i < cuts.Count; i++)
            {
                float lo = cuts[i - 1], hi = cuts[i], mid = (lo + hi) / 2;
                if (hi - lo < .049f || hi - lo > reach || !Inside(walls, point + normal * mid) || Mathf.Abs(mid) >= best) continue;
                best = Mathf.Abs(mid); span = new Vector2(lo, hi);
            }
            return best < float.PositiveInfinity;
        }

        public static void Align(LevelLayout layout, RoomPartition partition)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请退出运行模式后对齐隔板。");
            var host = new List<LevelPartitions.Host>(LevelPartitions.All(layout)).Find(h => h.data == partition);
            if (host == null) throw new InvalidOperationException("隔板不属于当前布局。");
            var tangent = (host.End - host.Start).normalized;
            if (tangent.sqrMagnitude < .5f) throw new InvalidOperationException("隔板长度过小。");
            var normal = new Vector2(-tangent.y, tangent.x);
            var walls = LevelGeometry.WallFootprint(layout, LevelGeometry.Footprint(layout), out _);
            float sample = layout.wallThickness + .05f, reach = Mathf.Max(.6f, layout.wallThickness * 3);
            bool left = Section(walls, host.Start - tangent * sample, tangent, normal, reach, out var a);
            bool right = Section(walls, host.End + tangent * sample, tangent, normal, reach, out var b);
            if (!left && !right) throw new InvalidOperationException("两端未找到可延续的墙面。请先把隔板两端接到墙边。");
            if (left && right && Vector2.Distance(a, b) > .003f)
                throw new InvalidOperationException("两端墙面不在同一平面或厚度不同，无法同时贴齐；请调整隔板端点。");
            var span = left ? a : b; var offset = normal * ((span.x + span.y) / 2);
            // The samples must belong to walls actually meeting the endpoints, not nearby
            // disconnected pieces across a gap.
            foreach (var end in new[] { host.Start, host.End })
                if (!Inside(walls, end + offset + tangent * .002f) && !Inside(walls, end + offset - tangent * .002f))
                    throw new InvalidOperationException("隔板端点未接触墙体，请先将端点延伸到墙边。");
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("隔板贴齐相邻墙面");
            Undo.RegisterCompleteObjectUndo(layout, "隔板贴齐相邻墙面");
            try
            {
                Vector2 start = host.Start + offset, end = host.End + offset;
                partition.start = host.room == null ? start : host.room.PartitionLocal(start);
                partition.end = host.room == null ? end : host.room.PartitionLocal(end);
                partition.thickness = span.y - span.x;
                LevelBuilder.Rebuild(layout);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            }
            catch { Undo.RevertAllDownToGroup(group); LevelBuilder.Rebuild(layout); throw; }
        }
    }
}
