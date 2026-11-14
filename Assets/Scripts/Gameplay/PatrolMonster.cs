using UnityEngine;
using UnityEngine.AI;

public enum MonsterBehaviourState { Patrol, Investigate, Search, Evade }

/// <summary>Hears event locations and navigates to them; never uses the player's live position to pursue.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(NavMeshAgent))]
public sealed class PatrolMonster : MonoBehaviour
{
    public RuntimePatrolNavigation navigation;
    public Transform[] patrolPoints;
    [Min(.1f)] public float patrolSpeed = 1.1f;
    [Min(.1f)] public float investigateSpeed = 1.8f;
    [Min(0)] public float patrolPause = .8f;
    [Min(.1f)] public float searchSeconds = 3;
    [Min(1)] public float investigateTimeout = 14;
    [Header("Lightning avoidance")]
    [Min(0)] public float lightningSafetyMargin = .5f;
    [Min(.1f)] public float evadeRepathInterval = .6f;
    public bool AvoidingLightning => State == MonsterBehaviourState.Evade;
    [Header("Hearing")]
    [Min(0)] public float hearingSensitivity = 1;
    [Range(0,1)] public float throughWallRangeMultiplier = .45f;
    public LayerMask hearingObstacles = Physics.DefaultRaycastLayers;
    [Min(0)] public float soundPriorityMemory = 1.5f;
    public MonsterBehaviourState State { get; private set; }
    public Vector3 LastHeardPosition { get; private set; }
    public GameplayNoiseKind LastHeardKind { get; private set; }
    public int HeardSounds { get; private set; }
    public bool NavigationReady => agent != null && agent.enabled && agent.isOnNavMesh;
    public Vector3 Destination => NavigationReady ? agent.destination : transform.position;
    NavMeshAgent agent;
    NavMeshPath path;
    int waypoint;
    bool hasDestination, initialized;
    float waitUntil, investigateUntil, lastHeardAt = -100, lastScore;

    MonsterBehaviourState resumeState;
    Vector3 resumeTarget;
    bool resumeHasTarget;
    float nextEvadePath;

    void Awake()
    {
        path = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        agent.enabled = false;
        // The actor contributes neither a visible mesh nor camera depth for sonar to reveal.
        foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
    }
    void OnEnable()
    {
        GameplayNoise.Emitted += Hear;
        if (NavigationReady) agent.isStopped = false;
    }
    void OnDisable()
    {
        GameplayNoise.Emitted -= Hear;
        if (NavigationReady) agent.isStopped = true;
    }
    void Start()
    {
        if (navigation == null) navigation = FindAnyObjectByType<RuntimePatrolNavigation>();
        agent.radius = navigation != null ? navigation.agentRadius : .35f;
        agent.height = navigation != null ? navigation.agentHeight : 1.8f;
        agent.stoppingDistance = .25f; agent.acceleration = 5; agent.angularSpeed = 180;
    }
    void Update()
    {
        if (navigation == null || !navigation.Ready) return;
        if (!initialized)
        {
            if (!NavMesh.SamplePosition(transform.position, out var start, 2, agent.areaMask)) return;
            transform.position = start.position;
            agent.enabled = true;
            if (!agent.Warp(start.position)) return;
            initialized = true; State = MonsterBehaviourState.Patrol;
        }
        if (!NavigationReady) return;
        if (AvoidLightning()) return;
        if (State == MonsterBehaviourState.Search)
        {
            if (Time.time >= waitUntil) { State = MonsterBehaviourState.Patrol; hasDestination = false; }
            return;
        }
        if (State == MonsterBehaviourState.Investigate)
        {
            if (Time.time >= investigateUntil || Arrived())
            {
                agent.ResetPath(); hasDestination = false;
                State = MonsterBehaviourState.Search; waitUntil = Time.time + searchSeconds;
            }
            return;
        }
        agent.speed = patrolSpeed;
        if (hasDestination && Arrived())
        { hasDestination = false; waypoint++; waitUntil = Time.time + patrolPause; }
        if (!hasDestination && Time.time >= waitUntil && patrolPoints != null && patrolPoints.Length > 0)
        {
            for (int attempt = 0; attempt < patrolPoints.Length; attempt++, waypoint++)
            {
                var point = patrolPoints[waypoint % patrolPoints.Length];
                if (point != null && NavigateTo(point.position)) break;
            }
            if (!hasDestination) waitUntil = Time.time + 1;
        }
    }
    bool Arrived() => hasDestination && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + .1f;
    bool NavigateTo(Vector3 position)
    {
        if (!NavigationReady || !NavMesh.SamplePosition(position, out var target, 2, agent.areaMask)) return false;
        // Native path handles do not survive an Editor script reload.
        if (path == null) path = new NavMeshPath();
        if (!agent.CalculatePath(target.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        hasDestination = agent.SetPath(path); return hasDestination;
    }
    void Hear(GameplayNoiseEvent sound)
    {
        if (!isActiveAndEnabled || AvoidingLightning || !NavigationReady || sound.source == this || hearingSensitivity <= 0) return;
        var ear = transform.position + Vector3.up;
        float distance = Vector3.Distance(ear, sound.position);
        float audibleRange = sound.range * hearingSensitivity;
        if (distance > audibleRange) return;
        Vector3 delta = sound.position + Vector3.up * .1f - ear;
        bool blocked = false;
        foreach (var hit in Physics.RaycastAll(ear, delta.normalized, delta.magnitude, hearingObstacles, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance >= delta.magnitude - .2f || hit.transform.IsChildOf(transform)
                || (sound.source != null && hit.transform.IsChildOf(sound.source.transform))) continue;
            blocked = true; break;
        }
        if (blocked) audibleRange *= throughWallRangeMultiplier;
        if (distance > audibleRange) return;
        float score = audibleRange / Mathf.Max(1, distance);
        if (Time.time - lastHeardAt < soundPriorityMemory && score < lastScore * .75f) return;
        if (!NavigateTo(sound.position)) return;
        LastHeardPosition = sound.position; LastHeardKind = sound.kind; HeardSounds++;
        lastHeardAt = Time.time; lastScore = score;
        State = MonsterBehaviourState.Investigate;
        investigateUntil = Time.time + investigateTimeout;
        agent.speed = investigateSpeed;
    }
    static Vector2 Flat(Vector3 p) => new Vector2(p.x,p.z);
    static float SegmentDistance(Vector3 p,Vector3 a,Vector3 b)
    {
        Vector2 delta=Flat(b)-Flat(a);
        float t=delta.sqrMagnitude>.0001f?Mathf.Clamp01(Vector2.Dot(Flat(p)-Flat(a),delta)/delta.sqrMagnitude):0;
        return Vector2.Distance(Flat(p),Flat(a)+delta*t);
    }
    static float PathClearance(NavMeshPath route,Vector3 center)
    {
        float distance=float.PositiveInfinity;var corners=route.corners;
        for(int i=1;i<corners.Length;i++)distance=Mathf.Min(distance,SegmentDistance(center,corners[i-1],corners[i]));
        return distance;
    }
    bool AvoidLightning()
    {
        BallLightning threat=null;
        float closest=float.PositiveInfinity;
        foreach(var hazard in BallLightning.Instances)
        {
            if(hazard==null || !hazard.isActiveAndEnabled)continue;
            float distance=Vector2.Distance(Flat(transform.position),Flat(hazard.Center));
            float radius=hazard.AvoidanceRadius+agent.radius+lightningSafetyMargin;
            if(distance>hazard.monsterSenseRadius || distance>=closest)continue;
            bool obstructs=agent.hasPath && PathClearance(agent.path,hazard.Center)<radius;
            if(distance<radius+.3f || obstructs){threat=hazard;closest=distance;}
        }
        if(threat==null)
        {
            if(!AvoidingLightning)return false;
            if(hasDestination && !Arrived())return true;
            State=resumeState;hasDestination=false;
            if(State==MonsterBehaviourState.Investigate && resumeHasTarget)
            {
                if(!NavigateTo(resumeTarget)){State=MonsterBehaviourState.Search;waitUntil=Time.time+searchSeconds;}
            }
            else if(State==MonsterBehaviourState.Search)waitUntil=Time.time+searchSeconds;
            return false;
        }
        if(!AvoidingLightning)
        {
            resumeState=State;resumeHasTarget=hasDestination;resumeTarget=agent.hasPath?agent.destination:transform.position;
            State=MonsterBehaviourState.Evade;nextEvadePath=0;
        }
        if(Time.time<nextEvadePath)return true;
        nextEvadePath=Time.time+Mathf.Max(.1f,evadeRepathInterval);
        if(path==null)path=new NavMeshPath();
        Vector3 center=threat.transform.position;
        Vector3 radial=transform.position-center;radial.y=0;
        if(radial.sqrMagnitude<.01f)radial=Vector3.right;
        radial.Normalize();
        float safeRadius=threat.AvoidanceRadius+agent.radius+lightningSafetyMargin;
        float allowed=Mathf.Min(closest,safeRadius)-.12f;
        float best=float.PositiveInfinity;Vector3 bestTarget=default;bool found=false;
        for(int ring=0;ring<2;ring++)for(int i=0;i<12;i++)
        {
            var candidate=center+Quaternion.AngleAxis(i*30,Vector3.up)*radial*(safeRadius+1.2f+ring*2);
            if(!NavMesh.SamplePosition(candidate,out var sample,.7f,agent.areaMask)
                || !agent.CalculatePath(sample.position,path) || path.status!=NavMeshPathStatus.PathComplete)continue;
            if(Vector2.Distance(Flat(sample.position),Flat(center))<safeRadius+.7f || PathClearance(path,center)<allowed)continue;
            bool otherHazard=false;
            foreach(var other in BallLightning.Instances)
                if(other!=null && other!=threat && other.isActiveAndEnabled
                    && Vector2.Distance(Flat(transform.position),Flat(other.Center))<=other.monsterSenseRadius
                    && PathClearance(path,other.Center)<other.AvoidanceRadius+agent.radius+lightningSafetyMargin)
                {otherHazard=true;break;}
            if(otherHazard)continue;
            float score=Vector3.Distance(sample.position,resumeTarget)+Vector3.Distance(transform.position,sample.position)*.2f;
            if(score<best){best=score;bestTarget=sample.position;found=true;}
        }
        if(found){agent.speed=investigateSpeed;NavigateTo(bestTarget);}
        else{agent.ResetPath();hasDestination=false;}
        return true;
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position + Vector3.up, .35f);
        if (patrolPoints == null) return;
        for (int i = 0; i < patrolPoints.Length; i++)
            if (patrolPoints[i] != null && patrolPoints[(i+1)%patrolPoints.Length] != null)
                Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[(i+1)%patrolPoints.Length].position);
    }
}
