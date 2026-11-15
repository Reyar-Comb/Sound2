using UnityEngine;

namespace Sound2.LevelEditing
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonsterVineArea))]
    public sealed class MonsterVineAreaMotion : MonoBehaviour
    {
        [InspectorName("起点（父物体局部坐标）")] public Vector3 startPosition;
        [InspectorName("终点（父物体局部坐标）")] public Vector3 endPosition = Vector3.forward * 4;
        [Min(0), InspectorName("移动速度（米/秒）")] public float speed = 1;
        [InspectorName("往返移动")] public bool pingPong = true;
        PlayerVitality player;
        bool towardsEnd = true, finished;
        public Vector3 StartWorld => transform.parent != null ? transform.parent.TransformPoint(startPosition) : startPosition;
        public Vector3 EndWorld => transform.parent != null ? transform.parent.TransformPoint(endPosition) : endPosition;

        void Reset()
        {
            startPosition = transform.localPosition;
            var endpoint = transform.position + transform.forward * 4;
            endPosition = transform.parent != null ? transform.parent.InverseTransformPoint(endpoint) : endpoint;
        }
        void OnEnable()
        {
            if (!Application.isPlaying) return;
            BindPlayer(); ResetToStart();
        }
        void OnDisable()
        {
            if (player != null) player.Died -= ResetToStart;
            player = null;
        }
        void BindPlayer()
        {
            if (player != null) return;
            foreach (var candidate in FindObjectsByType<PlayerVitality>())
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                player = candidate; player.Died += ResetToStart; break;
            }
        }
        public void ResetToStart()
        {
            transform.localPosition = startPosition; towardsEnd = true; finished = false;
            GetComponent<MonsterVines>()?.ClearTrail();
            GetComponent<MonsterAudioEmitter>()?.ResetMotionAudio();
        }
        void Update()
        {
            BindPlayer();
            if (finished || speed <= 0 || Time.deltaTime <= 0 || SonicSpaceStartMenu.BlocksGameplay) return;
            if (player != null && (player.IsDead || player.GetComponent<PlayerController>()?.DeathSequenceLocked == true)) return;
            var target = towardsEnd ? EndWorld : StartWorld;
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            if ((transform.position - target).sqrMagnitude > .000001f) return;
            if (pingPong) towardsEnd = !towardsEnd; else finished = true;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow; Gizmos.DrawLine(StartWorld, EndWorld);
            Gizmos.DrawWireSphere(StartWorld, .2f); Gizmos.color = Color.red; Gizmos.DrawWireSphere(EndWorld, .2f);
        }
    }
}
