using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class VerifyLevelEnemies
{
    const string Folder = "Assets/__EnemyPlacementVerification";
    const string Prefab = Folder + "/EnemyLevel.prefab";
    static readonly List<string> checks = new();
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks.Add("PASS: " + message); }

    public static object EditMode()
    {
        if (Application.isPlaying) throw new Exception("Exit play mode first");
        if (AssetDatabase.IsValidFolder(Folder)) throw new Exception("Verification folder already exists");
        AssetDatabase.CreateFolder("Assets", "__EnemyPlacementVerification");
        var previous = SceneManager.GetActiveScene();
        var selection = Selection.activeObject;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        LevelDesignerWindow window = null;
        try
        {
            var root = new GameObject("Enemy verification level");
            root.transform.position = new Vector3(2000, 0, 2000);
            var level = root.AddComponent<LevelLayout>();
            level.rooms.Add(new LevelRoom { size = new Vector2(20, 20) });
            level.pits.Add(new LevelPit { center = new Vector2(7, 7), size = 2, depth = 4 });
            LevelBuilder.Rebuild(level);
            Physics.SyncTransforms();
            Check(LevelEnemyPlacement.Ground(level, new Ray(level.World(Vector2.zero, 2), Vector3.down), out var ground), "Placement finds the authored floor");
            Check(!LevelEnemyPlacement.Ground(level, new Ray(level.World(new Vector2(7, 7), 2), Vector3.down), out _), "Placement rejects pit openings");
            Check(!LevelEnemyPlacement.Ground(level, new Ray(level.World(new Vector2(30, 30), 2), Vector3.down), out _), "Placement rejects outside the level");
            Check(LevelEnemyPlacement.HasClearance(ground), "Open floor has capsule clearance");
            Check(!LevelEnemyPlacement.HasClearance(level.World(new Vector2(9.8f, 0))), "Wall overlap rejects enemy placement");
            var enemy = LevelEnemyPlacement.Place(level, level.World(new Vector2(-4, -3)));
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Check(enemy.actor.navigation == level.GetComponent<RuntimePatrolNavigation>(), "Placement automatically binds a level-owned navigation map");
            Check(enemy.actor.GetComponent<MonsterScanEmitter>() != null && enemy.actor.GetComponent<MonsterVines>() != null
                && enemy.actor.GetComponent<MonsterAudioEmitter>().profile != null, "Scan, vines and audio are preconfigured");
            Undo.PerformUndo();
            Check(level.GetComponentsInChildren<LevelEnemy>().Length == 0 && level.GetComponent<RuntimePatrolNavigation>() == null, "One undo removes the first enemy and its automatically added navigation");
            Undo.PerformRedo();
            enemy = level.GetComponentInChildren<LevelEnemy>();
            Check(enemy != null && enemy.actor.navigation == level.GetComponent<RuntimePatrolNavigation>(), "Redo restores the complete enemy and its references");
            var second = LevelEnemyPlacement.Place(level, level.World(new Vector2(4, -3)));
            var nav = level.GetComponent<RuntimePatrolNavigation>();
            Check(level.GetComponentsInChildren<RuntimePatrolNavigation>().Length == 1 && second.actor.navigation == enemy.actor.navigation, "Multiple enemies share one navigation map");
            Undo.RecordObject(level, "Change climb fixture"); level.enemyStepHeight = .9f;
            LevelEnemyPlacement.ConfigureNavigation(level);
            Check(Mathf.Approximately(nav.BuildSettings().agentClimb, .9f), "Level climb height reaches the actual bake settings");
            level.enemyStepHeight = .3f; LevelEnemyPlacement.ConfigureNavigation(level);
            var step = GameObject.CreatePrimitive(PrimitiveType.Cube); step.name = "Climb fixture";
            step.transform.SetParent(level.transform, false); step.transform.localPosition = new Vector3(0, .3f, 1);
            step.transform.localScale = new Vector3(4, .6f, 4);
            var originalActor = enemy.actor;
            LevelBuilder.Rebuild(level);
            Check(enemy.actor == originalActor && nav.geometryRoot == level.transform, "Geometry rebuild preserves enemies and a stable geometry root");
            window = ScriptableObject.CreateInstance<LevelDesignerWindow>(); window.CreateGUI(); window.Bind(level);
            Check(window.rootVisualElement.Query<Button>().ToList().Any(b => b.text == "放置敌人"), "Enemy placement is a top-level editor tool");
            Check(window.rootVisualElement.Query<Button>().ToList().Any(b => b.text == "一键应用到本关全部敌人"), "One-click level setting is available in the editor");
            LevelBuilder.ExportPrefab(level, Prefab);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var copiedEnemy = prefab.GetComponentInChildren<LevelEnemy>();
            Check(prefab.GetComponentsInChildren<LevelEnemy>().Length == 2
                && copiedEnemy.actor.navigation == prefab.GetComponent<RuntimePatrolNavigation>()
                && copiedEnemy.actor.navigation.geometryRoot == prefab.transform, "Prefab export retains both enemies and internal navigation references");
            EditorSceneManager.SaveScene(scene, Folder + "/Source.unity");
            LevelBuilder.Export(level, Folder + "/EnemyLevel.unity", true);
            var exported = EditorSceneManager.OpenScene(Folder + "/EnemyLevel.unity", OpenSceneMode.Additive);
            try
            {
                var copy = exported.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LevelLayout>()).Single();
                Check(copy.GetComponentsInChildren<LevelEnemy>().All(e => e.actor.navigation == copy.GetComponent<RuntimePatrolNavigation>()), "Scene export preserves enemy bindings");
                Check(exported.GetRootGameObjects().Any(g => g.GetComponentInChildren<PlayerController>() != null), "Playable scene export contains the configured player");
            }
            finally { EditorSceneManager.CloseScene(exported, true); }
            return new { passed = true, checks };
        }
        finally
        {
            if (window != null) Object.DestroyImmediate(window);
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); Selection.activeObject = selection;
        }
    }

    static async Task Tick(int milliseconds)
    {
        int before = Time.frameCount; await Task.Delay(milliseconds);
        Check(Application.isPlaying && Time.frameCount > before, "Player loop advances");
    }

    public static async Task<object> Runtime()
    {
        if (!Application.isPlaying) throw new Exception("Enter play mode first");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(prefab, new Vector3(2000, 0, 2000), Quaternion.identity);
            var level = instance.GetComponent<LevelLayout>();
            var nav = instance.GetComponent<RuntimePatrolNavigation>();
            var enemies = instance.GetComponentsInChildren<LevelEnemy>();
            Physics.SyncTransforms();
            await Tick(650);
            Check(nav.Ready && nav.SourceCount > 0 && enemies.All(e => e.actor.NavigationReady), "Exported enemies initialize on the runtime-built map");
            Check(enemies.All(e => e.actor.patrolPoints.Length > 1), "Reachable patrol routes are automatic");
            var actor = enemies[0].actor;
            var start = actor.transform.position;
            await Tick(1400);
            Check(Vector3.Distance(start, actor.transform.position) > .15f, "Placed enemy actually moves along its patrol");
            Check(actor.GetComponent<MonsterVines>().PointCount > 0, "Moving enemy grows the existing vines");
            var scan = actor.GetComponent<MonsterScanEmitter>();
            Check(scan.player != null && scan.pulseField != null && scan.EmitNow(), "Scan finds the player and sonar field without manual references");
            Check(actor.GetComponent<MonsterAudioEmitter>().ReplacesScanSound, "Existing audio profile replaces scan sound");
            var navAgent = actor.GetComponent<NavMeshAgent>();
            var path = new NavMeshPath();
            Vector3 lower = level.World(new Vector2(-4, 1)), upper = level.World(new Vector2(0, 1), .6f);
            bool CanClimb()
            {
                if (!NavMesh.SamplePosition(lower, out var from, .15f, NavMesh.AllAreas)
                    || !NavMesh.SamplePosition(upper, out var to, .15f, NavMesh.AllAreas)) throw new Exception("Step endpoints not on mesh");
                return NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            }
            Check(!CanClimb(), "0.3m climb cannot reach a 0.6m platform");
            foreach (var e in enemies) e.actor.GetComponent<NavMeshAgent>().enabled = false;
            level.enemyStepHeight = .9f; nav.Rebuild();
            Check(CanClimb(), "One level setting of 0.9m makes the 0.6m platform reachable");
            await Tick(350);
            Check(enemies.All(e => e.actor.NavigationReady), "All enemies recover after rebuilding navigation");
            int heard = actor.HeardSounds;
            GameplayNoise.Emit(actor.transform.position + Vector3.forward, 30, GameplayNoiseKind.Impact, null);
            Check(actor.HeardSounds == heard + 1 && actor.State == MonsterBehaviourState.Investigate, "Placed enemy uses existing hearing and investigation");
            instance.SetActive(false); await Tick(100); instance.SetActive(true); await Tick(350);
            Check(nav.Ready && enemies.All(e => e.actor.NavigationReady), "Level deactivate/reactivate restores navigation and agents");
            Check(enemies.All(e => e.transform.Find("Automatic Patrol") != null), "Patrol routes survive level reactivation");
            return new { passed = true, checks };
        }
        finally { if (instance != null) Object.Destroy(instance); }
    }

    public static object Cleanup()
    {
        if (Application.isPlaying) throw new Exception("Exit play mode first");
        return new { removed = AssetDatabase.DeleteAsset(Folder) };
    }
}
