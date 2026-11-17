using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ConfigureBossMemoryPreview
{
    public static object Run()
    {
        const string shaderPath = "Assets/Shaders/BossMemoryPreview.shader";
        const string materialPath = "Assets/Materials/BossMemoryPreview.mat";
        var prefabPaths = new[] { "Assets/Prefabs/BossMemoryTable.prefab", "Assets/Levels/Prefabs/LevelBoss.prefab" };
        AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (shader == null || !shader.isSupported) throw new Exception("Preview shader unsupported");
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetColor("_Color", Color.red);
        EditorUtility.SetDirty(material);
        foreach (var prefabPath in prefabPaths)
        {
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var gate in prefab.GetComponentsInChildren<BossMemoryGate>(true))
                {
                    var serialized = new SerializedObject(gate);
                    serialized.FindProperty("previewMaterial").objectReferenceValue = material;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        AssetDatabase.SaveAssets();
        return new { materialPath, prefabPaths, shaderMessages = ShaderUtil.GetShaderMessages(shader).Select(m => m.message).ToArray() };
    }
}
