using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sound2.LevelEditing.Editor
{
    public sealed partial class LevelDesignerWindow
    {
        [SerializeField] bool partitionsExpanded = true;

        void DrawPartitionObjects(VisualElement area)
        {
            var fold = new Foldout { name = "partition-objects", text = $"独立隔板 ({layout.partitions.Count})", value = partitionsExpanded };
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) partitionsExpanded = e.newValue; });
            area.Add(fold);
            for (int i = 0; i < layout.partitions.Count; i++)
            {
                int index = i; var p = layout.partitions[i];
                var button = Button(fold, p.name + $" / {Vector2.Distance(p.start, p.end):0.##} m", () => Select(Kind.Partition, index));
                button.AddToClassList("item"); button.EnableInClassList("selected-item", kind == Kind.Partition && selected == i);
            }
            Button(fold, "绘制隔板（点击两端）", () => { tool = Tool.Partition; pending.Clear(); selectedProp = null; selected = -1; kind = Kind.None; Refresh(); SceneView.RepaintAll(); });
        }

        bool DrawFreePartitionProperties(VisualElement area)
        {
            if (layout == null || kind != Kind.Partition || selected < 0 || selected >= layout.partitions.Count) return false;
            var p = layout.partitions[selected];
            area.Add(new Label("独立隔板 · 不受房间边界限制"));
            Text(area, "名称", p.name, v => p.name = v);
            Vector(area, "起点 X / Z", p.start, v => p.start = Snap(v)).RegisterValueChangedCallback(_ => DrawProperties());
            Vector(area, "终点 X / Z", p.end, v => p.end = Snap(v)).RegisterValueChangedCallback(_ => DrawProperties());
            Vector(area, "中心 X / Z", (p.start + p.end) / 2, v => { var d = Snap(v) - (p.start + p.end) / 2; p.start += d; p.end += d; }).RegisterValueChangedCallback(_ => DrawProperties());
            Float(area, "高度", p.height, v => Change(() => p.height = Mathf.Max(.1f, v)));
            Float(area, "厚度", p.thickness, v => Change(() => p.thickness = Mathf.Max(.05f, v)));
            Button(area, "贴齐相邻墙面", () =>
            {
                try { PartitionAlignment.Align(layout, p); hasError = false; status = "隔板已贴齐相邻墙面，门已跟随；Ctrl+Z 可撤销。"; }
                catch (System.Exception error) { hasError = true; status = error.Message; }
                Refresh(); SceneView.RepaintAll();
            });
            Button(area, "在隔板上放门", () => ChooseProp(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WoodenDoor.prefab")));
            area.Add(new Label("拖动中心可整体移动，拖动两端可改变长度和方向。删除隔板会同时删除嵌入的门，可撤销恢复。"));
            Button(area, "删除隔板及其门", Delete);
            return true;
        }

        void PlacePartitionPoint()
        {
            if (pending.Count == 0) { pending.Add(hover); return; }
            if (Vector2.Distance(pending[0], hover) < .05f) return;
            var partition = new RoomPartition { start = pending[0], end = hover, height = layout.wallHeight, thickness = layout.wallThickness, name = "隔板 " + (layout.partitions.Count + 1) };
            Change(() => layout.partitions.Add(partition)); pending.Clear(); tool = Tool.Select; Select(Kind.Partition, layout.partitions.Count - 1);
        }

        void DeletePartition(RoomPartition partition, LevelRoom room = null)
        {
            Undo.RegisterCompleteObjectUndo(layout, "删除隔板及其门");
            foreach (var door in EmbeddedDoorPlacement.Doors(layout).Where(d => d.partitionId == partition.id && (d.roomId ?? "") == (room?.id ?? "")))
                Undo.DestroyObjectImmediate(door.gameObject);
            if (room == null) layout.partitions.Remove(partition); else room.partitions.Remove(partition);
        }

        void DetachPartition(LevelRoom room, RoomPartition partition)
        {
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("转为独立隔板");
            Undo.RegisterCompleteObjectUndo(layout, "转为独立隔板");
            Change(() =>
            {
                partition.start = room.PartitionPoint(partition.start); partition.end = room.PartitionPoint(partition.end);
                room.partitions.Remove(partition); layout.partitions.Add(partition);
                foreach (var door in EmbeddedDoorPlacement.Doors(layout).Where(d => d.roomId == room.id && d.partitionId == partition.id))
                {
                    Undo.RecordObject(door, "隔板独立"); door.roomId = ""; EditorUtility.SetDirty(door);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(door);
                }
            });
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            Select(Kind.Partition, layout.partitions.Count - 1);
        }

        bool PartitionSceneGUI(SceneView view, Event e)
        {
            for (int i = 0; i < layout.partitions.Count; i++)
            {
                var p = layout.partitions[i];
                Line(new System.Collections.Generic.List<Vector2> { p.start, p.end }, kind == Kind.Partition && selected == i ? Color.yellow : new Color(1, .7f, .3f));
                Marker((p.start + p.end) / 2, p.name, Kind.Partition, i);
            }
            if (tool != Tool.Partition) return false;
            bool onPlane = new Plane(layout.transform.up, layout.transform.position).Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out _);
            if (!e.alt) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (onPlane && pending.Count > 0) Line(new System.Collections.Generic.List<Vector2> { pending[0], hover }, Color.green);
            if (onPlane) { Handles.color = Color.green; Handles.DrawWireDisc(layout.World(hover, .05f), layout.transform.up, .12f); }
            if (onPlane && e.type == EventType.MouseDown && e.button == 0 && !e.alt) { PlacePartitionPoint(); e.Use(); }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { pending.Clear(); tool = Tool.Select; Refresh(); e.Use(); }
            if (e.type == EventType.MouseMove) view.Repaint();
            return true;
        }
    }
}
