using System;
using System.Collections.Generic;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Action = CollectionItemSequence.Action;

public static class ConfigureCollectionItems
{
    const string Prefabs = "Assets/Prefabs/Collection";
    const string ShardMesh = "Assets/Models/HouseholdProps/WaterCupShard.asset";
    const string Audio = "Assets/Audio/collection/";

    static T Asset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Missing asset at " + path);

    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before authoring collectibles.");
        var duck = Asset<AudioClip>(Audio + "duck.wav");
        var crash = Asset<AudioClip>(Audio + "glass bottle crash2.wav");
        var alarm = Asset<AudioClip>(Audio + "alarm.wav");
        var wedding = Asset<AudioClip>(Audio + "weddding.ogg");
        var monitor = Asset<AudioClip>(Audio + "flat heart monitor.wav");
        if (!AssetDatabase.IsValidFolder(Prefabs)) AssetDatabase.CreateFolder("Assets/Prefabs", "Collection");

        var staging = EditorSceneManager.NewPreviewScene();
        try
        {
            var debris = BuildDebris(staging);
            Variant(staging, "TeddyBear", "CollectibleTeddyBear", root =>
                Configure(root, "school.squeaky-toy", Action.PickUpAndStore, new Vector3(0, .25f, 0),
                    "崭新的玩偶，按压会发出声响", oneShot: duck, range: 8));
            Variant(staging, "PickupWaterCup", "CollectibleWaterCup", root =>
            {
                var pickup = root.GetComponent<PickupItem>();
                pickup.breakable = true;
                pickup.breakOnlyWhenThrown = true;
                pickup.breakSpeed = 3.8f;
                pickup.brokenPrefab = debris;
                pickup.breakClip = crash;
                pickup.breakHearingRange = 10;
                Configure(root, "library.water-cup", Action.ShatterThenStore, null,
                    "完好的玻璃杯，不会发出声响", pickup);
            });
            Variant(staging, "PickupAlarmClock", "CollectibleAlarmClock", root =>
            {
                root.GetComponent<PickupAlarm>().enabled = false;
                root.GetComponent<TickingClock>().enabled = false;
                root.GetComponent<CollisionSonarEmitter>().enabled = false;
                root.GetComponent<SonarObjectEmitter>().enabled = false;
                root.GetComponent<PickupItem>().enabled = false;
                root.GetComponent<Rigidbody>().isKinematic = true;
                root.GetComponent<Rigidbody>().useGravity = false;
                Configure(root, "home.alarm-clock", Action.PickUpAndStore, null,
                    "吵闹的闹钟，不断发出着尖锐的声响", loop: alarm, range: 14);
            });
            Variant(staging, "Earmuffs", "CollectibleEarmuffs", root =>
                Configure(root, "supermarket.earmuffs", Action.PickUpAndStore, new Vector3(0, .145f, 0),
                    "戴在头上世界会立刻安静下来"));
            Variant(staging, "SchoolSpeaker", "CollectibleWeddingSpeaker", root =>
            {
                var prop = root.GetComponent<LevelProp>();
                prop.surface = PropSurface.Floor;
                prop.surfaceAnchor = new Vector3(0, -.22f, .135f);
                Configure(root, "wedding.speaker", Action.InteractAndStore, new Vector3(0, 0, .2f),
                    "音响的微缩模型，能感受到震动，旋律却很模糊", oneShot: wedding, range: 15);
            });
            Variant(staging, "HeartRateMonitor", "CollectibleHeartRateMonitor", root =>
                Configure(root, "hospital.heart-monitor", Action.InteractAndStore, null,
                    "缠绕着白胶布的导线，塑料接头处有一股很淡的消毒水味", loop: monitor, range: 11));
        }
        finally { EditorSceneManager.ClosePreviewScene(staging); }
        return new { assets = Prefabs, shard = ShardMesh };
    }

    static void Variant(Scene scene, string original, string name, System.Action<GameObject> configure)
    {
        string path = $"{Prefabs}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return;
        var parent = Asset<GameObject>($"Assets/Prefabs/{original}.prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(parent, scene);
        try
        {
            instance.name = name;
            configure(instance);
            if (PrefabUtility.SaveAsPrefabAsset(instance, path) == null)
                throw new InvalidOperationException("Could not create " + path);
        }
        finally { Object.DestroyImmediate(instance); }
    }

    static void Configure(GameObject root, string id, Action action, Vector3? anchorPosition, string line = null,
        PickupItem cup = null, AudioClip loop = null, AudioClip oneShot = null, float range = 9, bool keepGold = false)
    {
        var anchor = root.transform.Find("Interaction Anchor");
        if (anchorPosition.HasValue)
        {
            var point = new GameObject("Collection Anchor");
            point.transform.SetParent(root.transform, false);
            point.transform.localPosition = anchorPosition.Value;
            anchor = point.transform;
        }

        var collectible = root.AddComponent<StoryCollectible>();
        collectible.interactionAnchor = anchor;
        var fields = new SerializedObject(collectible);
        fields.FindProperty("collectibleId").stringValue = id;
        if (line != null)
        {
            var trigger = root.AddComponent<StorySubtitleTrigger>();
            var subtitle = new SerializedObject(trigger);
            var lines = subtitle.FindProperty("lines");
            lines.arraySize = 1;
            var cue = lines.GetArrayElementAtIndex(0);
            cue.FindPropertyRelative("text").stringValue = line;
            cue.FindPropertyRelative("duration").floatValue = 2.7f;
            subtitle.ApplyModifiedPropertiesWithoutUndo();
            fields.FindProperty("storySubtitles").objectReferenceValue = trigger;
        }
        fields.ApplyModifiedPropertiesWithoutUndo();

        var sequence = new SerializedObject(root.AddComponent<CollectionItemSequence>());
        sequence.FindProperty("action").enumValueIndex = (int)action;
        sequence.FindProperty("cup").objectReferenceValue = cup;
        sequence.FindProperty("idleLoop").objectReferenceValue = loop;
        sequence.FindProperty("interactionClip").objectReferenceValue = oneShot;
        sequence.FindProperty("keepGoldAfterDiscovery").boolValue = keepGold;
        sequence.FindProperty("spatial.maxDistance").floatValue = range;
        sequence.ApplyModifiedPropertiesWithoutUndo();
    }

    static GameObject BuildDebris(Scene scene)
    {
        string path = Prefabs + "/CollectibleWaterCupBroken.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var cup = Asset<GameObject>("Assets/Prefabs/PickupWaterCup.prefab");
        var shards = cup.GetComponentsInChildren<MeshCollider>();
        var glass = cup.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        var root = new GameObject("CollectibleWaterCupBroken");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<LevelProp>().gameplayMaterial = cup.GetComponent<LevelProp>().gameplayMaterial;
        try
        {
            foreach (var collider in shards)
            {
                bool collectible = collider.name == "Wall 0 0 Collider";
                var shard = new GameObject(collectible ? "Collectible glass shard" : collider.name.Replace(" Collider", ""));
                shard.transform.SetParent(root.transform, false);
                shard.transform.localPosition = collider.transform.localPosition;
                Mesh mesh = collectible ? BuildShardMesh() : collider.sharedMesh;
                shard.AddComponent<MeshFilter>().sharedMesh = mesh;
                shard.AddComponent<MeshRenderer>().sharedMaterial = glass;
                var shape = shard.AddComponent<MeshCollider>();
                shape.sharedMesh = mesh;
                shape.convex = true;
                var body = shard.AddComponent<Rigidbody>();
                body.mass = .025f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.maxDepenetrationVelocity = 2;
                if (collectible) Configure(shard, "library.water-cup", Action.PickUpAndStore, null, keepGold: true);
            }
            if (root.GetComponentsInChildren<StoryCollectible>().Length != 1) throw new InvalidOperationException("Water cup requires a single collectible shard.");
            PropFragments.SetLayer(root);
            return PrefabUtility.SaveAsPrefabAsset(root, path) ?? throw new InvalidOperationException("Could not create cup debris.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    static Mesh BuildShardMesh()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(ShardMesh);
        if (existing != null) return existing;
        // Irregular wedge cut from the curved side of a glass tumbler, with a closed rim thickness.
        var face = new[] { new Vector2(-.025f, -.022f), new Vector2(.006f, -.085f), new Vector2(.030f, -.016f),
            new Vector2(.021f, .058f), new Vector2(-.005f, .091f), new Vector2(-.024f, .033f) };
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        for (int side = 0; side < 2; side++)
            foreach (var point in face)
                vertices.Add(new Vector3(point.x, point.y, .012f - 6 * point.x * point.x - side * .008f));
        int n = face.Length;
        for (int i = 1; i + 1 < n; i++) indices.AddRange(new[] { 0, i, i + 1, n, n + i + 1, n + i });
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            indices.AddRange(new[] { i, n + i, next, next, n + i, n + next });
        }
        var mesh = new Mesh { name = "Water Cup Glass Shard" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, ShardMesh);
        return mesh;
    }

}
