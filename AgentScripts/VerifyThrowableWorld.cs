using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using Sound2.LevelEditing;

public static class VerifyThrowableWorld
{
    static List<string> checks=new();
    static void Check(bool ok,string label)=>checks.Add((ok?"PASS: ":"FAIL: ")+label);
    static async Task Tick(int ms=100){int frame=Time.frameCount;await Task.Delay(ms);if(Time.frameCount<=frame)throw new Exception("Frames not advancing");}
    static PatrolMonster Monster(Transform parent,Vector3 position,RuntimePatrolNavigation navigation)
    {
        var go=new GameObject("Impact hearing fixture");go.SetActive(false);go.transform.SetParent(parent);go.transform.position=position;
        var m=go.AddComponent<PatrolMonster>();m.navigation=navigation;m.soundPriorityMemory=0;m.investigateSpeed=.1f;go.SetActive(true);return m;
    }
    public static async Task<object> Main()
    {
        if(SonicSpaceStartMenu.BlocksGameplay)throw new Exception("Enter gameplay first");
        checks.Clear();System.IO.Directory.CreateDirectory("Logs/ThrowableWorld");
        var setup=UnityEngine.Object.FindAnyObjectByType<BaiYuTestScene>();var player=setup.player.GetComponent<PlayerController>();
        var all=UnityEngine.Object.FindObjectsByType<PickupItem>();
        Check(all.Length>3 && all.All(p=>p.GetComponents<CollisionSonarEmitter>().Length==1 && p.GetComponents<SonarObjectEmitter>().Length==1),"Every live ball, cube and crystal has exactly one impact/sonar emitter");
        Check(all.All(p=>!p.GetComponent<SonarObjectEmitter>().automatic && p.GetComponent<SonarObjectEmitter>().toggleKey==UnityEngine.InputSystem.Key.None),"Throwable sonar is driven by collisions rather than automatic timers or a shared toggle key");
        Check(UnityEngine.Object.FindObjectsByType<CoinPickup>().Length==0 && AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Coin.prefab")==null,"Current floor contains no coins and the coin prefab is removed");
        foreach(var normal in new[]{Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
        {
            var outputs=new HashSet<Vector3>();bool valid=true;
            for(int angle=0;angle<360;angle+=7)
            {
                var tangent=Mathf.Abs(normal.y)>.5f?Vector3.forward:Vector3.up;
                var d=MonsterVines.EightWay(Quaternion.AngleAxis(angle,normal)*tangent,normal);
                Vector2 planar=Mathf.Abs(normal.y)>.5f?new Vector2(d.x,d.z):Mathf.Abs(normal.x)>.5f?new Vector2(d.y,d.z):new Vector2(d.x,d.y);
                valid&=Mathf.Abs(Vector3.Dot(d,normal))<.0001f && Mathf.Abs(d.magnitude-1)<.0001f
                    && (Mathf.Abs(planar.x)<.0001f || Mathf.Abs(planar.y)<.0001f || Mathf.Abs(Mathf.Abs(planar.x)-Mathf.Abs(planar.y))<.0001f);
                outputs.Add(new Vector3(Mathf.Round(d.x*1000),Mathf.Round(d.y*1000),Mathf.Round(d.z*1000)));
            }
            Check(valid && outputs.Count==8,"Exactly eight axis/diagonal growth directions on surface normal "+normal);
        }
        var root=new GameObject("Throwable verification");root.SetActive(false);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=new Vector3(505,-.5f,500);floor.transform.localScale=new Vector3(40,1,20);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(root.transform);wall.transform.position=new Vector3(500,1,503);wall.transform.localScale=new Vector3(3,2,.2f);
        var nav=root.AddComponent<RuntimePatrolNavigation>();nav.geometryRoot=root.transform;root.SetActive(true);Physics.SyncTransforms();nav.Rebuild();
        var near=Monster(root.transform,new Vector3(502,.05f,500),nav);var far=Monster(root.transform,new Vector3(518,.05f,500),nav);
        var originals=new[]{AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"),
            setup.ActiveContent.GetComponentsInChildren<PickupItem>().First(x=>x.name=="可搬运方块").gameObject,
            setup.map.FindFloor(setup.player.CurrentFloor).GetComponentInChildren<MapResource>().gameObject};
        GameplayNoiseEvent last=default;int events=0;Component watched=null;
        Action<GameplayNoiseEvent> observe=e=>{if(e.source==watched){last=e;events++;}};GameplayNoise.Emitted+=observe;
        try
        {
            await Tick(250);Check(nav.Ready && near.NavigationReady && far.NavigationReady,"Both hearing fixtures navigate on a real runtime NavMesh");
            for(int kind=0;kind<4;kind++)
            {
                GameObject itemObject;
                if(kind<3)itemObject=UnityEngine.Object.Instantiate(originals[kind]);
                else{itemObject=GameObject.CreatePrimitive(PrimitiveType.Capsule);itemObject.AddComponent<PickupItem>();}
                itemObject.transform.SetParent(root.transform);itemObject.transform.localScale=Vector3.one*.4f;
                var body=itemObject.GetComponent<Rigidbody>();body.isKinematic=false;body.useGravity=false;body.interpolation=RigidbodyInterpolation.None;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                var impact=itemObject.GetComponent<CollisionSonarEmitter>();var sonar=itemObject.GetComponent<SonarObjectEmitter>();
                Check(impact!=null && sonar!=null,"Type "+kind+": existing/new throwable receives impact dependencies");
                impact.minimumImpactSpeed=.1f;impact.rangePerSpeed=0;impact.hearingRange=6;impact.emissionInterval=.15f;
                sonar.range=4;sonar.playSound=false;sonar.automatic=false;watched=impact;
                for(int repeat=0;repeat<2;repeat++)
                {
                    body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.position=new Vector3(500,1,501.4f);itemObject.transform.position=body.position;
                    near.GetComponent<NavMeshAgent>().Warp(new Vector3(502,.05f,500));far.GetComponent<NavMeshAgent>().Warp(new Vector3(518,.05f,500));Physics.SyncTransforms();await Tick(180);
                    int pulses=sonar.Emissions,noises=events,heard=near.HeardSounds,farHeard=far.HeardSounds;
                    body.linearVelocity=Vector3.forward*8;
                    for(int step=0;step<30 && events==noises;step++)await Tick(20);
                    Check(sonar.Emissions>pulses && events>noises && impact.Emissions>0,"Type "+kind+" hit "+repeat+": real wall collision emits sonar and an impact event");
                    Check(near.HeardSounds>heard && near.LastHeardKind==GameplayNoiseKind.Impact && Vector3.Distance(near.LastHeardPosition,last.position)<.001f,"Type "+kind+" hit "+repeat+": nearby monster investigates the actual collision point");
                    Check(far.HeardSounds==farHeard,"Type "+kind+" hit "+repeat+": distant monster stays outside hearing range");
                }
                body.linearVelocity=Vector3.zero;body.position=new Vector3(500,1,500);itemObject.transform.position=body.position;body.useGravity=true;Physics.SyncTransforms();await Tick(180);
                int floorPulses=sonar.Emissions,floorEvents=events;body.linearVelocity=Vector3.down*3;
                for(int i=0;i<30 && events==floorEvents;i++)await Tick(20);
                Check(events>floorEvents && sonar.Emissions>floorPulses && Mathf.Abs(last.position.y)<.04f,"Type "+kind+": real floor impact emits sonar and a ground-contact hearing event");
                body.useGravity=false;
                // Hearing remains functional even if visual emission has been disabled.
                sonar.enabled=false;body.linearVelocity=Vector3.zero;body.position=new Vector3(500,1,501.4f);itemObject.transform.position=body.position;Physics.SyncTransforms();await Tick(180);
                int previous=events;body.linearVelocity=Vector3.forward*8;for(int i=0;i<30 && events==previous;i++)await Tick(20);
                Check(events>previous,"Type "+kind+": AI hearing does not depend on visual renderer availability");
                UnityEngine.Object.Destroy(itemObject);await Tick();
            }
        }
        finally{GameplayNoise.Emitted-=observe;UnityEngine.Object.Destroy(root);}
        int previousFloor=setup.player.CurrentFloor;setup.MoveToShaft();
        for(int i=0;i<80 && setup.player.CurrentFloor==previousFloor;i++)await Tick(50);
        Check(setup.player.CurrentFloor==previousFloor+1 && setup.PopulatedFloor==setup.player.CurrentFloor,"Normal shaft descent still populates the next floor");
        Check(UnityEngine.Object.FindObjectsByType<CoinPickup>().Length==0,"Newly generated floor never respawns coins");
        Check(UnityEngine.Object.FindObjectsByType<PickupItem>().All(p=>p.GetComponent<CollisionSonarEmitter>()!=null),"New-floor throwables also inherit impact feedback");
        var report=new{passed=checks.All(c=>c.StartsWith("PASS")),checks};System.IO.File.WriteAllText("Logs/ThrowableWorld/report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
