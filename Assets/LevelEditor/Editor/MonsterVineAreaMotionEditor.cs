using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sound2.LevelEditing.Editor
{
    [CustomEditor(typeof(MonsterVineAreaMotion))]
    public sealed class MonsterVineAreaMotionEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            root.Add(new HelpBox("起终点使用父物体局部坐标，可在 Scene 拖动端点。死亡立即回起点并清除旧藤蔓，复活后重新出发。关闭往返则到终点停止。", HelpBoxMessageType.Info));
            var motion = (MonsterVineAreaMotion)target;
            root.Add(new Button(() => SetEndpoint(motion, true, motion.transform.position)) { text = "当前位置设为起点" });
            root.Add(new Button(() => SetEndpoint(motion, false, motion.transform.position)) { text = "当前位置设为终点" });
            root.Add(new Button(() =>
            {
                Undo.RecordObject(motion.transform, "藤蔓区回到起点"); motion.ResetToStart();
                LevelEnemyPlacement.Dirty(motion.transform);
            }) { text = "回到起点" });
            return root;
        }
        public static void SetEndpoint(MonsterVineAreaMotion motion, bool start, Vector3 world)
        {
            Undo.RecordObject(motion, "设置藤蔓移动端点");
            var local = motion.transform.parent != null ? motion.transform.parent.InverseTransformPoint(world) : world;
            if (start) motion.startPosition = local; else motion.endPosition = local;
            LevelEnemyPlacement.Dirty(motion); SceneView.RepaintAll();
        }
        void OnSceneGUI()
        {
            var motion = (MonsterVineAreaMotion)target;
            Handles.color = Color.yellow; Handles.DrawLine(motion.StartWorld, motion.EndWorld);
            Handles.Label(motion.StartWorld, "起点"); Handles.Label(motion.EndWorld, "终点");
            if (Application.isPlaying) return;
            EditorGUI.BeginChangeCheck(); var start = Handles.PositionHandle(motion.StartWorld, Quaternion.identity);
            if (EditorGUI.EndChangeCheck()) SetEndpoint(motion, true, start);
            EditorGUI.BeginChangeCheck(); var end = Handles.PositionHandle(motion.EndWorld, Quaternion.identity);
            if (EditorGUI.EndChangeCheck()) SetEndpoint(motion, false, end);
        }
    }
}
