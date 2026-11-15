using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyFloorLoading
{
    const string Path = "Assets/Scenes/__FloorLoadingTest.unity";
    public static object Prepare()
    {
        if (Application.isPlaying || File.Exists(Path)) throw new Exception("Test scene already exists or playing");
        var original = SceneManager.GetActiveScene();
        var previous = EditorSceneManager.playModeStartScene;
        SessionState.SetString("FloorLoading.PreviousPlayScene", previous == null ? "" : AssetDatabase.GetAssetPath(previous));
        var library = EditorSceneManager.OpenScene("Assets/Levels/LevelLibrary.unity", OpenSceneMode.Additive);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var world = new GameObject("Loading test world").AddComponent<LevelWorld>();
            var source = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Levels/F1.prefab"), scene)).GetComponent<LevelLayout>();
            var template = library.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelLayout>(true)).Single();
            var destination = Object.Instantiate(template);
            SceneManager.MoveGameObjectToScene(destination.gameObject, scene);
            source.name = "Source"; destination.name = "Destination";
            source.transform.position = Vector3.zero; destination.transform.position = new Vector3(300, 0, 0);
            source.transform.SetParent(world.transform); destination.transform.SetParent(world.transform);
            var nav = destination.GetComponent<RuntimePatrolNavigation>() ?? destination.gameObject.AddComponent<RuntimePatrolNavigation>();
            nav.geometryRoot = destination.transform;
            var a = source.GetComponentInChildren<LevelElevator>(true);
            var b = destination.GetComponentInChildren<LevelElevator>(true);
            if (a == null || b == null) throw new Exception("Missing elevators");
            var player = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"), scene)).GetComponent<PlayerController>();
            player.transform.position = a.transform.TransformPoint(new Vector3(0, 1.1f, 0));
            var so = new SerializedObject(player); so.FindProperty("useGravity").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            world.player = player; world.startingLevel = source; world.levels.Add(source); world.levels.Add(destination);
            world.connections.Add(new ElevatorConnection { source = a, destination = b, travelSeconds = .2f, bidirectional = true });
            destination.gameObject.SetActive(false);
            EditorSceneManager.SaveScene(scene, Path);
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true); EditorSceneManager.CloseScene(library, true);
        }
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Path);
        return new { original.path, original.isDirty, testScene = Path };
    }

    public static object Baseline()
    {
        var world = Object.FindAnyObjectByType<LevelWorld>();
        var level = world.levels[1];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        level.gameObject.SetActive(true);
        double activationMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart(); level.gameObject.SetActive(false);
        var report = new { activationMs, deactivationMs = clock.Elapsed.TotalMilliseconds,
            objects = level.GetComponentsInChildren<Transform>(true).Length,
            props = level.GetComponentsInChildren<LevelProp>(true).Length };
        Directory.CreateDirectory("Logs/FloorLoading");
        File.WriteAllText("Logs/FloorLoading/baseline.json", Newtonsoft.Json.JsonConvert.SerializeObject(report));
        return report;
    }

    static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception(message);
    }
    static async Task Until(Func<bool> predicate, string message, double timeout = 30)
    {
        double deadline = EditorApplication.timeSinceStartup + timeout;
        while (!predicate())
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception(message);
            await Task.Delay(10);
        }
    }
    static async Task Frame()
    {
        int frame = Time.frameCount;
        await Until(() => Time.frameCount > frame, "Player loop stalled", 5);
    }
    public static async Task<object> Play()
    {
        Check(Application.isPlaying && SceneManager.GetActiveScene().path == Path, "Isolated test scene required");
        var world = Object.FindAnyObjectByType<LevelWorld>();
        var destination = world.levels[1];
        var nav = destination.GetComponent<RuntimePatrolNavigation>();
        var menu = Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if (menu != null && SonicSpaceStartMenu.BlocksGameplay) menu.BeginGame();
        await Frame();
        // Measure only each main-thread loading slice, with real player-loop frames in between.
        var type = typeof(LevelWorld).Assembly.GetType("Sound2.LevelEditing.LevelActivation");
        var preparation = (IDisposable)Activator.CreateInstance(type, destination, 2f, 24);
        var iterator = (IEnumerator)type.GetMethod("Prepare").Invoke(preparation, null);
        var slices = new List<double>();
        int frameStart = Time.frameCount;
        try
        {
            while (true)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew(); bool more = iterator.MoveNext();
                slices.Add(watch.Elapsed.TotalMilliseconds);
                if (!more) break;
                if (destination.IsPreparing) Check(!nav.Ready, "Navigation published before all colliders were active");
                await Frame();
            }
            await Until(() => nav.Ready, "Navigation failed to complete");
            Check(nav.SourceCount > 900, "Navigation missed staged library colliders");
            Check(destination.GetComponentsInChildren<LevelProp>().Length == 961, "Some props were not restored");
        }
        finally { preparation.Dispose(); }
        Check(!destination.gameObject.activeSelf && !nav.Ready && !nav.IsBuilding, "Rollback did not cancel navigation");
        int stagedFrames = Time.frameCount - frameStart;
        // Cancel an actual elevator trip while staging and preserve authored inactive objects.
        var disabled = new GameObject("Authored inactive"); disabled.transform.SetParent(destination.transform); disabled.SetActive(false);
        var state = destination.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.gameObject.activeSelf);
        var a = world.connections[0].source; var b = world.connections[0].destination;
        world.player.Teleport(a.transform.TransformPoint(new Vector3(0, 1.1f, 0)));
        a.Open(true); await Until(() => a.DoorOpen > .999f, "Source did not open");
        Check(world.Travel(a, world.player), "Travel refused");
        Check(!world.Travel(a, world.player), "Duplicate travel was accepted");
        await Until(() => destination.IsPreparing && destination.gameObject.activeInHierarchy, "Staged activation never started");
        Check(destination.GetComponentsInChildren<LevelProp>().Length < 961, "Cancellation test missed partially active floor");
        world.enabled = false;
        Check(!world.Travelling && !a.Busy && !b.Busy && !destination.IsPreparing && !destination.gameObject.activeSelf, "Cancelled trip left locks or partial floor");
        Check(state.All(p => p.Key.gameObject.activeSelf == p.Value), "Cancellation lost original activeSelf values");
        world.enabled = true;
        await Until(() => a.DoorOpen > .999f, "Cancellation did not reopen source");
        var cargo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cargo.name = "Loading regression cargo"; cargo.transform.SetParent(world.levels[0].transform);
        cargo.transform.position = a.transform.TransformPoint(new Vector3(.8f, .5f, 0));
        var body = cargo.AddComponent<Rigidbody>(); body.useGravity = false;
        Check(world.Travel(a, world.player), "Retry refused");
        await Until(() => !world.Travelling, "Trip did not finish");
        Check(world.CurrentLevel == destination && b.Contains(world.player.transform.position), "Player arrived at wrong floor");
        Check(nav.Ready && nav.SourceCount > 900 && b.DoorOpen > .999f, "Doors opened without a complete floor/navigation");
        Check(!disabled.activeSelf && !body.isKinematic && b.Contains(body.position), "Cargo or inactive object state lost");
        Check(world.player.GetComponent<PlayerFloorProgression>().CurrentFloor == 2 && world.ArrivalCheckpoint == b, "Progress/checkpoint not committed");
        Check(world.Travel(b, world.player), "Return refused");
        await Until(() => !world.Travelling, "Return did not finish");
        Check(world.CurrentLevel == world.levels[0] && !destination.gameObject.activeSelf && !nav.Ready, "Return did not retire library");
        Check(!body.isKinematic && a.Contains(body.position), "Cargo did not survive return trip");
        Check(world.Travel(a, world.player), "Second visit refused");
        await Until(() => !world.Travelling, "Second visit did not finish");
        Check(nav.Ready && b.Contains(world.player.transform.position), "Navigation not restored on revisit");
        // Rebuild/cancel/restart must not publish obsolete NavMeshData.
        nav.Rebuild(); nav.Rebuild(); nav.enabled = false;
        Check(!nav.Ready && !nav.IsBuilding, "Disabled navigation retained build");
        nav.enabled = true;
        await Until(() => nav.Ready, "Cancelled navigation could not restart");
        nav.Rebuild();
        var operationField = typeof(RuntimePatrolNavigation).GetField("operation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await Until(() => operationField.GetValue(nav) != null, "Native asynchronous build never started");
        nav.enabled = false;
        await Frame();
        Check(!nav.Ready && !nav.IsBuilding && operationField.GetValue(nav) == null, "Native cancellation published stale navigation");
        nav.enabled = true;
        await Until(() => nav.Ready, "Native build cancellation could not restart");
        var report = new { passed = true, stagedFrames, slices = slices.Count, maxActivationSliceMs = slices.Max(),
            medianActivationSliceMs = slices.OrderBy(v => v).ElementAt(slices.Count / 2), nav.SourceCount,
            checks = "partial readiness, all 961 props, cancellation, inactive flags, retry, duplicate travel, cargo, arrival gate, progression, checkpoint, round trip, revisit, navigation rebuild/cancel/restart" };
        Directory.CreateDirectory("Logs/FloorLoading");
        File.WriteAllText("Logs/FloorLoading/after.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return report;
    }

    public static object Cleanup()
    {
        if (Application.isPlaying) throw new Exception("Stop play first");
        string previous = SessionState.GetString("FloorLoading.PreviousPlayScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        AssetDatabase.DeleteAsset(Path);
        return "Restored play scene and removed isolated test scene";
    }
}
