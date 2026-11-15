using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public sealed class CreatorRoomTools : AssetPostprocessor
{
    public const string ScenePath = "Assets/Scenes/CreatorRoom.unity";
    public const string CatalogPath = "Assets/Scenes/CreatorRoomCatalog.asset";

    private static readonly string[] TestOrder = {
        "PickupBouncyBall", "PickupCoffeeMug", "PickupGlassMug", "PickupCeramicVase", "PickupAlarmClock", "Radio",
        "TickingClock", "PickupWaterCup", "PickupDeskLamp", "ShoppingCart", "TrashBin", "ServiceBell"
    };

    static CreatorRoomTools()
    {
        ScheduleSync();
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingEditMode)
                SyncExistingCatalog();
        };
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Any(IsModelPath) || deleted.Any(IsModelPath) || moved.Any(IsModelPath) || movedFrom.Any(IsModelPath))
            SyncExistingCatalog();
    }

    private static bool IsModelPath(string path) => path == "Assets/Prefabs" || path == "Assets/Models"
        || path.StartsWith("Assets/Prefabs/", StringComparison.Ordinal) || path.StartsWith("Assets/Models/", StringComparison.Ordinal);

    private static void ScheduleSync()
    {
        EditorApplication.delayCall -= SyncExistingCatalog;
        EditorApplication.delayCall += SyncExistingCatalog;
    }

    private static void SyncExistingCatalog()
    {
        if (EditorApplication.isPlaying) return;
        var catalog = AssetDatabase.LoadAssetAtPath<CreatorRoomCatalog>(CatalogPath);
        if (catalog != null) FillCatalog(catalog);
    }

    [MenuItem("Tools/Creator Room/Refresh Catalog")]
    public static void RefreshCatalog() => SyncExistingCatalog();

    private static IEnumerable<string> FindPaths(string filter, string folder) =>
        AssetDatabase.FindAssets(filter, new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Distinct();

    private static void FillCatalog(CreatorRoomCatalog catalog)
    {
        var entries = new List<CreatorRoomCatalog.Entry>();
        var authored = PropCatalog.All().OrderBy(prefab => {
            int priority = Array.IndexOf(TestOrder, prefab.name);
            return priority >= 0 ? priority : int.MaxValue;
        }).ThenBy(prefab => prefab.name, StringComparer.Ordinal);
        foreach (var prop in authored)
            entries.Add(new CreatorRoomCatalog.Entry { prefab = prop, label = PropCatalog.Name(prop) });

        // Native meshes and imported models already represented by a prefab are parts, not additional stations.
        var prefabPaths = FindPaths("t:Prefab", "Assets/Prefabs").Concat(FindPaths("t:Prefab", "Assets/Models"))
            .Where(path => path.EndsWith(".prefab", StringComparison.Ordinal)).Distinct().ToArray();
        var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(prefabPaths));
        var represented = new HashSet<GameObject>(entries.Select(entry => entry.prefab));
        foreach (string path in prefabPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null || represented.Contains(model) || model.name.EndsWith("Broken", StringComparison.Ordinal)
                || path == "Assets/Prefabs/PickupItem.prefab" || model.GetComponent<PlayerController>() != null
                || model.GetComponent<BallLightning>() != null || model.GetComponentInChildren<Renderer>(true) == null) continue;
            entries.Add(new CreatorRoomCatalog.Entry { prefab = model, label = PropCatalog.Name(model), modelOnly = model.GetComponent<LevelProp>() == null });
        }

        var rawPaths = FindPaths("t:Model", "Assets/Models").Concat(FindPaths("t:Mesh", "Assets/Models"));
        foreach (string path in rawPaths.Distinct().OrderBy(path => path, StringComparer.Ordinal))
        {
            if (dependencies.Contains(path)) continue;
            if (AssetImporter.GetAtPath(path) is ModelImporter)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model != null && model.GetComponentInChildren<Renderer>(true) != null)
                    entries.Add(new CreatorRoomCatalog.Entry { prefab = model, label = model.name, modelOnly = true });
            }
            else if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(Mesh))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh != null && mesh.vertexCount > 0)
                    entries.Add(new CreatorRoomCatalog.Entry { mesh = mesh, label = mesh.name, modelOnly = true });
            }
        }

        if (catalog.entries.SequenceEqual(entries)) return;
        catalog.entries = entries.ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
    }

    [MenuItem("Tools/Creator Room/Open Scene")]
    public static void OpenRoom()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
    }

    public static int CreateRoom()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath))
            throw new InvalidOperationException("The Creator Room can only be created once, in Edit mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scenes first.");

        var catalog = AssetDatabase.LoadAssetAtPath<CreatorRoomCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<CreatorRoomCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        FillCatalog(catalog);
        int count = catalog.entries.Length;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"), scene);
        player.transform.position = new Vector3(0, 1.05f, -1.5f);
        var camera = player.GetComponentInChildren<Camera>();
        var room = camera.gameObject.AddComponent<CreatorRoom>();
        var settings = new SerializedObject(room);
        settings.FindProperty("catalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CreatorRoomCatalog>(CatalogPath);
        settings.FindProperty("floorMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Floor.mat");
        settings.FindProperty("wallMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Walls.mat");
        settings.FindProperty("standMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/TablePreview.mat");
        settings.FindProperty("pointMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        settings.FindProperty("radioPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Radio.prefab");
        settings.FindProperty("radioAnalysis").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioReactiveClip>("Assets/Audio/Experiments/RadioLoop.asset");
        settings.ApplyModifiedPropertiesWithoutUndo();

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.6f, .63f, .67f);
        var light = new GameObject("Preview Lighting", typeof(Light)).GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.5f;
        light.transform.rotation = Quaternion.Euler(50, -35, 0);
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save the Creator Room.");
        return count;
    }
}
