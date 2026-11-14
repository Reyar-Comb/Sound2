using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Real runtime navigation, production noise hooks and physical collision integration.
public static class VerifyMonster
{
    static async Task Tick(int ms=150) { int frame=Time.frameCount; await Task.Delay(ms); if(Time.frameCount<=frame) throw new Exception("Player loop stopped"); }
    public static async Task<object> Main()
    {
        var monster=UnityEngine.Object.FindAnyObjectByType<PatrolMonster>();
        var scan=monster.GetComponent<MonsterScanEmitter>();
        var agent=monster.GetComponent<NavMeshAgent>();
        var nav=monster.navigation;
        var player=UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        var foot=player.GetComponent<FootstepEmitter>();
        var start=monster.transform.position;
        bool scanEnabled=scan.enabled, suspended=player.InputSuspended;
        float priority=monster.soundPriorityMemory, search=monster.searchSeconds, speed=monster.investigateSpeed;
        var checks=new List<string>(); var failures=new List<string>();
        Action<bool,string> check=(pass,label)=>{checks.Add((pass?"PASS: ":"FAIL: ")+label);if(!pass)failures.Add(label);};
        int footEvents=0,impactEvents=0;
        GameplayNoiseEvent lastImpact=default;
        Action<GameplayNoiseEvent> observer=e=>{if(e.kind==GameplayNoiseKind.Footstep)footEvents++;else{impactEvents++;lastImpact=e;}};
        GameObject wall=null,ball=null, hearingWall=null, marker=null;
        var scanPlayer=scan.player;
        float nearInterval=scan.nearInterval, farInterval=scan.farInterval, firstDelay=scan.firstDelay;
        bool scanSound=scan.playSound;
        try {
            GameplayNoise.Emitted+=observer;
            player.SetInputCaptured(false); scan.enabled=false;
            check(nav.Ready && nav.SourceCount>0 && monster.NavigationReady,"Runtime collider navigation is built and agent is on the mesh");
            check(monster.GetComponentsInChildren<Renderer>().Length==0,"Monster has no mesh or depth silhouette to reveal");
            var before=monster.transform.position; await Tick(1800);
            check(Vector3.Distance(before,monster.transform.position)>.2f,"Monster actually moves along its patrol");
            var path=new NavMeshPath();
            bool route=NavMesh.CalculatePath(new Vector3(9,.05f,0),new Vector3(0,.05f,-9),NavMesh.AllAreas,path);
            check(route && path.status==NavMeshPathStatus.PathComplete && path.corners.Length>=3,"East-to-south patrol path bends around the hall walls");
            check(scan.IntervalAtDistance(2)<scan.IntervalAtDistance(8) && scan.IntervalAtDistance(8)<scan.IntervalAtDistance(16),"Scan interval strictly decreases as the monster approaches the player");
            check(Mathf.Approximately(scan.IntervalAtDistance(0),scan.nearInterval) && Mathf.Approximately(scan.IntervalAtDistance(100),scan.farInterval),"Scan interval is clamped at near and far endpoints");
            monster.soundPriorityMemory=0; monster.searchSeconds=.4f; monster.investigateSpeed=5;
            agent.Warp(new Vector3(-2,.05f,0)); agent.ResetPath();
            int heard=monster.HeardSounds;
            GameplayNoise.Emit(monster.transform.position+Vector3.right*50,6,GameplayNoiseKind.Footstep,foot);
            check(monster.HeardSounds==heard,"Out-of-range footsteps are ignored");
            int eventsBefore=footEvents;
            await Tick(300); check(footEvents==eventsBefore,"Stationary player produces no footstep hearing events");
            // Invoke the actual step hook to verify hearing uses its capsule-base origin, not camera/player tracking.
            typeof(FootstepEmitter).GetMethod("EmitStep",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(foot,null);
            check(footEvents==eventsBefore+1 && monster.HeardSounds==heard+1 && monster.LastHeardKind==GameplayNoiseKind.Footstep,
                "Production footstep hook alerts a nearby monster");
            check(monster.State==MonsterBehaviourState.Investigate && Vector3.Distance(monster.LastHeardPosition,foot.LastOrigin)<.001f,
                "Investigation records the fixed footstep position");
            bool searched=false;
            for(int i=0;i<35;i++){await Tick(80);if(monster.State==MonsterBehaviourState.Search){searched=true;break;}}
            check(searched,"Monster reaches the sound location and searches");
            await Tick(600); check(monster.State==MonsterBehaviourState.Patrol,"Monster resumes patrol after a quiet search");
            // A real ball collision produces an impact stimulus and redirects the monster to the wall.
            agent.Warp(new Vector3(-1,.05f,-1)); agent.ResetPath();
            ball=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));
            var body=ball.GetComponent<Rigidbody>();body.useGravity=false;body.interpolation=RigidbodyInterpolation.None;
            body.position=new Vector3(-3,1,-.5f); ball.transform.position=body.position;
            var emitter=ball.GetComponent<SonarObjectEmitter>();emitter.automatic=false;emitter.playSound=false;
            var collision=ball.GetComponent<CollisionSonarEmitter>();collision.minimumImpactSpeed=.5f;collision.hearingRange=20;
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(-3,1,1);
            wall.transform.localScale=new Vector3(2,2,.2f);Physics.SyncTransforms();
            await Tick(200); heard=monster.HeardSounds;
            body.linearVelocity=Vector3.forward*8;
            for(int i=0;i<30 && collision.Emissions==0;i++) await Tick(30);
            check(collision.Emissions>0 && impactEvents>0,"Physical ball-wall contact emits a gameplay sound event");
            check(monster.HeardSounds>heard && monster.LastHeardKind==GameplayNoiseKind.Impact
                && Vector3.Distance(monster.LastHeardPosition,lastImpact.position)<.001f,
                "Monster diverts to the actual impact location rather than the thrower's position");
            check(lastImpact.range>foot.hearingRange,"Impact lure is audible farther away than footsteps");
            agent.Warp(new Vector3(-2,.05f,0)); agent.ResetPath();
            hearingWall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            hearingWall.transform.position=new Vector3(0,1,0); hearingWall.transform.localScale=new Vector3(.2f,3,4);
            Physics.SyncTransforms(); heard=monster.HeardSounds;
            var behindWall=new Vector3(2,.12f,0);
            GameplayNoise.Emit(behindWall,6,GameplayNoiseKind.Footstep,foot);
            check(monster.HeardSounds==heard,"Wall reduces the audible range of a quiet footstep");
            GameplayNoise.Emit(behindWall,20,GameplayNoiseKind.Impact,foot);
            check(monster.HeardSounds==heard+1,"A louder impact remains audible through that wall");
            UnityEngine.Object.Destroy(hearingWall);
            marker=new GameObject("Scan frequency fixture");marker.transform.SetParent(monster.transform,false);
            scan.player=marker.transform;scan.nearInterval=.2f;scan.farInterval=.6f;scan.firstDelay=0;scan.playSound=false;
            scan.enabled=true; int baseline=scan.Scans;await Tick(750);int nearScans=scan.Scans-baseline;
            scan.enabled=false;marker.transform.localPosition=Vector3.right*100;
            scan.enabled=true;baseline=scan.Scans;await Tick(750);int farScans=scan.Scans-baseline;
            check(nearScans>farScans && farScans>=1,"Real timed emissions are more frequent nearby (near="+nearScans+", far="+farScans+")");
        } catch(Exception e){failures.Add(e.ToString());}
        finally {
            GameplayNoise.Emitted-=observer;
            if(ball!=null)UnityEngine.Object.Destroy(ball);if(wall!=null)UnityEngine.Object.Destroy(wall);
            monster.soundPriorityMemory=priority;monster.searchSeconds=search;monster.investigateSpeed=speed;
            if(agent.enabled && agent.isOnNavMesh){agent.ResetPath();agent.Warp(start);}
            scan.enabled=false;scan.player=scanPlayer;scan.nearInterval=nearInterval;scan.farInterval=farInterval;
            scan.firstDelay=firstDelay;scan.playSound=scanSound;scan.enabled=scanEnabled;
            if(marker!=null)UnityEngine.Object.Destroy(marker);if(hearingWall!=null)UnityEngine.Object.Destroy(hearingWall);
            player.SetInputCaptured(!suspended);
        }
        return new {passed=failures.Count==0,checks,failures};
    }
}
