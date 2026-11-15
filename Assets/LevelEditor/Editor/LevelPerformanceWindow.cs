using UnityEditor;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public sealed class LevelPerformanceWindow : EditorWindow
    {
        LevelLayout layout;
        string report;
        Vector2 scroll;
        public static void Open(LevelLayout target)
        {
            var window = GetWindow<LevelPerformanceWindow>("关卡性能");
            window.layout = target;
            window.report = LevelPropOptimizer.Analyze(target);
        }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("分析显示道具的 Renderer / 碰撞体数量。优化会生成共享的静态道具 Prefab，并替换本关的安全实例。原始 Prefab 保留；交互、动态、参数化、子对象覆盖或被引用的实例会跳过。支持撤销，保存场景后持久化。", MessageType.Info);
            using (new EditorGUI.DisabledScope(layout == null || Application.isPlaying))
            {
                if (GUILayout.Button("重新分析")) report = LevelPropOptimizer.Analyze(layout);
                if (GUILayout.Button("优化本关静态道具"))
                {
                    try { report = LevelPropOptimizer.Optimize(layout) + "\n\n" + LevelPropOptimizer.Analyze(layout); }
                    catch (System.Exception error) { report = error.ToString(); Debug.LogException(error); }
                }
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report ?? "请从关卡编辑器打开。", GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }
}
