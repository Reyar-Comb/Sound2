using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sound2.LevelEditing.Editor
{
    public static class SeededMapBuilder
    {
        public static string CreateScene(string path)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请退出运行模式。");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) throw new InvalidOperationException("目标已存在，请使用新场景文件名。");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var map = new GameObject("Seeded Map", typeof(LevelMap)).GetComponent<LevelMap>();
                map.profile = AssetDatabase.LoadAssetAtPath<FloorProgressionProfile>("Assets/Resources/FloorProgression.asset");
                map.geometryMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
                map.resourceMaterial = map.geometryMaterial;
                var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"), scene);
                player.transform.position = new Vector3(0, 1.1f, 0);
                var controller = new SerializedObject(player.GetComponent<PlayerController>());
                controller.FindProperty("useGravity").boolValue = true; controller.ApplyModifiedPropertiesWithoutUndo();
                LevelBuilder.ConfigurePointCloudPlayer(player);
                PrefabUtility.RecordPrefabInstancePropertyModifications(player.GetComponent<PlayerController>());
                if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("无法保存场景。");
                return path;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        public static SeededFloor Preview(LevelMap map, int floor)
        {
            var result = map.Preview(floor);
            var previewMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Walls.mat");
            foreach (var renderer in result.GetComponentsInChildren<MeshRenderer>()) renderer.sharedMaterial = previewMaterial;
            Physics.SyncTransforms();
            return result;
        }
    }
}
