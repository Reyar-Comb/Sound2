using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sound2.LevelEditing.Editor
{
    public sealed partial class LevelDesignerWindow
    {
        [SerializeField] GameObject propPrefab;
        [SerializeField] GameObject selectedProp;
        [SerializeField] PropSurface propSurface;
        [SerializeField] float propOffset = .01f;
        [SerializeField] float propAngle;

        void ChooseProp(GameObject prefab)
        {
            if (prefab != null && !PrefabUtility.IsPartOfPrefabAsset(prefab))
            { status = "请选择 Project 中的 Prefab 资源。"; hasError = true; UpdateStatus(); return; }
            propPrefab = prefab;
            var metadata = prefab != null ? prefab.GetComponent<LevelProp>() : null;
            propSurface = metadata != null ? metadata.surface : PropSurface.Floor;
            propOffset = metadata != null ? metadata.surfaceOffset : .01f;
            propAngle = 0;
            tool = Tool.Prop;
            pending.Clear();
            Refresh(); SceneView.RepaintAll();
        }

        void DrawPropSettings(VisualElement area)
        {
            var section = new Foldout { text = "道具 / Props", value = tool == Tool.Prop };
            area.Add(section);
            var row = Row(section);
            Button(row, "钟表（贴墙）", () => ChooseProp(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TickingClock.prefab")));
            Button(row, "球（落地）", () => ChooseProp(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab")));
            Button(row, "闹钟（可摔碎）", () => ChooseProp(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupAlarmClock.prefab")));
            Button(row, "玻璃杯（可摔碎）", () => ChooseProp(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupGlassMug.prefab")));
            var prefab = new ObjectField("道具 Prefab") { objectType = typeof(GameObject), allowSceneObjects = false, value = propPrefab };
            prefab.RegisterValueChangedCallback(e => ChooseProp((GameObject)e.newValue)); section.Add(prefab);
            var surface = new PopupField<string>("吸附表面", new System.Collections.Generic.List<string> { "地面", "墙面" }, (int)propSurface);
            surface.RegisterValueChangedCallback(e => { propSurface = (PropSurface)surface.index; SceneView.RepaintAll(); }); section.Add(surface);
            Float(section, "离表面距离", propOffset, v => { propOffset = Mathf.Max(0, v); SceneView.RepaintAll(); });
            Float(section, "面内旋转角度", propAngle, v => { propAngle = v; SceneView.RepaintAll(); });
            section.Add(new Label("道具保存在 Props 下，随场景保存和导出；重建几何不会删除。") { style = { whiteSpace = WhiteSpace.Normal } });
            section.SetEnabled(!Application.isPlaying);
        }

        void DrawPropObjects(VisualElement area)
        {
            var props = LevelPropPlacement.PropsRoot(layout);
            if (props == null) return;
            foreach (Transform child in props)
            {
                var instance = child.gameObject;
                var button = Button(area, "◇  " + instance.name, () =>
                {
                    selectedProp = instance; selected = -1; kind = Kind.None; tool = Tool.Select;
                    Selection.activeGameObject = instance; Refresh(); SceneView.RepaintAll();
                });
                button.AddToClassList("item");
                button.EnableInClassList("selected-item", selectedProp == instance);
            }
        }

        void SelectPropFromHierarchy()
        {
            if (Selection.activeGameObject == null) return;
            var props = LevelPropPlacement.PropsRoot(layout);
            if (props == null) return;
            var candidate = Selection.activeGameObject.transform;
            while (candidate != null && candidate.parent != props) candidate = candidate.parent;
            if (candidate == null) return;
            selectedProp = candidate.gameObject; kind = Kind.None; selected = -1;
            Refresh();
        }

        bool DrawPropProperties(VisualElement area)
        {
            if (layout == null || selectedProp == null || selectedProp.transform.parent != LevelPropPlacement.PropsRoot(layout)) return false;
            var instance = selectedProp;
            var transform = instance.transform;
            area.Add(new Label("道具：" + instance.name));
            void Vector3Field(string label, Vector3 value, System.Action<Vector3> setter)
            {
                var field = new Vector3Field(label) { value = value };
                field.Query<FloatField>().ForEach(f => f.isDelayed = true);
                field.RegisterValueChangedCallback(e =>
                {
                    if (Application.isPlaying || instance == null) return;
                    Undo.RecordObject(transform, "调整关卡道具"); setter(e.newValue);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
                    EditorSceneManager.MarkSceneDirty(instance.scene); SceneView.RepaintAll();
                });
                area.Add(field);
            }
            Vector3Field("局部位置", transform.localPosition, v => transform.localPosition = v);
            Vector3Field("局部旋转", transform.localEulerAngles, v => transform.localEulerAngles = v);
            Vector3Field("局部缩放", transform.localScale, v => transform.localScale = v);
            var clock = instance.GetComponent<TickingClock>();
            if (clock != null)
                Float(area, "Tick 间隔（秒）", clock.tickInterval, v =>
                {
                    Undo.RecordObject(clock, "调整钟表速度"); clock.tickInterval = Mathf.Max(.05f, v);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(clock);
                    EditorSceneManager.MarkSceneDirty(instance.scene);
                });
            Button(area, "在 Scene 中框选", () => { Selection.activeGameObject = instance; SceneView.lastActiveSceneView?.FrameSelected(); });
            Button(area, "删除此道具", () =>
            {
                Undo.DestroyObjectImmediate(instance); selectedProp = null;
                EditorSceneManager.MarkSceneDirty(layout.gameObject.scene); Refresh();
            });
            area.SetEnabled(!Application.isPlaying);
            return true;
        }

        void PropSceneGUI(SceneView view, Ray ray, Event e)
        {
            if (!e.alt) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            bool valid = LevelPropPlacement.TryPose(layout, propPrefab, ray, propSurface, propOffset, propAngle, out var pose);
            if (valid)
            {
                var rootMatrix = Matrix4x4.TRS(pose.position, pose.rotation, propPrefab.transform.localScale);
                if (e.type == EventType.Repaint) foreach (var filter in propPrefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    var matrix = rootMatrix * propPrefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    if (!filter.sharedMesh.isReadable)
                    {
                        using (new Handles.DrawingScope(new Color(.3f, 1, .65f), matrix))
                            Handles.DrawWireCube(filter.sharedMesh.bounds.center, filter.sharedMesh.bounds.size);
                        continue;
                    }
                    var vertices = filter.sharedMesh.vertices;
                    var triangles = filter.sharedMesh.triangles;
                    var lines = new Vector3[triangles.Length * 2];
                    for (int i = 0; i < triangles.Length; i += 3)
                        for (int j = 0; j < 3; j++)
                        {
                            lines[(i + j) * 2] = vertices[triangles[i + j]];
                            lines[(i + j) * 2 + 1] = vertices[triangles[i + (j + 1) % 3]];
                        }
                    using (new Handles.DrawingScope(new Color(.3f, 1, .65f), matrix)) Handles.DrawLines(lines);
                }
                Handles.Label(pose.position, "点击放置 " + propPrefab.name);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (valid)
                {
                    selectedProp = LevelPropPlacement.Place(layout, propPrefab, pose);
                    selected = -1; kind = Kind.None;
                    status = "已放置 " + selectedProp.name + "；继续点击放置，Esc 退出。"; hasError = false;
                    Refresh();
                }
                else { status = "请指向布局中匹配的墙面或地面；贴墙时使用 3D 视图。"; hasError = true; UpdateStatus(); }
                e.Use();
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                tool = Tool.Select;
                if (selectedProp != null) Selection.activeGameObject = selectedProp;
                Refresh(); e.Use();
            }
            if (e.type == EventType.MouseMove) view.Repaint();
        }
    }
}
