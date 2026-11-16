using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sound2.LevelEditing.Editor
{
    public sealed partial class LevelDesignerWindow : EditorWindow
    {
        enum Tool { Select, Rectangle, Circle, Line, Curve, Pit, Connect, Prop, RepositionProp, Partition, Enemy }
        enum Kind { None, Room, Corridor, Pit, Partition }
        [SerializeField] LevelLayout layout;
        [SerializeField] Tool tool;
        [SerializeField] Kind kind;
        [SerializeField] int selected = -1;
        [SerializeField] float snap = .5f;
        [SerializeField] float newWidth = 3;
        [SerializeField] float newPitSize = 2;
        [SerializeField] bool endConnection;
        [SerializeField] bool includePlayer = true;
        [SerializeField] bool seededMode;
        [SerializeField] int previewFloor;
        [SerializeField] LevelMap seededMap;
        [SerializeField] bool geometryExpanded = true;
        [SerializeField] bool propsExpanded = true;
        bool SceneInstance => LevelBuilder.IsScenePrefabInstance(layout);
        readonly List<Vector2> pending = new List<Vector2>();
        Vector2 hover;
        string status = "新建布局，或选择已有 LevelLayout。";
        bool hasError;

        [MenuItem("Tools/Sound2/关卡编辑器")]
        public static void Open()
        {
            var window = GetWindow<LevelDesignerWindow>("关卡编辑器");
            window.OnSelection();
            if (window.layout == null) window.Bind(window.SceneLayouts().FirstOrDefault(l => l.gameObject.activeInHierarchy)
                ?? window.SceneLayouts().FirstOrDefault());
        }
        public void OpenSourcePrefab()
        {
            var source = SceneInstance ? PrefabUtility.GetCorrespondingObjectFromSource(layout) : layout;
            if (source != null) Bind(source);
        }
        public void Bind(LevelLayout target)
        {
            if (target != null && EditorUtility.IsPersistent(target))
            {
                var stage = PrefabStageUtility.OpenPrefab(AssetDatabase.GetAssetPath(target));
                target = stage.prefabContentsRoot.GetComponent<LevelLayout>();
            }
            seededMode = false;
            tool = Tool.Select;
            layout = target; selectedProp = null; selectedEnemy = null; selected = -1; kind = Kind.None; pending.Clear(); hasError = false;
            InvalidatePropPreview();
            status = layout == null ? "新建布局，或选择已有 LevelLayout。" : $"已载入：{layout.rooms.Count} 房间 · {layout.corridors.Count} 走廊 · {layout.pits.Count} 竖井。";
            Refresh(); Frame();
        }

        void OnEnable()
        {
            minSize = new Vector2(600, 440);
            SceneView.duringSceneGui += SceneGUI;
            Undo.undoRedoPerformed += OnUndo;
            Selection.selectionChanged += OnSelection;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.hierarchyChanged += InvalidatePropPreview;
        }
        void OnDisable()
        {
            EndRingStroke();
            SceneView.duringSceneGui -= SceneGUI;
            Undo.undoRedoPerformed -= OnUndo;
            Selection.selectionChanged -= OnSelection;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.hierarchyChanged -= InvalidatePropPreview;
            DisposePropPreview();
        }
        void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (tool == Tool.RepositionProp) tool = Tool.Select;
            if (state != PlayModeStateChange.EnteredEditMode && state != PlayModeStateChange.EnteredPlayMode) return;
            if (layout == null)
                layout = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<LevelLayout>(true)).FirstOrDefault();
            if (selectedProp == null) selectedProp = null;
            Refresh();
        }
        void OnSelection()
        {
            if (tool == Tool.RepositionProp && (Selection.activeGameObject == null
                || selectedProp == null || !Selection.activeGameObject.transform.IsChildOf(selectedProp.transform)))
            { tool = Tool.Select; Refresh(); SceneView.RepaintAll(); }
            if (Selection.activeGameObject == null) return;
            var map = Selection.activeGameObject.GetComponentInParent<LevelMap>();
            if (map != null) { seededMap = map; seededMode = true; Refresh(); return; }
            var target = Selection.activeGameObject.GetComponentInParent<LevelLayout>(true);
            if (target != null && EditorUtility.IsPersistent(target)) return;
            if (target == null)
            {
                var world = Selection.activeGameObject.GetComponent<LevelWorld>();
                if (world != null) target = world.GetComponentsInChildren<LevelLayout>(true)
                    .FirstOrDefault(l => l.gameObject.activeInHierarchy) ?? world.GetComponentsInChildren<LevelLayout>(true).FirstOrDefault();
            }
            if (target != null && target != layout) Bind(target);
            if (target != null)
            {
                var enemy = Selection.activeGameObject.GetComponentInParent<LevelEnemy>();
                if (enemy != null) { SelectEnemy(enemy); return; }
                selectedEnemy = null; SelectPropFromHierarchy();
            }
        }
        void OnUndo() { if (tool == Tool.RepositionProp) tool = Tool.Select; if (layout != null) { if (!LevelBuilder.IsScenePrefabInstance(layout)) TryRebuild(); Refresh(); } }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/LevelEditor/Editor/LevelEditorWindow.uxml").CloneTree(rootVisualElement);
            Refresh();
        }

        List<LevelLayout> SceneLayouts() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<LevelLayout>(true)).ToList();

        void Refresh()
        {
            if (rootVisualElement.Q("header") == null) return;
            var header = rootVisualElement.Q("header"); header.Clear();
            var mode = new PopupField<string>("生成模式", new List<string> { "独立关卡", "种子程序化" }, seededMode ? 1 : 0);
            mode.RegisterValueChangedCallback(e => { seededMode = mode.index == 1; if (tool == Tool.RepositionProp) tool = Tool.Select; Refresh(); }); header.Add(mode);
            rootVisualElement.Q("workspace").SetEnabled(!seededMode);
            rootVisualElement.Q<Foldout>("settings-foldout").text = seededMode ? "程序化设置" : "工具与生成设置";
            if (seededMode) { DrawSeededMode(); return; }
            var levels = SceneLayouts();
            if (levels.Count > 1)
            {
                var choices = levels.Select((l, i) => $"{i + 1}. {l.name}").ToList();
                var picker = new PopupField<string>("场景关卡", choices, Mathf.Max(0, levels.IndexOf(layout))) { name = "scene-level-picker" };
                picker.RegisterValueChangedCallback(_ => { Bind(levels[picker.index]); Selection.activeGameObject = layout.gameObject; }); header.Add(picker);
            }
            var target = new ObjectField("编辑布局") { objectType = typeof(LevelLayout), allowSceneObjects = true, value = layout };
            target.RegisterValueChangedCallback(e => { Bind((LevelLayout)e.newValue); if (layout != null) Selection.activeGameObject = layout.gameObject; }); header.Add(target);
            bool instance = LevelBuilder.IsScenePrefabInstance(layout);
            foreach (var section in new[] { "tools", "workspace", "settings", "footer" })
                rootVisualElement.Q(section).SetEnabled(true);
            if (instance)
            {
                header.Add(new HelpBox("正在编辑总场景中的关卡：道具和敌人修改随当前场景保存。房间、走廊、隔板和嵌入门请打开源 Prefab 编辑。", HelpBoxMessageType.Info));
                Button(header, "打开源关卡 Prefab", OpenSourcePrefab);
            }
            if (layout != null && !layout.gameObject.activeInHierarchy)
            {
                header.Add(new HelpBox("本关当前未启用，启用后可在 Scene 中放置道具。", HelpBoxMessageType.Info));
                Button(header, "启用本关以编辑", () =>
                {
                    for (var t = layout.transform; t != null; t = t.parent)
                    {
                        if (t.gameObject.activeSelf) continue;
                        Undo.RecordObject(t.gameObject, "启用关卡编辑"); t.gameObject.SetActive(true);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                    }
                    EditorSceneManager.MarkSceneDirty(layout.gameObject.scene); Refresh(); Frame();
                }).SetEnabled(!Application.isPlaying);
            }
            Toggle(header, "道具原色预览", propColorPreview, v => { propColorPreview = v; SceneView.RepaintAll(); });
            var row = Row(header);
            Button(row, "新建独立场景", CreateLayout);
            Button(row, "俯视 / 框选全部", Frame);
            Button(row, "3D 检查", () =>
            {
                var view = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
                view.in2DMode = false;
                view.orthographic = false;
                view.rotation = Quaternion.Euler(45, -30, 0);
                view.Repaint();
            });
            var tools = rootVisualElement.Q("tools"); tools.Clear();
            string[] names = { "选择 / 编辑", "矩形房间", "圆柱房间", "直线走廊", "曲线走廊", "方形竖井", "连接端点", "放置道具", "隔板", "放置敌人" };
            for (int i = 0; i < names.Length; i++)
            {
                Tool value = i == 9 ? Tool.Enemy : i == 8 ? Tool.Partition : (Tool)i;
                var button = Button(tools, names[i], () =>
                {
                    tool = value; pending.Clear(); selectedEnemy = null;
                    if (value == Tool.Enemy) { selectedProp = null; kind = Kind.None; selected = -1; rootVisualElement.Q<Foldout>("settings-foldout").value = true; }
                    if (value == Tool.Partition) { selectedProp = null; kind = Kind.None; selected = -1; }
                    if (value == Tool.Prop) rootVisualElement.Q<Foldout>("settings-foldout").value = true;
                    Refresh(); SceneView.RepaintAll();
                });
                button.EnableInClassList("active-tool", tool == value);
                button.SetEnabled(!instance || value == Tool.Select || value == Tool.Prop || value == Tool.Enemy);
            }
            tools.SetEnabled(layout != null && !Application.isPlaying);
            rootVisualElement.Q<Label>("instructions").text = tool switch
            {
                Tool.Line => "依次点击直线走廊的控制点，各点以直线连接。Enter 完成；Backspace 撤回一点，Esc 取消。",
                Tool.Curve => "依次点击曲线经过的控制点，Enter 完成；Backspace 撤回一点，Esc 取消。",
                Tool.Connect => "先从对象列表选中走廊，选择起点/终点，再点击房间边缘。连接点会跟随房间移动和缩放。",
                Tool.Partition => "依次点击隔板起点和终点。隔板可放在任意位置；Esc 取消。选中隔板后拖动中心平移，拖动端点改变长度和方向。",
                Tool.Pit => "点击地面放置竖井。橙色轮廓是实际开口；管壁必须完整位于关卡内部。",
                Tool.Prop => "打开道具浏览器，选择预览卡片后在 Scene 点击放置。墙面道具请切到 3D 检查。Esc 退出放置。",
                Tool.Enemy => "在本关地面点击放置巡逻敌人。导航、巡逻、听声调查、扫描、藤蔓和音效自动配置；Esc 退出。",
                Tool.RepositionProp => "重新吸附：在 Scene 指向新表面，左键确认移动原道具；Esc 取消。贴墙请切到 3D 检查。",
                Tool.Rectangle or Tool.Circle => "点击 Scene 放置房间中心，切换选择工具后拖动中心与尺寸手柄。",
                _ => "点击对象标记选择；拖动控制点调整形状。Ctrl+Z 撤销，Ctrl+Y 重做。"
            };
            DrawSettings(); DrawObjects(); DrawProperties();
            var footer = rootVisualElement.Q("footer"); footer.Clear();
            Button(footer, "重新生成", () => { TryRebuild(); Refresh(); }).SetEnabled(layout != null && !instance);
            Button(footer, "保存当前场景", Save).SetEnabled(layout != null);
            Button(footer, "性能分析 / 优化", () => LevelPerformanceWindow.Open(layout)).SetEnabled(layout != null && !Application.isPlaying);
            var export = Button(footer, "导出 .unity 场景", Export); export.AddToClassList("primary"); export.SetEnabled(!instance);
            Button(footer, "导出关卡 Prefab", ExportLevelPrefab).SetEnabled(layout != null && !instance && !Application.isPlaying);
            UpdateStatus();
        }

        void DrawSettings()
        {
            var area = rootVisualElement.Q("settings"); area.Clear();
            Float(area, "网格吸附（0 = 关闭）", snap, v => snap = Mathf.Max(0, v));
            Float(area, "新走廊宽度", newWidth, v => newWidth = Mathf.Max(.3f, v));
            Float(area, "新竖井边长", newPitSize, v => newPitSize = Mathf.Max(.3f, v));
            if (tool == Tool.Connect) Toggle(area, "连接终点（关闭 = 起点）", endConnection, v => endConnection = v);
            Toggle(area, "导出时加入重力玩家", includePlayer, v => includePlayer = v);
            if (layout == null) return;
            DrawEnemySettings(area);
            var fold = new Foldout { text = "生成设置", value = false }; area.Add(fold);
            Toggle(fold, "露天（不启用顶面）", layout.openSky, v => Change(() => layout.openSky = v));
            Toggle(fold, "保留房间隔墙", layout.architecturalPartitions, v => Change(() => layout.architecturalPartitions = v));
            Float(fold, "墙高", layout.wallHeight, v => Change(() => layout.wallHeight = Mathf.Max(.1f, v)));
            Float(fold, "墙厚", layout.wallThickness, v => Change(() => layout.wallThickness = Mathf.Max(.05f, v)));
            Float(fold, "地板厚度", layout.floorThickness, v => Change(() => layout.floorThickness = Mathf.Max(.05f, v)));
            var samples = new SliderInt("每段曲线采样", 4, 64) { value = layout.curveSamples };
            samples.RegisterValueChangedCallback(e => Change(() => layout.curveSamples = e.newValue)); fold.Add(samples);
            var segments = new SliderInt("圆形分段", 16, 128) { value = layout.circleSegments };
            segments.RegisterValueChangedCallback(e => Change(() => layout.circleSegments = e.newValue)); fold.Add(segments);
            MaterialField(fold, "地板材质", layout.floorMaterial, v => layout.floorMaterial = v);
            MaterialField(fold, "墙壁材质", layout.wallMaterial, v => layout.wallMaterial = v);
            MaterialField(fold, "管壁材质", layout.pitMaterial, v => layout.pitMaterial = v);
            fold.SetEnabled(!SceneInstance);
            DrawPropSettings(area);
        }

        void DrawSeededMode()
        {
            rootVisualElement.Q<Foldout>("settings-foldout").value = true;
            rootVisualElement.Q("tools").Clear(); rootVisualElement.Q("objects").Clear();
            rootVisualElement.Q("properties").Clear(); rootVisualElement.Q("footer").Clear();
            rootVisualElement.Q<Label>("instructions").text = "独立程序化模式：楼层编号 = 种子。相同楼层可复现，越深房间、分支与可搬运物品越多。手工布局不受影响。";
            var area = rootVisualElement.Q("settings"); area.Clear();
            var target = new ObjectField("程序化地图") { objectType = typeof(LevelMap), allowSceneObjects = true, value = seededMap };
            target.RegisterValueChangedCallback(e => { seededMap = (LevelMap)e.newValue; Refresh(); }); area.Add(target);
            var seed = new IntegerField("预览层数 / 种子") { value = previewFloor, isDelayed = true };
            seed.RegisterValueChangedCallback(e => { previewFloor = Mathf.Max(0, e.newValue); Refresh(); }); area.Add(seed);
            var plan = SeededFloorPlan.Generate(previewFloor, seededMap != null ? seededMap.growthLimit : 24);
            area.Add(new Label($"F{previewFloor}：{plan.Rooms.Count} 房间 · {plan.Connections.Count} 连接 · {plan.Cells.Count} m² · {plan.Resources.Count} 物品"));
            area.Add(new Label("输入仅预览指定层。实际游戏从 F0 开始，穿过竖井后进入 F1、F2……"));
            if (seededMap != null)
            {
                Float(area, "层间距", seededMap.floorSpacing, v => { Undo.RecordObject(seededMap, "修改程序化配置"); seededMap.floorSpacing = Mathf.Max(8, v); EditorUtility.SetDirty(seededMap); });
                var growth = new SliderInt("规模增长上限层", 0, 100) { value = seededMap.growthLimit };
                growth.RegisterValueChangedCallback(e => { Undo.RecordObject(seededMap, "修改规模上限"); seededMap.growthLimit = e.newValue; EditorUtility.SetDirty(seededMap); }); area.Add(growth);
                var profile = new ObjectField("数值 / 字幕配置") { objectType = typeof(FloorProgressionProfile), value = seededMap.profile };
                profile.RegisterValueChangedCallback(e => { Undo.RecordObject(seededMap, "修改楼层配置"); seededMap.profile = (FloorProgressionProfile)e.newValue; EditorUtility.SetDirty(seededMap); }); area.Add(profile);
            }
            Button(area, "预览此层", () =>
            {
                var floor = SeededMapBuilder.Preview(seededMap, previewFloor);
                var bounds = floor.GetComponent<MeshRenderer>().bounds;
                var view = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
                view.LookAt(bounds.center, Quaternion.Euler(90, 0, 0), bounds.extents.magnitude, true, true);
            }).SetEnabled(seededMap != null && !Application.isPlaying);
            Button(area, "创建独立程序化场景", () =>
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                string path = EditorUtility.SaveFilePanelInProject("新建种子地图", "SeededMap", "unity", "创建独立场景，不改动已有地图。", "Assets/Scenes");
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    SeededMapBuilder.CreateScene(path);
                    EditorSceneManager.OpenScene(path);
                    seededMap = UnityEngine.Object.FindAnyObjectByType<LevelMap>();
                    Selection.activeGameObject = seededMap.gameObject; Refresh();
                }
                catch (Exception e) { status = e.Message; hasError = true; UpdateStatus(); }
            }).SetEnabled(!Application.isPlaying);
            UpdateStatus();
        }

        void DrawObjects()
        {
            var area = rootVisualElement.Q("objects"); area.Clear();
            if (layout == null) return;
            var geometry = new Foldout { name = "geometry-objects", text = $"房间 / 走廊 / 竖井 ({layout.rooms.Count + layout.corridors.Count + layout.pits.Count})", value = geometryExpanded };
            geometry.RegisterValueChangedCallback(e => { if (e.target == geometry) geometryExpanded = e.newValue; });
            area.Add(geometry);
            void Item(Kind k, int i, string text)
            {
                var b = Button(geometry, text, () => Select(k, i)); b.AddToClassList("item");
                b.EnableInClassList("selected-item", kind == k && selected == i);
            }
            for (int i = 0; i < layout.rooms.Count; i++) Item(Kind.Room, i, "▣  " + layout.rooms[i].name);
            for (int i = 0; i < layout.corridors.Count; i++) Item(Kind.Corridor, i, "↝  " + layout.corridors[i].name + "  /  " + layout.corridors[i].width.ToString("0.##") + " m");
            for (int i = 0; i < layout.pits.Count; i++) Item(Kind.Pit, i, "□  " + layout.pits[i].name);
            geometry.SetEnabled(!SceneInstance);
            if (!SceneInstance) DrawPartitionObjects(area);
            DrawEnemyObjects(area);
            var propRoot = LevelPropPlacement.PropsRoot(layout);
            var props = new Foldout { name = "prop-objects", text = $"道具 ({(propRoot != null ? propRoot.childCount : 0)})", value = propsExpanded };
            props.RegisterValueChangedCallback(e => { if (e.target == props) propsExpanded = e.newValue; });
            area.Add(props);
            DrawPropObjects(props);
        }

        void DrawProperties()
        {
            var area = rootVisualElement.Q("properties"); area.Clear(); area.SetEnabled(!Application.isPlaying);
            if (DrawEnemyProperties(area) || DrawPropProperties(area)) return;
            if (SceneInstance) { area.Add(new Label("选择道具或敌人以编辑；房间结构请打开源关卡 Prefab。")); return; }
            if (DrawFreePartitionProperties(area)) return;
            if (layout == null || selected < 0) { area.Add(new Label("选择对象以编辑参数")); return; }
            if (kind == Kind.Room && selected < layout.rooms.Count)
            {
                var r = layout.rooms[selected];
                Text(area, "名称", r.name, v => r.name = v);
                var use = new EnumField("房间用途", r.use); use.RegisterValueChangedCallback(e => Change(() => r.use = (RoomUse)e.newValue)); area.Add(use);
                Toggle(area, "开放分区（无隔墙）", r.openPlan, v => Change(() => r.openPlan = v));
                Vector(area, "中心 X / Z", r.center, v => r.center = v);
                if (r.shape == RoomShape.Circle) Float(area, "直径", r.size.x, v => Change(() => r.size = Vector2.one * Mathf.Max(.5f, v)));
                else Vector(area, "宽 / 长", r.size, v => r.size = Vector2.Max(Vector2.one * .5f, v));
                Float(area, "旋转角度", r.rotation, v => Change(() => r.rotation = v));
                var partitions = new Foldout { text = "房间隔板", value = true }; area.Add(partitions);
                for (int i = 0; i < r.partitions.Count; i++)
                {
                    int index = i; var p = r.partitions[i];
                    var fold = new Foldout { text = "隔板 " + (i + 1), value = true }; partitions.Add(fold);
                    PartitionEndpoint(fold, "起点（相对房间）", r, p.start, v => p.start = v);
                    PartitionEndpoint(fold, "终点（相对房间）", r, p.end, v => p.end = v);
                    Float(fold, "高度", p.height, v => Change(() => p.height = Mathf.Max(.1f, v)));
                    Float(fold, "厚度", p.thickness, v => Change(() => p.thickness = Mathf.Max(.05f, v)));
                    Button(fold, "转为独立隔板", () => DetachPartition(r, p));
                    Button(fold, "删除隔板及其门", () => { Change(() => DeletePartition(p, r)); Refresh(); });
                }
                Button(partitions, "添加隔板", () => { Change(() => r.partitions.Add(new RoomPartition { start = SnapPartition(r, Vector2.left * r.size.x * .3f), end = SnapPartition(r, Vector2.right * r.size.x * .3f), height = layout.wallHeight })); Refresh(); });
            }
            else if (kind == Kind.Corridor && selected < layout.corridors.Count)
            {
                var c = layout.corridors[selected];
                Text(area, "名称", c.name, v => c.name = v);
                Toggle(area, "共墙门洞", c.doorOnly, v => Change(() => c.doorOnly = v));
                Toggle(area, "平滑曲线", c.curved, v => Change(() => c.curved = v));
                Float(area, "净通行宽度", c.width, v => Change(() => c.width = Mathf.Max(.3f, v)));
                for (int i = 0; i < c.points.Count; i++)
                {
                    int index = i;
                    bool attached = i == 0 && layout.FindRoom(c.start.roomId) != null || i == c.points.Count - 1 && layout.FindRoom(c.end.roomId) != null;
                    var row = Row(area);
                    Vector(row, "控制点 " + (i + 1), i == 0 ? LevelGeometry.Endpoint(layout, c, false) : i == c.points.Count - 1 ? LevelGeometry.Endpoint(layout, c, true) : c.points[i], v => c.points[index] = v).SetEnabled(!attached);
                    if (i > 0 && i < c.points.Count - 1) Button(row, "删除", () => { Change(() => c.points.RemoveAt(index)); Refresh(); });
                    if (i < c.points.Count - 1) Button(row, "+", () => { Change(() => c.points.Insert(index + 1, (Point(c, index) + Point(c, index + 1)) * .5f)); Refresh(); });
                }
                ConnectionUI(area, c, false); ConnectionUI(area, c, true);
            }
            else if (kind == Kind.Pit && selected < layout.pits.Count)
            {
                var p = layout.pits[selected];
                Text(area, "名称", p.name, v => p.name = v);
                Vector(area, "中心 X / Z", p.center, v => p.center = v);
                Float(area, "方形边长", p.size, v => Change(() => p.size = Mathf.Max(.3f, v)));
                Float(area, "管道深度", p.depth, v => Change(() => p.depth = Mathf.Max(layout.floorThickness + .1f, v)));
                Float(area, "旋转角度", p.rotation, v => Change(() => p.rotation = v));
            }
            else { selected = -1; return; }
            Button(area, "删除选中对象", Delete);
        }

        Vector2 Point(LevelCorridor c, int i) => i == 0 ? LevelGeometry.Endpoint(layout, c, false) : i == c.points.Count - 1 ? LevelGeometry.Endpoint(layout, c, true) : c.points[i];

        void ConnectionUI(VisualElement area, LevelCorridor c, bool end)
        {
            var connection = end ? c.end : c.start;
            var r = layout.FindRoom(connection.roomId);
            area.Add(new Label((end ? "终点 → " : "起点 → ") + (r == null ? "未连接" : r.name)));
            if (r != null)
            {
                Float(area, "沿边位置（0–1）", connection.edge, v => Change(() => connection.edge = Mathf.Repeat(v, 1)));
                Button(area, "解除" + (end ? "终点" : "起点") + "连接", () =>
                {
                    Change(() => { c.points[end ? c.points.Count - 1 : 0] = LevelGeometry.Endpoint(layout, c, end); connection.roomId = ""; }); Refresh();
                });
            }
        }

        void Select(Kind k, int index) { if (tool == Tool.RepositionProp || tool == Tool.Partition) tool = Tool.Select; selectedEnemy = null; selectedProp = null; kind = k; selected = index; Refresh(); SceneView.RepaintAll(); }
        void Change(Action action)
        {
            if (layout == null || Application.isPlaying || LevelBuilder.IsScenePrefabInstance(layout)) return;
            Undo.RecordObject(layout, "编辑关卡布局"); action(); EditorUtility.SetDirty(layout); TryRebuild(); DrawObjects();
        }
        bool TryRebuild()
        {
            try { LevelBuilder.Rebuild(layout); status = $"{layout.rooms.Count} 房间 · {layout.corridors.Count} 走廊 · {layout.pits.Count} 竖井。预览已更新。"; hasError = false; UpdateStatus(); return true; }
            catch (Exception e) { status = e.Message + " 当前显示上次有效预览，请修正参数后再保存。"; hasError = true; UpdateStatus(); return false; }
        }
        void UpdateStatus()
        {
            var label = rootVisualElement.Q<Label>("status"); if (label == null) return;
            label.text = status; label.EnableInClassList("error", hasError);
        }

        void CreateLayout()
        {
            if (Application.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Editable Level", typeof(LevelLayout));
            LevelBuilder.AddLighting();
            Undo.RegisterCreatedObjectUndo(go, "创建关卡布局"); Bind(go.GetComponent<LevelLayout>());
            Selection.activeGameObject = go; tool = Tool.Rectangle; Refresh();
        }

        void Save()
        {
            if (SceneInstance)
            {
                if (EditorSceneManager.SaveScene(layout.gameObject.scene)) { status = "已保存总场景中的关卡修改。"; hasError = false; UpdateStatus(); }
                return;
            }
            if (layout == null || !TryRebuild()) return;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene == layout.gameObject.scene)
            {
                LevelBuilder.Persist(layout, stage.assetPath);
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                status = "已保存独立关卡 Prefab。"; UpdateStatus(); return;
            }
            if (EditorSceneManager.SaveScene(layout.gameObject.scene)) { status = "已保存场景及可编辑布局。"; UpdateStatus(); }
        }
        void Export()
        {
            if (layout == null || !TryRebuild()) return;
            string path = EditorUtility.SaveFilePanelInProject("导出可编辑关卡", "NewLevel", "unity", "保存参数化布局、网格、碰撞与可选玩家。", "Assets/Scenes");
            if (string.IsNullOrEmpty(path)) return;
            if (path == layout.gameObject.scene.path) { Save(); return; }
            if (UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path).isLoaded)
            { status = "目标场景正被打开，请另选文件名，或使用保存当前场景。"; hasError = true; UpdateStatus(); return; }
            try { LevelBuilder.Export(layout, path, includePlayer); status = "已导出 " + path + "。双击打开后可继续编辑。"; hasError = false; }
            catch (Exception e) { status = e.Message; hasError = true; }
            UpdateStatus();
        }
        void Delete()
        {
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("删除布局对象及附属门");
            Change(() =>
            {
                if (kind == Kind.Room && selected < layout.rooms.Count)
                {
                    string id = layout.rooms[selected].id;
                    foreach (var partition in layout.rooms[selected].partitions.ToArray()) DeletePartition(partition, layout.rooms[selected]);
                    foreach (var c in layout.corridors)
                    {
                        if (c.start.roomId == id) { c.points[0] = LevelGeometry.Endpoint(layout, c, false); c.start.roomId = ""; }
                        if (c.end.roomId == id) { c.points[c.points.Count - 1] = LevelGeometry.Endpoint(layout, c, true); c.end.roomId = ""; }
                    }
                    layout.rooms.RemoveAt(selected);
                }
                else if (kind == Kind.Corridor && selected < layout.corridors.Count) layout.corridors.RemoveAt(selected);
                else if (kind == Kind.Pit && selected < layout.pits.Count) layout.pits.RemoveAt(selected);
                else if (kind == Kind.Partition && selected < layout.partitions.Count) DeletePartition(layout.partitions[selected]);
            });
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            selected = -1; kind = Kind.None; Refresh();
        }

        void ExportLevelPrefab()
        {
            if (layout == null || !TryRebuild()) return;
            string path = EditorUtility.SaveFilePanelInProject("导出独立关卡", layout.name, "prefab", "只包含本关模型、道具和电梯入口；连接关系由大场景保存。");
            if (string.IsNullOrEmpty(path)) return;
            LevelBuilder.ExportPrefab(layout, path);
            Bind(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<LevelLayout>());
            status = "已保存关卡 Prefab 并保留实例关联：" + path; UpdateStatus();
        }

        void Frame()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) view = GetWindow<SceneView>();
            Bounds bounds = new Bounds(layout == null ? Vector3.zero : layout.transform.position, Vector3.one * 10);
            if (layout != null)
            {
                if (layout.manualGeometry)
                    foreach (var renderer in layout.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(renderer.bounds);
                foreach (var r in layout.rooms) foreach (var p in LevelGeometry.RoomOutline(r, 32)) bounds.Encapsulate(layout.World(p));
                foreach (var c in layout.corridors) foreach (var p in c.points) bounds.Encapsulate(layout.World(p));
                foreach (var p in LevelPartitions.All(layout)) { bounds.Encapsulate(layout.World(p.Start)); bounds.Encapsulate(layout.World(p.End, p.data.height)); }
            }
            view.LookAt(bounds.center, (layout == null ? Quaternion.identity : layout.transform.rotation) * Quaternion.Euler(90, 0, 0), Mathf.Max(8, bounds.extents.magnitude), true, true);
            view.Repaint();
        }

        Vector2 Snap(Vector2 point) => snap <= 0 ? point : new Vector2(Mathf.Round(point.x / snap) * snap, Mathf.Round(point.y / snap) * snap);
        Vector2 SnapPartition(LevelRoom room, Vector2 point) => snap <= 0 ? point : room.PartitionLocal(Snap(room.PartitionPoint(point)));
        void SceneGUI(SceneView view)
        {
            if (seededMode || layout == null || Application.isPlaying || !layout.gameObject.activeInHierarchy) return;
            DrawPropColorPreview(view);
            Event e = Event.current;
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var plane = new Plane(layout.transform.up, layout.transform.position);
            if (plane.Raycast(ray, out float distance)) hover = Snap(layout.Local(ray.GetPoint(distance)));
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            DrawOutlines();
            if (EnemySceneGUI(view, ray, e)) { Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual; return; }
            if (RingTableSceneGUI(view, ray, e)) { Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual; return; }
            if (!SceneInstance && PartitionSceneGUI(view, e)) { Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual; return; }
            if (tool == Tool.Prop || tool == Tool.RepositionProp)
            {
                PropSceneGUI(view, ray, e);
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                return;
            }
            if (SceneInstance) { Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual; return; }
            if (tool == Tool.Select) EditHandles();
            else
            {
                if (!e.alt) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (tool == Tool.Connect) ConnectionPreview();
                else if (tool == Tool.Rectangle || tool == Tool.Circle)
                    Outline(LevelGeometry.RoomOutline(new LevelRoom { center = hover, shape = tool == Tool.Circle ? RoomShape.Circle : RoomShape.Rectangle }, 64), Color.white);
                else if (tool == Tool.Pit) Outline(LevelGeometry.PitOutline(new LevelPit { center = hover, size = newPitSize }), new Color(1, .6f, .2f));
                else if (pending.Count > 0)
                {
                    var draft = new LevelCorridor { points = new List<Vector2>(pending), curved = tool == Tool.Curve, width = newWidth };
                    draft.points.Add(hover);
                    Line(LevelGeometry.CorridorPath(layout, draft), Color.yellow);
                }
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && plane.Raycast(ray, out _)) { Click(); e.Use(); }
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Escape) { pending.Clear(); tool = Tool.Select; Refresh(); e.Use(); }
                    else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && (tool == Tool.Line || tool == Tool.Curve)) { FinishCorridor(); e.Use(); }
                    else if (e.keyCode == KeyCode.Backspace && pending.Count > 0) { pending.RemoveAt(pending.Count - 1); e.Use(); }
                }
            }
            if (e.type == EventType.MouseMove) view.Repaint();
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        }

        void DrawOutlines()
        {
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var r = layout.rooms[i];
                Outline(LevelGeometry.RoomOutline(r, layout.circleSegments), kind == Kind.Room && selected == i ? Color.yellow : new Color(.55f, .85f, 1));
                Marker(r.center, r.name, Kind.Room, i);
                foreach (var p in r.partitions) Line(new List<Vector2> { r.PartitionPoint(p.start), r.PartitionPoint(p.end) }, new Color(1, .7f, .3f));
            }
            for (int i = 0; i < layout.corridors.Count; i++)
            {
                var c = layout.corridors[i]; if (c.points.Count < 2) continue;
                Color color = kind == Kind.Corridor && selected == i ? Color.yellow : Color.cyan;
                var path = LevelGeometry.CorridorPath(layout, c);
                Line(path, color);
                foreach (var outline in LevelGeometry.Offset(new Clipper2Lib.Paths64 { LevelGeometry.Path(LevelGeometry.CorridorPath(layout, c, true)) }, c.width * .5f, Clipper2Lib.EndType.Butt))
                    Outline(outline.Select(LevelGeometry.Point).ToList(), color);
                Marker(path[path.Count / 2], c.name + " / " + c.width.ToString("0.##") + "m", Kind.Corridor, i);
            }
            for (int i = 0; i < layout.pits.Count; i++)
            {
                var p = layout.pits[i]; Outline(LevelGeometry.PitOutline(p), kind == Kind.Pit && selected == i ? Color.yellow : new Color(1, .5f, .1f));
                Marker(p.center, p.name + " ↓" + p.depth.ToString("0.#") + "m", Kind.Pit, i);
            }
        }
        void Line(List<Vector2> points, Color color)
        {
            if (points.Count < 2) return;
            Handles.color = color; Handles.DrawAAPolyLine(3, points.Select(p => layout.World(p, .04f)).ToArray());
        }
        void Outline(List<Vector2> points, Color color)
        {
            var closed = new List<Vector2>(points); if (closed.Count > 0) closed.Add(closed[0]); Line(closed, color);
        }
        void Marker(Vector2 p, string text, Kind k, int i)
        {
            var world = layout.World(p, .06f); float size = HandleUtility.GetHandleSize(world) * .07f;
            Handles.Label(world + layout.transform.right * size * 2, text);
            if (tool == Tool.Select && !(kind == k && selected == i) && Handles.Button(world, Quaternion.identity, size, size * 1.5f, Handles.DotHandleCap)) Select(k, i);
        }

        Vector2 Drag(Vector2 p, Color color)
        {
            Handles.color = color;
            var world = layout.World(p, .08f);
            var moved = Handles.Slider2D(world, layout.transform.up, layout.transform.right, layout.transform.forward, HandleUtility.GetHandleSize(world) * .09f, Handles.DotHandleCap, Vector2.zero);
            if ((moved - world).sqrMagnitude < 1e-10f) return p;
            return Snap(layout.Local(moved));
        }
        void EditHandles()
        {
            if (selected < 0) return;
            EditorGUI.BeginChangeCheck();
            Action apply = null;
            if (kind == Kind.Partition && selected < layout.partitions.Count)
            {
                var p = layout.partitions[selected]; var center = (p.start + p.end) / 2;
                var delta = Drag(center, Color.yellow) - center;
                var start = Drag(p.start, Color.green); var end = Drag(p.end, Color.red);
                apply = () => { p.start = start + delta; p.end = end + delta; };
            }
            else if (kind == Kind.Room && selected < layout.rooms.Count)
            {
                var r = layout.rooms[selected];
                var center = Drag(r.center, Color.yellow);
                var edgeX = Drag(r.center + r.Rotate(Vector2.right * r.size.x * .5f), Color.red);
                var size = r.size; size.x = Mathf.Max(.5f, 2 * Vector2.Dot(edgeX - r.center, r.Rotate(Vector2.right)));
                if (r.shape == RoomShape.Circle) size.y = size.x;
                else
                {
                    var edgeZ = Drag(r.center + r.Rotate(Vector2.up * r.size.y * .5f), Color.green);
                    size.y = Mathf.Max(.5f, 2 * Vector2.Dot(edgeZ - r.center, r.Rotate(Vector2.up)));
                }
                var starts = new List<Vector2>(); var ends = new List<Vector2>();
                foreach (var p in r.partitions)
                {
                    starts.Add(r.PartitionLocal(Drag(r.PartitionPoint(p.start), new Color(1,.7f,.3f))));
                    ends.Add(r.PartitionLocal(Drag(r.PartitionPoint(p.end), new Color(1,.7f,.3f))));
                }
                apply = () => { r.center = center; r.size = size; for(int i=0;i<r.partitions.Count;i++){r.partitions[i].start=starts[i];r.partitions[i].end=ends[i];} };
            }
            else if (kind == Kind.Corridor && selected < layout.corridors.Count)
            {
                var c = layout.corridors[selected]; var points = new List<Vector2>(c.points);
                for (int i = 0; i < points.Count; i++)
                {
                    bool attached = i == 0 && layout.FindRoom(c.start.roomId) != null || i == points.Count - 1 && layout.FindRoom(c.end.roomId) != null;
                    if (!attached) points[i] = Drag(points[i], Color.yellow);
                    Handles.Label(layout.World(Point(c, i)), "P" + (i + 1) + (attached ? " [已连接]" : ""));
                }
                apply = () => c.points = points;
            }
            else if (kind == Kind.Pit && selected < layout.pits.Count)
            {
                var p = layout.pits[selected]; var center = Drag(p.center, Color.yellow);
                apply = () => p.center = center;
            }
            if (EditorGUI.EndChangeCheck() && apply != null) { Change(apply); DrawProperties(); }
        }

        LevelRoom NearestRoom(out float edge, out Vector2 point)
        {
            LevelRoom best = null; edge = 0; point = hover; float distance = 24;
            foreach (var r in layout.rooms)
            {
                float t = r.NearestEdge(hover); var p = r.Edge(t, out _);
                float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(layout.World(p)), Event.current.mousePosition);
                if (d < distance) { distance = d; best = r; edge = t; point = p; }
            }
            return best;
        }
        void ConnectionPreview()
        {
            var room = NearestRoom(out _, out Vector2 p); if (room == null) return;
            Handles.color = Color.green; Handles.DrawWireDisc(layout.World(p, .1f), layout.transform.up, .3f);
            Handles.Label(layout.World(p, .1f), "连接到 " + room.name);
        }
        void Click()
        {
            if (tool == Tool.Partition) { PlacePartitionPoint(); return; }
            if (tool == Tool.Connect)
            {
                var r = NearestRoom(out float edge, out _);
                if (kind != Kind.Corridor || selected < 0 || selected >= layout.corridors.Count || r == null)
                { status = "请先从列表选择走廊，再点击房间边缘（24 像素以内）。"; UpdateStatus(); return; }
                var c = layout.corridors[selected]; var connection = endConnection ? c.end : c.start;
                Change(() => { connection.roomId = r.id; connection.edge = edge; }); Refresh(); return;
            }
            if (tool == Tool.Line || tool == Tool.Curve)
            {
                if (pending.Count == 0 || Vector2.Distance(pending[pending.Count - 1], hover) > .05f) pending.Add(hover);
                return;
            }
            if (tool == Tool.Pit)
            {
                var pit = new LevelPit { center = hover, size = newPitSize, name = "竖井 " + (layout.pits.Count + 1) };
                // Reject invalid placement without adding an unusable object.
                layout.pits.Add(pit);
                try { LevelGeometry.Validate(layout, LevelGeometry.Footprint(layout)); }
                catch (Exception e) { layout.pits.Remove(pit); status = e.Message; hasError = true; UpdateStatus(); return; }
                layout.pits.Remove(pit);
                Change(() => layout.pits.Add(pit)); Select(Kind.Pit, layout.pits.Count - 1); return;
            }
            var room = new LevelRoom { center = hover, shape = tool == Tool.Circle ? RoomShape.Circle : RoomShape.Rectangle, name = (tool == Tool.Circle ? "圆柱房间 " : "矩形房间 ") + (layout.rooms.Count + 1) };
            Change(() => layout.rooms.Add(room)); Select(Kind.Room, layout.rooms.Count - 1);
        }
        void FinishCorridor()
        {
            if (pending.Count < 2) return;
            var corridor = new LevelCorridor { curved = tool == Tool.Curve, width = newWidth, points = new List<Vector2>(pending), name = (tool == Tool.Curve ? "曲线走廊 " : "直线走廊 ") + (layout.corridors.Count + 1) };
            Change(() => layout.corridors.Add(corridor)); pending.Clear(); Select(Kind.Corridor, layout.corridors.Count - 1);
        }

        static VisualElement Row(VisualElement parent) { var row = new VisualElement(); row.AddToClassList("toolbar-row"); parent.Add(row); return row; }
        static Button Button(VisualElement parent, string text, Action action) { var b = new Button(action) { text = text }; parent.Add(b); return b; }
        static void Float(VisualElement parent, string name, float value, Action<float> change)
        { var f = new FloatField(name) { value = value, isDelayed = true }; f.RegisterValueChangedCallback(e => change(e.newValue)); parent.Add(f); }
        static void Toggle(VisualElement parent, string name, bool value, Action<bool> change)
        { var f = new Toggle(name) { value = value }; f.RegisterValueChangedCallback(e => change(e.newValue)); parent.Add(f); }
        void Text(VisualElement parent, string name, string value, Action<string> change)
        { var f = new TextField(name) { value = value, isDelayed = true }; f.RegisterValueChangedCallback(e => { Change(() => change(e.newValue)); DrawObjects(); }); parent.Add(f); }
        void PartitionEndpoint(VisualElement parent, string name, LevelRoom room, Vector2 value, Action<Vector2> change)
        {
            var field = new Vector2Field(name) { value = value, tooltip = "坐标相对房间；提交后按房间放置使用的关卡网格吸附。" };
            field.Query<FloatField>().ForEach(component => component.isDelayed = true);
            field.RegisterValueChangedCallback(e =>
            {
                var snapped = SnapPartition(room, e.newValue);
                Change(() => change(snapped));
                field.SetValueWithoutNotify(snapped);
            });
            parent.Add(field);
        }
        Vector2Field Vector(VisualElement parent, string name, Vector2 value, Action<Vector2> change)
        { var f = new Vector2Field(name) { value = value }; f.Query<FloatField>().ForEach(n => n.isDelayed = true); f.RegisterValueChangedCallback(e => Change(() => change(e.newValue))); parent.Add(f); return f; }
        void MaterialField(VisualElement parent, string name, Material value, Action<Material> change)
        { var f = new ObjectField(name) { objectType = typeof(Material), value = value }; f.RegisterValueChangedCallback(e => Change(() => change((Material)e.newValue))); parent.Add(f); }
    }

    [CustomEditor(typeof(LevelLayout))]
    public sealed class LevelLayoutInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox("此对象保留完整关卡布局。使用关卡编辑器修改后重新生成；Generated Geometry 下的网格可使用 Unity 工具编辑，但下次生成会覆盖这些网格。", HelpBoxMessageType.Info));
            root.Add(new Button(() => { var window = EditorWindow.GetWindow<LevelDesignerWindow>("关卡编辑器"); window.Bind((LevelLayout)target); }) { text = "打开关卡编辑器" });
            return root;
        }
    }
}
