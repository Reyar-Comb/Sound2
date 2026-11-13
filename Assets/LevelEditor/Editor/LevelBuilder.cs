using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Sound2.LevelEditing.Editor
{
    public static class LevelBuilder
    {
        public static void Rebuild(LevelLayout layout)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请退出运行模式后编辑关卡。");
            var meshes = LevelGeometry.Build(layout); // Validate first; preserve the last valid preview on failure.
            EnsureMaterials(layout);
            var previousRoot = layout.transform.Find("Generated Geometry");
            var root = new GameObject("Generated Geometry");
            root.transform.SetParent(layout.transform, false);
            foreach (var pair in meshes)
            {
                var part = new GameObject(pair.Key, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                part.transform.SetParent(root.transform, false);
                part.GetComponent<MeshFilter>().sharedMesh = pair.Value;
                part.GetComponent<MeshCollider>().sharedMesh = pair.Value;
                part.GetComponent<MeshRenderer>().sharedMaterial = pair.Key == "Floor" ? layout.floorMaterial : pair.Key == "Walls" ? layout.wallMaterial : layout.pitMaterial;
            }
            if (previousRoot != null)
            {
                foreach (var filter in previousRoot.GetComponentsInChildren<MeshFilter>())
                    if (filter.sharedMesh != null && !AssetDatabase.Contains(filter.sharedMesh)) Object.DestroyImmediate(filter.sharedMesh);
                Object.DestroyImmediate(previousRoot.gameObject);
            }
            layout.generatedRoot = root;
            EditorUtility.SetDirty(layout);
            EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            Physics.SyncTransforms();
            SceneView.RepaintAll();
        }

        static void EnsureMaterials(LevelLayout layout)
        {
            Directory.CreateDirectory("Assets/LevelEditor/Materials");
            if (layout.floorMaterial == null) layout.floorMaterial = Material("Floor", new Color(.18f, .29f, .34f));
            if (layout.wallMaterial == null) layout.wallMaterial = Material("Walls", new Color(.56f, .68f, .72f));
            if (layout.pitMaterial == null) layout.pitMaterial = Material("Shaft", new Color(.75f, .36f, .12f));
        }

        static Material Material(string name, Color color)
        {
            string path = "Assets/LevelEditor/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static void Persist(LevelLayout layout, string scenePath)
        {
            if (layout.generatedRoot == null) Rebuild(layout);
            var filters = layout.generatedRoot.GetComponentsInChildren<MeshFilter>();
            string folder = Path.ChangeExtension(scenePath, null) + "_Geometry";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            if (string.IsNullOrEmpty(layout.geometryAsset) || !layout.geometryAsset.StartsWith(folder + "/", StringComparison.Ordinal))
            {
                layout.geometryAsset = AssetDatabase.GenerateUniqueAssetPath(folder + "/Meshes.asset");
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<LevelMeshStore>(), layout.geometryAsset);
            }
            foreach (var filter in filters)
            {
                var source = filter.sharedMesh;
                if (source == null || AssetDatabase.GetAssetPath(source) == layout.geometryAsset) continue;
                // Reuse sub-asset identity so existing scene references survive subsequent saves.
                Mesh stored = null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(layout.geometryAsset))
                    if (asset is Mesh candidate && candidate.name == source.name) { stored = candidate; break; }
                if (stored == null)
                {
                    stored = Object.Instantiate(source); stored.name = source.name;
                    AssetDatabase.AddObjectToAsset(stored, layout.geometryAsset);
                }
                else EditorUtility.CopySerialized(source, stored);
                EditorUtility.SetDirty(stored);
                filter.sharedMesh = stored;
                filter.GetComponent<MeshCollider>().sharedMesh = null;
                filter.GetComponent<MeshCollider>().sharedMesh = stored;
                if (!AssetDatabase.Contains(source)) Object.DestroyImmediate(source);
            }
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(layout.geometryAsset));
        }

        public static void Export(LevelLayout source, string path, bool includePlayer)
        {
            Rebuild(source);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var copy = Object.Instantiate(source.gameObject);
                copy.name = source.name;
                var layout = copy.GetComponent<LevelLayout>();
                layout.geometryAsset = "";
                // Rebuild to separate transient meshes from the source scene.
                layout.generatedRoot = null;
                var copiedGeometry = copy.transform.Find("Generated Geometry");
                if (copiedGeometry != null) Object.DestroyImmediate(copiedGeometry.gameObject);
                Rebuild(layout);
                AddLighting();
                if (includePlayer) AddPlayer(layout);
                Persist(layout, path);
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("场景保存失败：" + path);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            AssetDatabase.SaveAssets();
        }

        public static void AddLighting()
        {
            var light = new GameObject("Level Directional Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(55, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.45f, .48f, .55f);
        }

        public static GameObject AddPlayer(LevelLayout layout)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (prefab == null) throw new InvalidOperationException("找不到 Assets/Prefabs/Player.prefab。");
            Vector2 spawn = layout.rooms.Count > 0 ? layout.rooms[0].center : layout.corridors.Count > 0 ? LevelGeometry.Endpoint(layout, layout.corridors[0], false) : Vector2.zero;
            // Find a supported position, including when a pit covers the first room's center.
            Physics.SyncTransforms();
            bool found = false;
            var floor = layout.generatedRoot.transform.Find("Floor").GetComponent<MeshCollider>();
            for (int i = 0; i < 1600; i++)
            {
                Vector2 candidate = spawn + new Vector2(i % 40 - 20, i / 40 - 20) * .5f;
                if (i == 0) candidate = spawn;
                bool safe = true;
                foreach (var offset in new[] { Vector2.zero, Vector2.right * .65f, Vector2.left * .65f, Vector2.up * .65f, Vector2.down * .65f,
                    new Vector2(.46f, .46f), new Vector2(-.46f, .46f), new Vector2(.46f, -.46f), new Vector2(-.46f, -.46f) })
                    if (!floor.Raycast(new Ray(layout.World(candidate + offset, 2), -layout.transform.up), out _, 3)) { safe = false; break; }
                if (safe) { spawn = candidate; found = true; break; }
            }
            if (!found) throw new InvalidOperationException("找不到安全的玩家出生地面；请增大房间或调整坑洞。");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, layout.gameObject.scene);
            var capsule = player.GetComponentInChildren<CharacterController>();
            float feetOffset = capsule != null ? capsule.height * .5f - capsule.center.y : 1;
            player.transform.position = layout.World(spawn, feetOffset + .1f);
            player.transform.rotation = layout.transform.rotation;
            var controller = player.GetComponentInChildren<PlayerController>();
            if (controller != null)
            {
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("useGravity").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return player;
        }
    }

    public sealed class LevelSaveProcessor : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            foreach (var path in paths)
            {
                if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var layout in root.GetComponentsInChildren<LevelLayout>(true)) LevelBuilder.Persist(layout, path);
            }
            return paths;
        }
    }
}
