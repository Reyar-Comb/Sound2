using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BouncePlaytest))]
public class BouncePlaytestEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("反弹音效 · 距离衰减曲线", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("横轴是撞击点与玩家的距离（米），纵轴是音量增益（0–1）。左边抬高会让近处反弹更响，右边压低会让远处反弹更轻。只影响反弹音效；第 3 个开关开启时生效。大厅嗡鸣与飞行正弦音不使用此曲线。", MessageType.Info);
        var curve = serializedObject.FindProperty("impactDistanceCurve");
        EditorGUILayout.PropertyField(curve, new GUIContent("Impact Distance Curve"), GUILayout.Height(80));
        var play = (BouncePlaytest)target;
        EditorGUILayout.LabelField("0.5 / 1 / 6 / 12m", $"{play.ImpactDistanceGain(0.5f):P0} / {play.ImpactDistanceGain(1):P0} / {play.ImpactDistanceGain(6):P0} / {play.ImpactDistanceGain(12):P0}");
        if (GUILayout.Button("恢复默认反弹衰减曲线")) curve.animationCurveValue = BouncePlaytest.CreateDefaultImpactCurve();
        EditorGUILayout.Space();
        DrawPropertiesExcluding(serializedObject, "m_Script", "impactDistanceCurve");
        serializedObject.ApplyModifiedProperties();
    }
}
