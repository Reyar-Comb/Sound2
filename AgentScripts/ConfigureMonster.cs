using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class ConfigureMonster
{
    public static object Main()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SonarHall.unity") throw new System.InvalidOperationException("Open SonarHall.");
        var sonar = Object.FindAnyObjectByType<SphericalSonar>();
        var player = sonar.GetComponentInParent<PlayerController>();
        var world = Object.FindAnyObjectByType<RuntimePatrolNavigation>();
        if (world == null) world = new GameObject("Monster Navigation").AddComponent<RuntimePatrolNavigation>();
        world.geometryRoot = GameObject.Find("Circular Hall and Passages").transform;
        var monster = Object.FindAnyObjectByType<PatrolMonster>();
        if (monster == null)
        {
            var actor = new GameObject("Patrolling Monster (invisible)");
            actor.transform.position = new Vector3(9,0,0);
            var agent = actor.AddComponent<NavMeshAgent>(); agent.enabled = false;
            monster = actor.AddComponent<PatrolMonster>(); monster.navigation = world;
            var scan = actor.AddComponent<MonsterScanEmitter>(); scan.player = player.transform;
            scan.pulseField = sonar.GetComponent<SonarPulseField>();
            var route = new GameObject("Monster Patrol Route");
            var positions = new[] { new Vector3(9,0,0), new Vector3(3,0,2), new Vector3(-3,0,1),
                new Vector3(0,0,-9), new Vector3(2,0,-3) };
            monster.patrolPoints = new Transform[positions.Length];
            for(int i=0;i<positions.Length;i++) {
                var point = new GameObject("Patrol " + (i+1)); point.transform.SetParent(route.transform);
                point.transform.position = positions[i]; monster.patrolPoints[i]=point.transform;
            }
        }
        // Make the lure usable in this scene without changing the shared item/player prefabs.
        var ball = GameObject.Find("PickupBouncyBall");
        if (ball == null)
        {
            ball = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"),scene);
            ball.name = "PickupBouncyBall"; ball.transform.position = new Vector3(1.4f,.24f,.65f);
            var objectSonar = ball.GetComponent<SonarObjectEmitter>(); objectSonar.sonar = sonar;
            objectSonar.pulseField = sonar.GetComponent<SonarPulseField>();
            PrefabUtility.RecordPrefabInstancePropertyModifications(ball.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(objectSonar);
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = monster.gameObject;
        return new { scene=scene.path, monster=monster.name, patrolPoints=monster.patrolPoints.Length,
            invisible=monster.GetComponentsInChildren<Renderer>().Length==0, ball=ball.name };
    }
}
