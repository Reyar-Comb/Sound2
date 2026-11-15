using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Sound2.LevelEditing
{
    /// <summary>A stationary authoring anchor; the existing monster moves independently below it.</summary>
    [DisallowMultipleComponent]
    public sealed class LevelEnemy : MonoBehaviour
    {
        public PatrolMonster actor;
        [Min(1)] public float patrolRadius = 6;
        bool routeBuilt;

        void OnEnable() => routeBuilt = false;
        void Update()
        {
            if (routeBuilt || actor == null || actor.navigation == null || !actor.NavigationReady) return;
            BuildPatrol();
            routeBuilt = true;
        }

        public void BuildPatrol()
        {
            if (!Application.isPlaying || actor == null || !actor.NavigationReady) return;
            var old = transform.Find("Automatic Patrol");
            if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            var route = new GameObject("Automatic Patrol").transform;
            route.SetParent(transform, false);
            var points = new List<Transform>();
            var agent = actor.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            var path = new NavMeshPath();
            void Add(Vector3 candidate)
            {
                if (!NavMesh.SamplePosition(candidate, out var hit, .75f, filter)
                    || !agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return;
                foreach (var point in points) if ((point.position - hit.position).sqrMagnitude < .25f) return;
                var target = new GameObject("Patrol " + (points.Count + 1)).transform;
                target.SetParent(route, false); target.position = hit.position; points.Add(target);
            }
            // Near points work even in small rooms; outer points give larger rooms a useful route.
            for (int ring = 1; ring <= 2; ring++)
                for (int i = 0; i < 8; i++)
                    Add(transform.position + Quaternion.AngleAxis(i * 45, Vector3.up) * Vector3.forward * (patrolRadius * ring / 2));
            var level = GetComponentInParent<LevelLayout>();
            if (level != null)
                foreach (var room in level.rooms)
                    if (Vector3.Distance(level.World(room.center), transform.position) <= patrolRadius)
                        Add(level.World(room.center));
            Add(actor.transform.position);
            actor.patrolPoints = points.ToArray();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1, .25f, .25f);
            Vector3 position = Application.isPlaying && actor != null ? actor.transform.position : transform.position;
            Gizmos.DrawWireSphere(position + Vector3.up * .9f, .35f);
            Gizmos.DrawLine(position, position + Vector3.up * 1.8f);
        }
    }
}
