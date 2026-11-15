using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Sound2.LevelEditing.Editor
{
    public static class LevelEnemyPlacement
    {
        public static RuntimePatrolNavigation ConfigureNavigation(LevelLayout level)
        {
            if (level == null || Application.isPlaying) throw new InvalidOperationException("请在编辑模式选择关卡。");
            var nav = level.GetComponent<RuntimePatrolNavigation>();
            if (nav == null) nav = Undo.AddComponent<RuntimePatrolNavigation>(level.gameObject);
            Undo.RecordObject(nav, "设置关卡敌人导航");
            nav.enabled = true; nav.geometryRoot = level.transform;
            nav.agentRadius = .35f; nav.agentHeight = 1.8f;
            nav.agentClimb = Mathf.Clamp(level.enemyStepHeight, 0, nav.agentHeight);
            nav.geometryLayers = Physics.DefaultRaycastLayers;
            foreach (var other in level.GetComponentsInChildren<RuntimePatrolNavigation>(true))
            {
                if (other == nav || other.GetComponentInParent<LevelLayout>() != level) continue;
                Undo.RecordObject(other, "合并关卡敌人导航"); other.enabled = false;
                Dirty(other);
            }
            foreach (var actor in level.GetComponentsInChildren<PatrolMonster>(true))
            {
                if (actor.GetComponentInParent<LevelLayout>() != level) continue;
                Undo.RecordObject(actor, "关联关卡敌人导航"); actor.navigation = nav; Dirty(actor);
                var agent = actor.GetComponent<NavMeshAgent>();
                Undo.RecordObject(agent, "设置敌人体型");
                agent.radius = nav.agentRadius; agent.height = nav.agentHeight;
                agent.agentTypeID = nav.BuildSettings().agentTypeID; Dirty(agent);
            }
            Dirty(nav); EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            return nav;
        }

        public static bool Ground(LevelLayout level, Ray ray, out Vector3 position)
        {
            position = default; float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(ray, 10000, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var collider = hit.collider;
                if (collider.GetComponentInParent<LevelLayout>() != level || collider.attachedRigidbody != null
                    || collider.GetComponentInParent<NavMeshAgent>() != null || hit.normal.y < .7f || hit.distance >= nearest) continue;
                nearest = hit.distance; position = hit.point;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        public static bool HasClearance(Vector3 position)
            => !Physics.CheckCapsule(position + Vector3.up * .37f, position + Vector3.up * 1.44f,
                .34f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        public static LevelEnemy Place(LevelLayout level, Vector3 position, float radius = 6)
        {
            if (level == null || Application.isPlaying) throw new InvalidOperationException("请在编辑模式选择关卡。");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("放置敌人");
            var nav = ConfigureNavigation(level);
            var root = new GameObject("巡逻敌人 " + (level.GetComponentsInChildren<LevelEnemy>(true).Length + 1));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, level.gameObject.scene);
            root.transform.SetParent(level.transform, false); root.transform.position = position;
            var enemy = root.AddComponent<LevelEnemy>(); enemy.patrolRadius = Mathf.Max(1, radius);
            var body = new GameObject("Monster"); body.transform.SetParent(root.transform, false);
            var agent = body.AddComponent<NavMeshAgent>(); agent.enabled = false;
            agent.agentTypeID = nav.BuildSettings().agentTypeID; agent.radius = nav.agentRadius; agent.height = nav.agentHeight;
            agent.stoppingDistance = .25f; agent.acceleration = 5; agent.angularSpeed = 180;
            enemy.actor = body.AddComponent<PatrolMonster>(); enemy.actor.navigation = nav;
            body.AddComponent<MonsterVines>(); body.AddComponent<MonsterScanEmitter>();
            body.AddComponent<MonsterAudioEmitter>().profile = AssetDatabase.LoadAssetAtPath<MonsterAudioProfile>("Assets/Resources/MonsterAudio.asset");
            Undo.RegisterCreatedObjectUndo(root, "放置敌人");
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            return enemy;
        }

        public static void Dirty(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
