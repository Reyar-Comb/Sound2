using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sound2.LevelEditing.Editor
{
    public sealed partial class LevelDesignerWindow
    {
        bool ringEraseActive;
        float ringBrushAngle = 15;
        float ringGapStart, ringGapSweep = 45;
        float lastRingAngle;
        int ringStrokeGroup = -1;
        DrapedRingTable ringBrushTarget;

        void DrawRingTableSettings(VisualElement area, DrapedRingTable table)
        {
            if (table == null) return;
            var section = new Foldout { text = "圆环桌与桌布", value = true, name = "ring-table-settings" }; area.Add(section);
            var serialized = new SerializedObject(table);
            foreach (var pair in new[] { ("innerRadius", "内半径（米）"), ("outerRadius", "外半径（米）"), ("height", "桌高（米）"), ("foldDepth", "褶皱深度（米）"), ("clothSeed", "褶皱种子") })
                AddPropField(section, serialized, pair.Item1, pair.Item2);
            section.Add(new Label("内外侧桌布均拖地。修改种子可更换不等距褶皱。缺口同时移除桌面、桌布和碰撞。") { style = { whiteSpace = WhiteSpace.Normal } });
            Float(section, "缺口起点（度）", ringGapStart, value => ringGapStart = value);
            Float(section, "擦除角度（度）", ringGapSweep, value => ringGapSweep = Mathf.Clamp(value, .1f, 360));
            Button(section, "擦除指定区段", () => ChangeRing(table, "擦除圆环桌区段", () => table.Erase(ringGapStart, ringGapSweep))).name = "ring-erase-range";
            Float(section, "画刷宽度（度）", ringBrushAngle, value => ringBrushAngle = Mathf.Clamp(value, .1f, 180));
            Button(section, ringEraseActive && ringBrushTarget == table ? "退出擦除画刷（Esc）" : "在 Scene 中擦除缺口", () =>
            {
                EndRingStroke(); ringEraseActive = !(ringEraseActive && ringBrushTarget == table); ringBrushTarget = table;
                tool = Tool.Select; Refresh(); SceneView.RepaintAll();
            }).name = "ring-erase-brush";
            section.Add(new Label("画刷：左键拖动擦除，Esc 退出，Ctrl+Z 撤销整笔。0° 从本地 +X 朝 +Z 方向增加，可跨越 360°。") { style = { whiteSpace = WhiteSpace.Normal } });
            AddPropField(section, serialized, "gaps", "已擦除区段（可修改或删除以恢复）");
            Button(section, "恢复完整圆环", () => ChangeRing(table, "恢复圆环桌", () => { table.gaps.Clear(); table.Rebuild(); })).name = "ring-restore";
        }
        static void ChangeRing(DrapedRingTable table, string label, System.Action action)
        {
            Undo.RecordObject(table, label); action(); Undo.FlushUndoRecordObjects();
            PrefabUtility.RecordPrefabInstancePropertyModifications(table); EditorUtility.SetDirty(table);
            EditorSceneManager.MarkSceneDirty(table.gameObject.scene); SceneView.RepaintAll();
        }
        void EndRingStroke()
        {
            if (ringStrokeGroup < 0) return;
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(ringStrokeGroup); ringStrokeGroup = -1;
        }
        bool RingTableSceneGUI(SceneView view, Ray ray, Event e)
        {
            if (!ringEraseActive) return false;
            var table = selectedProp != null ? selectedProp.GetComponent<DrapedRingTable>() : null;
            if (table == null || table != ringBrushTarget || tool != Tool.Select)
            { EndRingStroke(); ringEraseActive = false; ringBrushTarget = null; return false; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { EndRingStroke(); ringEraseActive = false; Refresh(); e.Use(); return true; }
            if (e.type == EventType.MouseUp && e.button == 0) { EndRingStroke(); e.Use(); return true; }
            if (!e.alt) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var plane = new Plane(table.transform.up, table.transform.TransformPoint(new Vector3(0, table.height, 0)));
            if (!plane.Raycast(ray, out float distance)) return true;
            var point = table.transform.InverseTransformPoint(ray.GetPoint(distance));
            float radius = new Vector2(point.x, point.z).magnitude;
            bool onRing = radius >= table.innerRadius - .15f && radius <= table.outerRadius + .2f;
            if (!onRing) { if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint(); return true; }
            float angle = Mathf.Atan2(point.z, point.x) * Mathf.Rad2Deg;
            float first = angle - ringBrushAngle * .5f, last = angle + ringBrushAngle * .5f;
            using (new Handles.DrawingScope(new Color(1, .38f, .18f), table.transform.localToWorldMatrix))
            {
                Vector3 P(float a, float r) => new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, table.height + .02f, Mathf.Sin(a * Mathf.Deg2Rad) * r);
                Handles.DrawWireArc(new Vector3(0, table.height + .02f, 0), Vector3.down, P(first, 1) - new Vector3(0, table.height + .02f, 0), ringBrushAngle, table.outerRadius);
                Handles.DrawWireArc(new Vector3(0, table.height + .02f, 0), Vector3.down, P(first, 1) - new Vector3(0, table.height + .02f, 0), ringBrushAngle, table.innerRadius);
                Handles.DrawLine(P(first, table.innerRadius), P(first, table.outerRadius)); Handles.DrawLine(P(last, table.innerRadius), P(last, table.outerRadius));
            }
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                if (e.type == EventType.MouseDown)
                {
                    Undo.IncrementCurrentGroup(); ringStrokeGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("擦除圆环桌");
                    Undo.RegisterCompleteObjectUndo(table, "擦除圆环桌"); lastRingAngle = angle;
                }
                if (ringStrokeGroup >= 0)
                {
                    float delta = Mathf.DeltaAngle(lastRingAngle, angle);
                    table.Erase(lastRingAngle + Mathf.Min(0, delta) - ringBrushAngle * .5f, Mathf.Min(360, Mathf.Abs(delta) + ringBrushAngle));
                    lastRingAngle = angle; PrefabUtility.RecordPrefabInstancePropertyModifications(table); EditorUtility.SetDirty(table);
                    EditorSceneManager.MarkSceneDirty(table.gameObject.scene);
                }
                e.Use();
            }
            view.Repaint(); return true;
        }
    }
}
