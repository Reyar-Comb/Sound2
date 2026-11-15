using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sound2.LevelEditing.Editor
{
    public sealed partial class LevelDesignerWindow
    {
        [SerializeField] LevelEnemy selectedEnemy;
        [SerializeField] float enemyPatrolRadius = 6;

        void DrawEnemySettings(VisualElement area)
        {
            var fold = new Foldout { text = "敌人与寻路", value = tool == Tool.Enemy }; area.Add(fold);
            fold.SetEnabled(!Application.isPlaying);
            Float(fold, "最高攀爬台阶（米）", layout.enemyStepHeight, v =>
            {
                if (!float.IsFinite(v)) return;
                Undo.RecordObject(layout, "设置关卡攀爬高度");
                layout.enemyStepHeight = Mathf.Clamp(v, 0, 1.8f); LevelEnemyPlacement.Dirty(layout);
                EditorSceneManager.MarkSceneDirty(layout.gameObject.scene); Refresh();
            });
            Button(fold, "一键应用到本关全部敌人", () =>
            {
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("应用关卡敌人设置");
                LevelEnemyPlacement.ConfigureNavigation(layout); Undo.CollapseUndoOperations(group);
                status = $"本关敌人共用导航；最大台阶 {layout.enemyStepHeight:0.##} 米，下次运行时自动重建。";
                hasError = false; UpdateStatus();
            });
            fold.Add(new HelpBox("默认 0.3 米，范围 0–1.8 米。限制单级台阶，不限制连续楼梯的总高度；斜坡仍按 45° 通行。运行时自动生成导航，无需手工烘焙。", HelpBoxMessageType.Info));
            if (tool == Tool.Enemy)
                Float(fold, "新敌人巡逻半径（米）", enemyPatrolRadius, v => { if (float.IsFinite(v)) enemyPatrolRadius = Mathf.Max(1, v); });
        }

        void DrawEnemyObjects(VisualElement area)
        {
            var enemies = layout.GetComponentsInChildren<LevelEnemy>(true);
            var fold = new Foldout { text = $"敌人 ({enemies.Length})", value = true }; area.Add(fold);
            foreach (var enemy in enemies)
            {
                if (enemy.GetComponentInParent<LevelLayout>() != layout) continue;
                var button = Button(fold, "●  " + enemy.name, () =>
                {
                    SelectEnemy(enemy); Selection.activeGameObject = enemy.gameObject;
                });
                button.AddToClassList("item"); button.EnableInClassList("selected-item", selectedEnemy == enemy);
            }
        }

        void SelectEnemy(LevelEnemy enemy)
        {
            selectedEnemy = enemy; selectedProp = null; selected = -1; kind = Kind.None;
            tool = Tool.Select; pending.Clear(); Refresh(); SceneView.RepaintAll();
        }

        void EditEnemy(UnityEngine.Object target, Action edit)
        {
            if (Application.isPlaying || target == null) return;
            Undo.RecordObject(target, "编辑敌人"); edit(); LevelEnemyPlacement.Dirty(target);
            EditorSceneManager.MarkSceneDirty(layout.gameObject.scene); SceneView.RepaintAll();
        }

        bool DrawEnemyProperties(VisualElement area)
        {
            if (selectedEnemy == null || layout == null || selectedEnemy.GetComponentInParent<LevelLayout>() != layout) return false;
            var enemy = selectedEnemy;
            var name = new TextField("名称") { value = enemy.name, isDelayed = true };
            name.RegisterValueChangedCallback(e => { EditEnemy(enemy.gameObject, () => enemy.name = e.newValue); DrawObjects(); }); area.Add(name);
            var position = new Vector3Field("关卡内位置") { value = enemy.transform.localPosition };
            position.Query<FloatField>().ForEach(f => f.isDelayed = true);
            position.RegisterValueChangedCallback(e => EditEnemy(enemy.transform, () => enemy.transform.localPosition = e.newValue)); area.Add(position);
            Float(area, "巡逻半径（米）", enemy.patrolRadius, v => { if (float.IsFinite(v)) EditEnemy(enemy, () => enemy.patrolRadius = Mathf.Max(1, v)); });
            if (enemy.actor != null)
            {
                Float(area, "巡逻速度", enemy.actor.patrolSpeed, v => EditEnemy(enemy.actor, () => enemy.actor.patrolSpeed = Mathf.Max(.1f, v)));
                Float(area, "调查速度", enemy.actor.investigateSpeed, v => EditEnemy(enemy.actor, () => enemy.actor.investigateSpeed = Mathf.Max(.1f, v)));
                Float(area, "听觉灵敏度", enemy.actor.hearingSensitivity, v => EditEnemy(enemy.actor, () => enemy.actor.hearingSensitivity = Mathf.Max(0, v)));
            }
            area.Add(new HelpBox("巡逻点会从附近可达地面自动挑选。没有可达巡逻点时留在原地，仍可听声调查。敌人只通过扫描、藤蔓与音效显现。", HelpBoxMessageType.Info));
            Button(area, "删除敌人", () => { Undo.DestroyObjectImmediate(enemy.gameObject); selectedEnemy = null; Refresh(); });
            return true;
        }

        bool EnemySceneGUI(SceneView view, Ray ray, Event e)
        {
            if (tool == Tool.Enemy)
            {
                if (!e.alt) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                bool valid = LevelEnemyPlacement.Ground(layout, ray, out var point);
                if (valid)
                {
                    var local = layout.transform.InverseTransformPoint(point);
                    var snapped = Snap(new Vector2(local.x, local.z));
                    var candidate = layout.World(snapped, local.y);
                    valid = LevelEnemyPlacement.Ground(layout, new Ray(candidate + Vector3.up * .1f, Vector3.down), out point)
                        && Mathf.Abs(point.y - candidate.y) < .2f && LevelEnemyPlacement.HasClearance(point);
                }
                Handles.color = valid ? new Color(1, .3f, .3f) : Color.gray;
                if (valid)
                {
                    Handles.DrawWireDisc(point, Vector3.up, .35f);
                    Handles.DrawWireDisc(point + Vector3.up * 1.8f, Vector3.up, .35f);
                    Handles.DrawLine(point, point + Vector3.up * 1.8f);
                    Handles.Label(point + Vector3.up * 2, "巡逻敌人");
                }
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    if (valid)
                    {
                        LevelEnemyPlacement.Place(layout, point, enemyPatrolRadius);
                        status = "已放置敌人；继续点击放置，Esc 退出。"; hasError = false; Refresh();
                    }
                    else { status = "请选本关静态地面，并为敌人留出足够空间（远离墙壁、坑洞和低矮顶面）。"; hasError = true; UpdateStatus(); }
                    e.Use();
                }
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { tool = Tool.Select; Refresh(); e.Use(); }
                if (e.type == EventType.MouseMove) view.Repaint();
                return true;
            }
            if (tool != Tool.Select) return false;
            foreach (var enemy in layout.GetComponentsInChildren<LevelEnemy>(true))
            {
                var position = enemy.transform.position + Vector3.up * .9f;
                Handles.color = enemy == selectedEnemy ? Color.yellow : new Color(1, .3f, .3f);
                Handles.Label(position + Vector3.up * .5f, enemy.name);
                float size = HandleUtility.GetHandleSize(position) * .08f;
                if (enemy != selectedEnemy && Handles.Button(position, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                    SelectEnemy(enemy);
            }
            if (selectedEnemy == null) return false;
            EditorGUI.BeginChangeCheck();
            var moved = Handles.PositionHandle(selectedEnemy.transform.position, layout.transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                EditEnemy(selectedEnemy.transform, () => selectedEnemy.transform.position = moved); DrawProperties();
            }
            return true;
        }
    }
}
