using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum LightningState { Calm, Active }

[Serializable]
public sealed class LightningPulseSettings
{
    [Min(.2f)] public float interval=6;
    [Min(1)] public float range=2;
    [Min(.05f)] public float afterglow=.25f;
    public SonarDistanceSettings distance=new SonarDistanceSettings {
        enabled=true,fadeStartDistance=.3f,fadeEndDistance=2,falloffPower=1,pulseStrength=.25f
    };
    public SonarDensitySettings density=new SonarDensitySettings {
        enabled=true,fadeStartDistance=.5f,fadeEndDistance=2,falloffPower=1
    };
}

/// <summary>Independent wandering hazard. Its visual stays at an exact world height; it never follows the player.</summary>
[DefaultExecutionOrder(50), DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent),typeof(SonarObjectEmitter))]
public sealed class BallLightning : MonoBehaviour
{
    public RuntimePatrolNavigation navigation;
    public Transform visual;
    public PlayerVitality player;
    [Min(.2f)] public float hoverHeight=1.35f;
    [Min(0)] public float wanderSpeed=.28f;
    [Min(1)] public float wanderRadius=6;
    public LightningState initialState=LightningState.Calm;
    public bool autoCycle=true;
    [Min(.2f)] public float calmSeconds=12;
    [Min(.2f)] public float activeSeconds=7;
    public LightningPulseSettings calm=new LightningPulseSettings();
    public LightningPulseSettings active=new LightningPulseSettings {
        interval=.7f,range=8,afterglow=.65f,
        distance=new SonarDistanceSettings {enabled=true,fadeStartDistance=1,fadeEndDistance=8,falloffPower=1,pulseStrength=.95f},
        density=new SonarDensitySettings {enabled=true,fadeStartDistance=4,fadeEndDistance=8,falloffPower=1}
    };
    [Header("Active damage")]
    [Min(0)] public float damage=20;
    [Min(.1f)] public float damageInterval=1;
    [Min(.1f)] public float damageRadius=2;
    [Header("Monster avoidance")]
    [Min(.1f)] public float calmAvoidanceRadius=1.4f;
    [Min(.1f)] public float activeAvoidanceRadius=3.2f;
    [Min(1)] public float monsterSenseRadius=7;
    public LightningState State { get; private set; }
    public int DamageTicks { get; private set; }
    public bool NavigationReady => agent!=null && agent.enabled && agent.isOnNavMesh;
    public Vector3 Center => visual!=null?visual.position:transform.position+Vector3.up*hoverHeight;
    public float AvoidanceRadius => State==LightningState.Active?activeAvoidanceRadius:calmAvoidanceRadius;
    static readonly List<BallLightning> instances=new List<BallLightning>();
    public static IReadOnlyList<BallLightning> Instances => instances;
    static readonly Vector4[] bodySpheres=new Vector4[8];
    static void PublishBodies()
    {
        int count=0;
        foreach(var hazard in instances)
        {
            if(count>=bodySpheres.Length)break;
            if(hazard==null || hazard.visual==null || !hazard.isActiveAndEnabled)continue;
            var scale=hazard.visual.lossyScale;
            var center=hazard.Center;
            bodySpheres[count++]=new Vector4(center.x,center.y,center.z,.5f*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
        }
        Shader.SetGlobalInt("_SonarBlueBodyCount",count);
        Shader.SetGlobalVectorArray("_SonarBlueBodies",bodySpheres);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInstances() {instances.Clear();Shader.SetGlobalInt("_SonarBlueBodyCount",0);}
    NavMeshAgent agent; NavMeshPath path; SonarObjectEmitter pulse;
    Vector3 home;
    float stateUntil,nextDestination,nextDamage;
    bool initialized;
    void Awake()
    {
        agent=GetComponent<NavMeshAgent>();agent.enabled=false;
        path=new NavMeshPath();pulse=GetComponent<SonarObjectEmitter>();home=transform.position;
    }
    void OnEnable() { if(!instances.Contains(this))instances.Add(this);if(NavigationReady)agent.isStopped=false; }
    void OnDisable() {instances.Remove(this);PublishBodies();if(NavigationReady)agent.isStopped=true;}
    void Start()
    {
        if(navigation==null)navigation=FindAnyObjectByType<RuntimePatrolNavigation>();
        if(player==null)player=FindAnyObjectByType<PlayerVitality>();
        pulse.pulseKind=SonarPulseKind.Lightning;pulse.emissionOrigin=visual;
        agent.radius=.4f;agent.height=.8f;agent.acceleration=1;agent.stoppingDistance=.15f;
        SetState(initialState);
    }
    public void SetState(LightningState state)
    {
        State=state;stateUntil=Time.time+Mathf.Max(.2f,state==LightningState.Calm?calmSeconds:activeSeconds);
        if(pulse==null)pulse=GetComponent<SonarObjectEmitter>();
        var profile=state==LightningState.Calm?calm:active;
        pulse.interval=profile.interval;pulse.range=profile.range;pulse.afterglow=profile.afterglow;
        pulse.distanceSettings=profile.distance;pulse.densitySettings=profile.density;
        pulse.RestartSchedule();nextDamage=Time.time;
    }
    [ContextMenu("Set calm (Play mode)")] void Calm() {if(Application.isPlaying)SetState(LightningState.Calm);}
    [ContextMenu("Set active (Play mode)")] void Active() {if(Application.isPlaying)SetState(LightningState.Active);}
    void Update()
    {
        if(autoCycle && Time.time>=stateUntil)SetState(State==LightningState.Calm?LightningState.Active:LightningState.Calm);
        Move();
        if(State==LightningState.Active)ApplyDamage();
    }
    void Move()
    {
        if(navigation==null || !navigation.Ready)return;
        if(!initialized)
        {
            if(!NavMesh.SamplePosition(transform.position,out var start,2,agent.areaMask))return;
            agent.enabled=true;if(!agent.Warp(start.position))return;
            initialized=true;
        }
        if(!NavigationReady)return;
        agent.speed=Mathf.Max(0,wanderSpeed);
        if(agent.hasPath && (agent.pathPending || agent.remainingDistance>agent.stoppingDistance+.1f))return;
        if(Time.time<nextDestination)return;
        nextDestination=Time.time+1;
        if(path==null)path=new NavMeshPath();
        for(int attempt=0;attempt<10;attempt++)
        {
            Vector2 offset=UnityEngine.Random.insideUnitCircle*wanderRadius;
            if(!NavMesh.SamplePosition(home+new Vector3(offset.x,0,offset.y),out var target,1,agent.areaMask))continue;
            if(Vector3.Distance(target.position,transform.position)<1)continue;
            if(agent.CalculatePath(target.position,path) && path.status==NavMeshPathStatus.PathComplete)
            {agent.SetPath(path);break;}
        }
    }
    void LateUpdate()
    {
        if(visual==null)return;
        var position=visual.position;position.y=hoverHeight;visual.position=position;
        PublishBodies();
    }
    void ApplyDamage()
    {
        if(Time.time<nextDamage || player==null || !player.isActiveAndEnabled || player.IsDead
            || !player.TryGetComponent<CharacterController>(out var capsule) || !capsule.enabled)return;
        Vector3 nearest=capsule.ClosestPoint(Center);
        if(Vector3.Distance(Center,nearest)>damageRadius)return;
        Vector3 delta=capsule.bounds.center-Center;
        foreach(var hit in Physics.RaycastAll(Center,delta.normalized,delta.magnitude,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(player.transform))return;
        nextDamage=Time.time+Mathf.Max(.1f,damageInterval);
        if(player.TakeDamage(damage))DamageTicks++;
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.color=State==LightningState.Active?Color.cyan:Color.blue;
        Gizmos.DrawWireSphere(Center,damageRadius);
    }
}
