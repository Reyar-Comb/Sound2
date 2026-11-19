using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// CLI Play-mode regression: compare both query paths and their actual GPU blocking-plane slices.
public static class VerifyBatchedSonar
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo Hits = typeof(SonarPulseField).GetField("rayHits", Private);
    static readonly FieldInfo Commands = typeof(SonarPulseField).GetField("rayCommands", Private);
    static readonly FieldInfo Texture = typeof(SonarPulseField).GetField("blockers", Private);
    static int rayIdentityChecks;
    static readonly List<string> checks = new List<string>();
    static void Check(bool pass, string label) { if (!pass) throw new Exception(label); checks.Add(label); }
    static NativeArray<RaycastHit> Buffer(SonarPulseField field) => (NativeArray<RaycastHit>)Hits.GetValue(field);
    static int Offset(SonarPulseKind kind) {
        switch(kind) {
            case SonarPulseKind.Footstep:return SonarPulseField.FootstepPulseOffset;
            case SonarPulseKind.Danger:return SonarPulseField.DangerPulseOffset;
            case SonarPulseKind.Coin:return SonarPulseField.CoinPulseOffset;
            case SonarPulseKind.Lightning:return SonarPulseField.LightningPulseOffset;
            default:return 0;
        }
    }
    static Color[] Planes(SonarPulseField field, SonarPulseKind kind)
    {
        var request = AsyncGPUReadback.Request((Texture2DArray)Texture.GetValue(field), 0, 0, 48, 0, 48, Offset(kind)*6, 6);
        request.WaitForCompletion();
        Check(!request.hasError, "GPU readback succeeds for " + kind);
        var result=new Color[6*48*48];
        for(int face=0;face<6;face++) { var layer=request.GetData<Color>(face); for(int cell=0;cell<48*48;cell++)result[face*48*48+cell]=layer[cell]; }
        return result;
    }
    static int Compare(SonarPulseField field, Vector3 origin, string label,
        SonarPulseKind kind=SonarPulseKind.Main, SonarDistanceSettings distance=null, SonarDensitySettings density=null)
    {
        field.batchRaycasts=false;field.ClearPulses();
        Check(field.EmitFrom(origin,10,7,.5f,kind,distance,density),label+": legacy emits");
        int count=field.LastRayCount,hits=field.LastHitCount;
        var old=Buffer(field).GetSubArray(0,count).ToArray();var oldPlanes=Planes(field,kind);
        field.batchRaycasts=true;field.ClearPulses();
        int before=field.Emissions;
        Check(field.EmitFrom(origin,10,7,.5f,kind,distance,density),label+": batch emits");
        Check(field.Emissions==before+1 && field.LastRayCount==count && field.LastHitCount==hits,label+": count and hit count unchanged");
        var current=Buffer(field);float maxDistance=0,maxNormal=0;
        for(int i=0;i<count;i++) {
            if(old[i].collider!=current[i].collider)throw new Exception(label+": hit identity "+i);
            if(old[i].collider==null)continue;
            maxDistance=Mathf.Max(maxDistance,Mathf.Abs(old[i].distance-current[i].distance));
            maxNormal=Mathf.Max(maxNormal,(old[i].normal-current[i].normal).magnitude);
        }
        rayIdentityChecks+=count;
        Check(maxDistance<.001f && maxNormal<.005f,label+": first-hit distance/normal agree");
        var newPlanes=Planes(field,kind);float maxPlane=0;
        for(int i=0;i<oldPlanes.Length;i++) {
            var a=oldPlanes[i];var b=newPlanes[i];
            maxPlane=Mathf.Max(maxPlane,Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b),Mathf.Abs(a.a-b.a));
        }
        Check(maxPlane<.005f,label+": all six uploaded GPU faces agree");
        Check(Shader.GetGlobalVectorArray("_SonarPulseParameters80")[Offset(kind)].w==1,label+": correct bank published synchronously");
        return hits;
    }
    public static async Task<string> Main()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        checks.Clear();rayIdentityChecks=0;
        var roots=SceneManager.GetActiveScene().GetRootGameObjects();var active=roots.Select(r=>r.activeSelf).ToArray();
        bool backfaces=Physics.queriesHitBackfaces,triggers=Physics.queriesHitTriggers;
        GameObject fixture=null;Mesh mesh=null;long allocated=-1;bool passed=false;
        try {
            foreach(var root in roots)root.SetActive(false);
            fixture=new GameObject("Batched sonar regression");
            var origin=new Vector3(4000,100,4000);
            GameObject Box(string name,Vector3 position,Vector3 size,int layer=28,bool trigger=false) {
                var go=new GameObject(name);go.transform.SetParent(fixture.transform);go.transform.position=origin+position;
                go.layer=layer;go.AddComponent<BoxCollider>().size=size;go.GetComponent<BoxCollider>().isTrigger=trigger;return go;
            }
            Box("front",Vector3.forward*4,new Vector3(12,12,.2f));
            Box("back",Vector3.back*4,new Vector3(12,12,.2f));
            Box("left",Vector3.left*4,new Vector3(.2f,12,12));
            Box("right",Vector3.right*4,new Vector3(.2f,12,12));
            Box("ceiling",Vector3.up*4,new Vector3(12,.2f,12));
            Box("floor",Vector3.down*4,new Vector3(12,.2f,12));
            Box("ignored trigger",Vector3.forward,new Vector3(12,12,.2f),28,true);
            Box("ignored layer",Vector3.forward*1.5f,new Vector3(12,12,.2f),29);
            Box("left doorway",new Vector3(-1.2f,0,2),new Vector3(2,7,.1f));
            Box("right doorway",new Vector3(1.2f,0,2),new Vector3(2,7,.1f));
            var gate=Box("moving thin gate",new Vector3(0,0,2),new Vector3(.5f,7,.02f));
            var slanted=Box("slanted wall",new Vector3(-2,0,1),new Vector3(.1f,3,2));slanted.transform.rotation=Quaternion.Euler(5,25,12);
            var sphere=new GameObject("curved surface");sphere.transform.SetParent(fixture.transform);sphere.layer=28;
            sphere.transform.position=origin+new Vector3(1.5f,1,2.5f);sphere.AddComponent<SphereCollider>().radius=.45f;
            var goField=new GameObject("Field");goField.transform.SetParent(fixture.transform);
            var field=goField.AddComponent<SonarPulseField>();field.obstacleLayers=1<<28;
            Check(field.batchRaycasts,"Batch mode is the default on a new field");
            foreach(SonarPulseKind kind in Enum.GetValues(typeof(SonarPulseKind))) {
                Compare(field,origin,"Bank "+kind,kind);
                Check(field.LastRayCount==(kind==SonarPulseKind.Main?13824:3456),kind+": original angular resolution retained");
            }
            const int forward=4*48*48+24*48+24;
            Compare(field,origin,"Closed gate");Check(Buffer(field)[forward].collider==gate.GetComponent<Collider>(),"Thin gate is the nearest blocker");
            gate.transform.position+=Vector3.right*15;
            Compare(field,origin,"Moved gate without caller SyncTransforms");
            Check(Buffer(field)[forward].collider.name=="front","Moving a blocker is reflected immediately");
            foreach(bool globalTriggers in new[]{true,false}) {
                Physics.queriesHitTriggers=globalTriggers;Compare(field,origin,"Trigger global "+globalTriggers);
                Check(Buffer(field)[forward].collider.name=="front","Triggers and excluded layer never block the wave");
            }
            field.obstacleLayers=1<<29;Compare(field,origin,"Changed obstacle layer");
            Check(Buffer(field)[forward].collider.name=="ignored layer","Current LayerMask is honored");
            field.obstacleLayers=0;Check(Compare(field,origin,"Misses after previous hits")==0,"Reused hit buffers and GPU slices clear old blockers");
            field.obstacleLayers=1<<28;
            Check(Compare(field,origin,"Brightness range cap",SonarPulseKind.Main,new SonarDistanceSettings{enabled=true,fadeEndDistance=.5f})==0,"Brightness falloff limits ray distance");
            Check(Compare(field,origin,"Density range cap",SonarPulseKind.Coin,null,new SonarDensitySettings{enabled=true,fadeEndDistance=.5f})==0,"Density falloff limits ray distance");
            var inside=Box("Origin inside collider",Vector3.zero,Vector3.one);
            Compare(field,origin,"Origin inside collider");inside.SetActive(false);
            mesh=new Mesh();mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
            mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
            var sheet=new GameObject("Back-facing mesh");sheet.transform.SetParent(fixture.transform);sheet.transform.position=origin+Vector3.forward*2;sheet.layer=28;
            sheet.AddComponent<MeshCollider>().sharedMesh=mesh;
            Physics.queriesHitBackfaces=false;Compare(field,origin,"Mesh backfaces ignored");
            Check(Buffer(field)[forward].collider.name=="front","Back-facing mesh skipped when global setting is off");
            Physics.queriesHitBackfaces=true;Compare(field,origin,"Mesh backfaces included");
            Check(Buffer(field)[forward].collider.name=="Back-facing mesh","Back-facing mesh hit when global setting is on");
            for(int cycle=0;cycle<3;cycle++) {
                field.enabled=false;
                Check(!Buffer(field).IsCreated && !((NativeArray<RaycastCommand>)Commands.GetValue(field)).IsCreated,"Native buffers disposed on disable "+cycle);
                int emissions=field.Emissions;Check(!field.EmitFrom(origin,10,7,.5f)&&field.Emissions==emissions,"Disabled field rejects emission");
                field.enabled=true;Compare(field,origin,"Re-enabled field "+cycle);
            }
            var projectile=new GameObject("Collision and manual trigger integration");projectile.transform.SetParent(fixture.transform);
            projectile.transform.position=origin;projectile.layer=28;
            projectile.AddComponent<SphereCollider>().radius=.15f;
            var body=projectile.AddComponent<Rigidbody>();body.useGravity=false;
            var collision=projectile.AddComponent<CollisionSonarEmitter>();
            var emitter=projectile.GetComponent<SonarObjectEmitter>();
            emitter.automatic=false;emitter.toggleKey=UnityEngine.InputSystem.Key.None;emitter.playSound=false;emitter.pulseField=field;
            int previous=field.Emissions;emitter.EmitNow();
            Check(field.Emissions==previous+1 && emitter.Emissions==1,"Manual object trigger still publishes immediately");
            body.linearVelocity=Vector3.forward*10;
            float deadline=Time.realtimeSinceStartup+2;
            while(collision.Emissions==0 && Time.realtimeSinceStartup<deadline)await Task.Delay(20);
            Check(collision.Emissions>0 && emitter.Emissions>1,"Actual rigidbody collision still emits through the shared field");
            Check(Vector3.Distance(field.LastOrigin,emitter.LastOrigin)<.001f && field.LastRayCount==13824,"Collision keeps its world origin and full-resolution main bank");
            body.isKinematic=true;
            field.EmitFrom(origin,10,7,.5f);field.EmitFrom(origin,10,7,.5f);
            long start=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<10;i++)field.EmitFrom(origin,10,7,.5f);
            allocated=GC.GetAllocatedBytesForCurrentThread()-start;
            Check(allocated==0,"Ten warmed batched emissions allocate zero managed bytes");
            Object.Destroy(goField);await Task.Delay(80);
            Check(field==null,"Emitted field can be destroyed without an outstanding query job");
            passed=true;
            return JsonConvert.SerializeObject(new{passed,checks=checks.ToArray(),rayIdentityChecks,managedBytesForTenEmissions=allocated});
        }
        finally {
            Physics.queriesHitBackfaces=backfaces;Physics.queriesHitTriggers=triggers;
            if(fixture!=null){fixture.SetActive(false);Object.Destroy(fixture);}
            if(mesh!=null)Object.Destroy(mesh);
            for(int i=0;i<roots.Length;i++)if(roots[i]!=null)roots[i].SetActive(active[i]);
            System.IO.Directory.CreateDirectory("Logs/AlarmProfiler");
            System.IO.File.WriteAllText("Logs/AlarmProfiler/batch-regression.json",JsonConvert.SerializeObject(new{passed,checks=checks.ToArray(),rayIdentityChecks,managedBytesForTenEmissions=allocated},Formatting.Indented));
        }
    }
}

