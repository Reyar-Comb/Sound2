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
        [SerializeField] PropSurface repositionSurface;
        [SerializeField] float repositionOffset;
        [SerializeField] float repositionAngle;

        void ChooseProp(GameObject prefab)
        {
            // Selection may predate opening/reloading the window, so no selectionChanged
            // event was delivered. Resolve its level before starting a placement stroke.
            var selectedLayout = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<LevelLayout>(true) : null;
            if (selectedLayout != null && selectedLayout != layout && !EditorUtility.IsPersistent(selectedLayout))
                Bind(selectedLayout);
            if (prefab != null && !PrefabUtility.IsPartOfPrefabAsset(prefab))
            { status = "请选择 Project 中的 Prefab 资源。"; hasError = true; UpdateStatus(); return; }
            if (SceneInstance && prefab != null && prefab.GetComponent<EmbeddedDoor>() != null)
            { status = "嵌入门需要更新墙体，请点击“打开源关卡 Prefab”后放置。"; hasError = true; UpdateStatus(); return; }
            propPrefab = prefab;
            var metadata = prefab != null ? prefab.GetComponent<LevelProp>() : null;
            propSurface = metadata != null ? metadata.surface : PropSurface.Floor;
            propOffset = metadata != null ? metadata.surfaceOffset : .01f;
            propAngle = 0;
            tool = Tool.Prop;
            pending.Clear();
            var settings = rootVisualElement.Q<Foldout>("settings-foldout");
            if (settings != null) settings.value = true;
            Refresh(); SceneView.RepaintAll();
        }

        void DrawPropSettings(VisualElement area)
        {
            var section = new Foldout { text = "道具 / Props", value = tool == Tool.Prop };
            area.Add(section);
            Button(section, "选择道具…", () => PropBrowserWindow.Open(prefab => { if (this != null) ChooseProp(prefab); })).name = "open-prop-browser";
            section.Add(new Label("当前道具：" + PropCatalog.Name(propPrefab)) { name = "selected-prop-name" });
            if (propPrefab != null) section.Add(new Label("分类：" + PropCatalog.Category(propPrefab)));
            if (propPrefab != null && propPrefab.GetComponent<EmbeddedDoor>() != null)
                section.Add(new Label(EmbeddedDoorPlacement.Help) { style = { whiteSpace = WhiteSpace.Normal } });
            else
            {
                var surface = new PopupField<string>("吸附表面", new System.Collections.Generic.List<string> { "地面", "墙面" }, (int)propSurface);
                surface.RegisterValueChangedCallback(e => { propSurface = (PropSurface)surface.index; SceneView.RepaintAll(); }); section.Add(surface);
                Float(section, "离表面距离", propOffset, v => { propOffset = Mathf.Max(0, v); SceneView.RepaintAll(); });
                Float(section, "面内旋转角度", propAngle, v => { propAngle = v; SceneView.RepaintAll(); });
            }
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
            if (tool != Tool.RepositionProp || selectedProp != candidate.gameObject) tool = Tool.Select;
            selectedProp = candidate.gameObject; kind = Kind.None; selected = -1;
            Refresh();
        }

        bool DrawPropProperties(VisualElement area)
        {
            if (layout == null || selectedProp == null || selectedProp.transform.parent != LevelPropPlacement.PropsRoot(layout)) return false;
            var instance = selectedProp;
            area.Add(new Label("道具：" + instance.name));
            if (SceneInstance && instance.GetComponent<EmbeddedDoor>() != null)
            {
                area.Add(new HelpBox("此门与墙洞关联，请打开源关卡 Prefab 编辑。", HelpBoxMessageType.Info));
                return true;
            }
            bool moving = tool == Tool.RepositionProp;
            Button(area, moving ? "取消重新吸附（Esc）" : "重新吸附到新位置", () =>
            {
                if (moving) CancelReposition(); else BeginReposition(instance);
            }).AddToClassList("primary");
            bool embeddedDoor = instance.GetComponent<EmbeddedDoor>() != null;
            var door = instance.GetComponent<EmbeddedDoor>();
            if (door != null && string.IsNullOrEmpty(door.partitionId))
                area.Add(new Label("此门尚未绑定隔板，请重新吸附到隔板或删除。"));
            if (embeddedDoor) area.Add(new Label("门仅嵌入隔板，自动落地并匹配隔板厚度；移动请使用重新吸附。") { style = { whiteSpace = WhiteSpace.Normal } });
            if (moving && !embeddedDoor)
            {
                var surface = new PopupField<string>("吸附表面", new System.Collections.Generic.List<string> { "地面", "墙面" }, (int)repositionSurface);
                surface.RegisterValueChangedCallback(e => { repositionSurface = (PropSurface)surface.index; SceneView.RepaintAll(); }); area.Add(surface);
                Float(area, "离表面距离", repositionOffset, v => { repositionOffset = Mathf.Max(0, v); SceneView.RepaintAll(); });
                Float(area, "面内旋转角度", repositionAngle, v => { repositionAngle = v; SceneView.RepaintAll(); });
                area.Add(new Label("绿色线框为新位置。左键确认；Esc 取消，原道具保持不变。"));
            }
            Button(area, "在 Scene 中框选", () => { Selection.activeGameObject = instance; SceneView.lastActiveSceneView?.FrameSelected(); });
            Button(area, "删除此道具", () =>
            {
                if (tool == Tool.RepositionProp) tool = Tool.Select;
                Undo.DestroyObjectImmediate(instance); selectedProp = null;
                if (embeddedDoor) LevelBuilder.Rebuild(layout);
                EditorSceneManager.MarkSceneDirty(layout.gameObject.scene); Refresh();
            });
            var serializedInstance = new SerializedObject(instance);
            var name = new PropertyField(serializedInstance.FindProperty("m_Name"), "名称");
            name.Bind(serializedInstance);
            name.RegisterValueChangeCallback(_ => DrawObjects()); area.Add(name);
            var placement = new Foldout { text = "摆放", value = true };
            area.Add(placement);
            placement.SetEnabled(!embeddedDoor);
            var transform = instance.transform;
            var serializedTransform = new SerializedObject(transform);
            AddPropField(placement, serializedTransform, "m_LocalPosition", "位置（局部）");
            var rotation = new Vector3Field("旋转（度）") { value = transform.localEulerAngles };
            rotation.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(transform, "旋转道具");
                transform.localEulerAngles = e.newValue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
                EditorSceneManager.MarkSceneDirty(instance.scene);
                SceneView.RepaintAll();
            });
            rotation.TrackPropertyValue(serializedTransform.FindProperty("m_LocalRotation"),
                _ => rotation.SetValueWithoutNotify(transform.localEulerAngles));
            placement.Add(rotation);
            AddPropField(placement, serializedTransform, "m_LocalScale", "缩放");
            DrawPropDimensions(area, instance.GetComponent<ResizableTable>(), "桌子尺寸",
                ("width", "宽度（米）"), ("depth", "深度（米）"), ("height", "高度（米）"),
                ("topThickness", "桌面厚度（米）"), ("legThickness", "桌腿粗细（米）"), ("legInset", "桌腿内缩（米）"));
            DrawPropDimensions(area, instance.GetComponent<ResizableBar>(), "吧台尺寸", ("length", "长度（米）"));
            DrawPropDimensions(area, instance.GetComponent<ResizableWardCurtain>(), "病房隔帘尺寸（保持褶皱尺度）", ("length", "长度（米）"));
            DrawRingTableSettings(area, instance.GetComponent<DrapedRingTable>());
            var interaction = instance.GetComponent<DoorInteraction>();
            if (interaction != null)
            {
                DrawPropDimensions(area, interaction, "门的通行与传送", ("openingSide", "允许开门的方向"),
                    ("teleportDestination", "开门传送到"), ("autoCloseDelay", "自动关门等待（秒，0 关闭）"),
                    ("lockedShakeAngle", "锁门晃动角度"), ("lockedShakeDuration", "锁门晃动时长（秒）"));
                area.Add(new Label("正面为门的本地 +Z；绿色箭头指向允许开门的一侧，青线连接目标门。\n成功开门时立即传送，保留门前相对位置和朝向；目标门继续同一开门动作。目标留空为普通门，双向传送需两扇门分别设置目标。\n自动关门从完全打开后计时，受阻时暂停关门。两处门周围的布局应保持相近。")
                    { style = { whiteSpace = WhiteSpace.Normal } });
                var issue = new HelpBox("", HelpBoxMessageType.Warning);
                area.Add(issue);
                void UpdateDoorIssue()
                {
                    if (interaction == null) return;
                    string message = interaction.TeleportConfigurationIssue();
                    issue.text = message ?? "";
                    issue.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
                }
                UpdateDoorIssue();
                issue.schedule.Execute(UpdateDoorIssue).Every(500);
            }
            var vines = instance.GetComponent<MonsterVineArea>();
            if (vines != null)
            {
                DrawPropDimensions(area, vines, "红色藤蔓矩形范围", ("width", "宽度（米）"), ("depth", "长度（米）"),
                    ("height", "攀爬高度（米）"), ("growthSources", "每次生长源数量"));
                DrawPropDimensions(area, instance.GetComponent<MonsterVines>(), "藤蔓生长", ("growthInterval", "生长间隔（秒）"),
                    ("lifetime", "存活时间（秒）"), ("reach", "枝条长度（米）"), ("surfaces", "附着表面层"));
                var motion = instance.GetComponent<MonsterVineAreaMotion>();
                if (motion == null)
                    Button(area, "添加起终点移动", () => { Undo.AddComponent<MonsterVineAreaMotion>(instance); Refresh(); });
                else
                {
                    DrawPropDimensions(area, motion, "藤蔓区移动", ("startPosition", "起点（父物体局部坐标）"),
                        ("endPosition", "终点（父物体局部坐标）"), ("speed", "速度（米/秒）"), ("pingPong", "往返移动"));
                    Button(area, "当前位置设为起点", () => { MonsterVineAreaMotionEditor.SetEndpoint(motion, true, instance.transform.position); Refresh(); });
                    Button(area, "当前位置设为终点", () => { MonsterVineAreaMotionEditor.SetEndpoint(motion, false, instance.transform.position); Refresh(); });
                }
                area.Add(new Label("红框为显示范围，运行时隐藏。藤蔓只附着范围内的真实表面，不产生碰撞。") { style = { whiteSpace = WhiteSpace.Normal } });
            }
            area.SetEnabled(!Application.isPlaying);
            return true;
        }

        static void AddPropField(VisualElement area, SerializedObject target, string property, string label)
        {
            var field = new PropertyField(target.FindProperty(property), label);
            field.Bind(target);
            area.Add(field);
        }

        static void DrawPropDimensions(VisualElement area, Component component, string title,
            params (string property, string label)[] fields)
        {
            if (component == null) return;
            var section = new Foldout { text = title, value = true };
            area.Add(section);
            var target = new SerializedObject(component);
            foreach (var field in fields) AddPropField(section, target, field.property, field.label);
        }

        void BeginReposition(GameObject instance)
        {
            if (Application.isPlaying || layout == null || instance == null
                || instance.transform.parent != LevelPropPlacement.PropsRoot(layout)) return;
            selectedProp = instance; selected = -1; kind = Kind.None;
            Selection.activeGameObject = instance;
            var metadata = instance.GetComponent<LevelProp>();
            repositionSurface = metadata != null ? metadata.surface : PropSurface.Floor;
            repositionOffset = metadata != null ? metadata.surfaceOffset : .01f;
            var t = instance.transform;
            if (repositionSurface == PropSurface.Wall)
            {
                var upright = Vector3.ProjectOnPlane(layout.transform.up, t.forward);
                repositionAngle = upright.sqrMagnitude > .0001f ? Vector3.SignedAngle(upright, t.up, t.forward) : 0;
            }
            else
            {
                var forward = Quaternion.FromToRotation(layout.transform.up, t.up) * layout.transform.forward;
                repositionAngle = Vector3.SignedAngle(forward, t.forward, t.up);
            }
            tool = Tool.RepositionProp; pending.Clear();
            status = "正在重新吸附 " + instance.name + "：点击新表面确认，Esc 取消。"; hasError = false;
            Refresh(); SceneView.RepaintAll();
        }

        void CancelReposition()
        {
            tool = Tool.Select;
            status = "已取消重新吸附，原道具保持不变。"; hasError = false;
            Refresh(); SceneView.RepaintAll();
        }

        void PropSceneGUI(SceneView view, Ray ray, Event e)
        {
            bool moving = tool == Tool.RepositionProp;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (moving) CancelReposition();
                else
                {
                    tool = Tool.Select;
                    if (selectedProp != null) Selection.activeGameObject = selectedProp;
                    Refresh();
                }
                e.Use(); return;
            }
            var source = moving ? selectedProp : propPrefab;
            if (moving && (source == null || source.transform.parent != LevelPropPlacement.PropsRoot(layout)))
            { CancelReposition(); return; }
            if (!e.alt) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            bool valid = LevelPropPlacement.TryPose(layout, source, ray,
                moving ? repositionSurface : propSurface, moving ? repositionOffset : propOffset,
                moving ? repositionAngle : propAngle, out var pose);
            bool overlapping = valid && LevelPropOverlap.HasOverlap(layout, source, pose);
            if (valid)
            {
                var scale = LevelPropOverlap.PreviewScale(layout, source);
                var previewColor = overlapping ? Color.red : new Color(.3f, 1, .65f);
                var rootMatrix = Matrix4x4.TRS(pose.position, pose.rotation, scale);
                if (e.type == EventType.Repaint) foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    var matrix = rootMatrix * source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    if (!filter.sharedMesh.isReadable)
                    {
                        using (new Handles.DrawingScope(previewColor, matrix))
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
                    using (new Handles.DrawingScope(previewColor, matrix)) Handles.DrawLines(lines);
                }
                if (!overlapping) Handles.Label(pose.position, (moving ? "点击重新吸附 " : "点击放置 ") + source.name);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (overlapping) { e.Use(); return; }
                if (valid)
                {
                    if (moving)
                    {
                        LevelPropPlacement.Reposition(layout, source, pose);
                        tool = Tool.Select;
                        status = "已重新吸附 " + source.name + "；Ctrl+Z 撤销，Ctrl+Y 重做。";
                    }
                    else
                    {
                        selectedProp = LevelPropPlacement.Place(layout, propPrefab, pose);
                        status = "已放置 " + selectedProp.name + "；继续点击放置，Esc 退出。";
                    }
                    selected = -1; kind = Kind.None;
                    hasError = false;
                    Refresh();
                }
                else { status = source != null && source.GetComponent<EmbeddedDoor>() != null ? EmbeddedDoorPlacement.Help : "请指向布局中匹配的墙面或地面；贴墙时使用 3D 视图。"; hasError = true; UpdateStatus(); }
                e.Use();
            }
            if (e.type == EventType.MouseMove) view.Repaint();
        }
    }
}
