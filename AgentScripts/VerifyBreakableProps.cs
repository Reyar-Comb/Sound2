using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class VerifyBreakableProps
{
    static readonly List<string> checks=new();
    static void Check(bool value,string label){checks.Add((value?"PASS: ":"FAIL: ")+label);if(!value)throw new Exception(label);}
    static void Report(string mode){Directory.CreateDirectory("Logs/BreakableProps");File.WriteAllLines("Logs/BreakableProps/"+mode+".txt",checks);}
    static GameObject Prefab(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+name+".prefab");

    public static object Edit()
    {
        checks.Clear();
        foreach(string name in new[]{"PickupAlarmClock","PickupGlassMug"})
        {
            var prefab=Prefab(name);var item=prefab.GetComponent<PickupItem>();
            Check(item!=null&&item.breakable&&item.brokenPrefab!=null,name+": complete pickup and breakage configuration");
            var remains=item.brokenPrefab;
            Check(remains.GetComponentsInChildren<PickupItem>(true).Length==0&&remains.GetComponentsInChildren<InteractableObject>(true).Length==0,name+": remnants cannot be picked up");
            Check(remains.GetComponentsInChildren<SonarObjectEmitter>(true).Length==0&&remains.GetComponentsInChildren<TickingClock>(true).Length==0,name+": fragments have no repeating sound emitters");
            Check(remains.GetComponentsInChildren<Rigidbody>().All(b=>!b.isKinematic&&b.GetComponentsInChildren<Collider>().Length>0),name+": fragments have dynamic bodies and colliders");
            Check(prefab.GetComponent<SonarCueSource>().clip==null&&!prefab.GetComponent<SonarCueSource>().generateDefaultPing&&item.breakClip==null,name+": audio slots empty, no synthesis");
        }
        Check(Prefab("PickupAlarmClock").GetComponent<PickupAlarm>()!=null,"Alarm countdown is a separate configurable component");
        Check(Prefab("PickupGlassMug").GetComponent<PickupItem>().brokenPrefab.transform.childCount==49,"Stemmed glass has matching bowl, stem and foot fragments");
        Check(!Prefab("PickupBouncyBall").GetComponent<PickupItem>().breakable,"Existing bouncy ball is not breakable by default");
        var actual=SceneManager.GetSceneByPath("Assets/Scenes/test.unity");
        var layout=actual.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>()).Single();
        Check(layout.transform.Find("Props/PickupAlarmClock")!=null&&layout.transform.Find("Props/PickupGlassMug")!=null,"Both new props are saved under test layout Props");
        CapturePreview();Report("edit");return new {passed=true,checks};
    }

    static void CapturePreview()
    {
        var original=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var center=new Vector3(6000,0,6000);
            var alarm=Object.Instantiate(Prefab("PickupAlarmClock"),center+new Vector3(-.5f,.411f,0),Quaternion.Euler(0,180,0));
            var mug=Object.Instantiate(Prefab("PickupGlassMug"),center+new Vector3(.45f,0,0),Quaternion.Euler(0,0,0));
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=center+Vector3.down*.05f;floor.transform.localScale=new Vector3(8,.1f,8);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Floor.mat");
            var light=new GameObject("Preview light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
            var camera=new GameObject("Preview camera",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;
            camera.transform.position=center+new Vector3(1.5f,1.4f,-2.5f);camera.transform.LookAt(center+Vector3.up*.35f);
            camera.fieldOfView=38;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.075f,.09f);
            Capture(camera,"models.png");
        }
        finally {SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);}
    }

    static int Capture(Camera camera,string name)
    {
        var rt=new RenderTexture(1100,800,24);var image=new Texture2D(1100,800,TextureFormat.RGB24,false);
        var old=RenderTexture.active;var oldTarget=camera.targetTexture;
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1100,800),0,0);image.Apply();
            Directory.CreateDirectory("Logs/BreakableProps");File.WriteAllBytes("Logs/BreakableProps/"+name,image.EncodeToPNG());
            return image.GetPixels32().Count(p=>p.r>30||p.g>30||p.b>30);
        }
        finally{camera.targetTexture=oldTarget;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(image);Object.DestroyImmediate(rt);}
    }

    static async Task Frames(int milliseconds)
    {
        int frame=Time.frameCount;await Task.Delay(milliseconds);
        if(Time.frameCount<=frame)throw new Exception("Player loop did not advance");
    }

    static async Task Until(Func<bool> condition,string label,int milliseconds=2500)
    {
        var start=DateTime.UtcNow;
        while(!condition()&&(DateTime.UtcNow-start).TotalMilliseconds<milliseconds)await Frames(40);
        Check(condition(),label);
    }

    public static async Task<object> Play()
    {
        checks.Clear();Check(Application.isPlaying,"Play mode active");
        var scene=SceneManager.GetActiveScene();var originals=scene.GetRootGameObjects();var active=originals.Select(r=>r.activeSelf).ToArray();
        GameObject fixture=null;AudioClip recording=null;
        try
        {
            foreach(var r in originals)r.SetActive(false);
            fixture=new GameObject("Breakable verification");var center=new Vector3(4000,0,4000);
            GameObject Cube(string name,Vector3 position,Vector3 scale)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(fixture.transform);go.transform.position=center+position;go.transform.localScale=scale;
                go.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");return go;
            }
            Cube("Floor",new Vector3(0,-.5f,0),new Vector3(20,1,20));Cube("Impact wall",new Vector3(0,2,4),new Vector3(15,4,.2f));
            var player=Object.Instantiate(Prefab("Player"),center+Vector3.up*1.1f,Quaternion.identity,fixture.transform);
            player.GetComponent<PlayerController>().InputSuspended=true;
            var pickup=player.GetComponent<PlayerPickup>();var field=player.GetComponentInChildren<SonarPulseField>();
            player.GetComponentInChildren<SphericalSonar>().emissionSound=false;
            PickupItem Spawn(string name,Vector3 position)
            {
                var go=Object.Instantiate(Prefab(name),center+position,Quaternion.identity,fixture.transform);
                var item=go.GetComponent<PickupItem>();go.GetComponent<Rigidbody>().useGravity=false;
                return item;
            }
            await Frames(100);
            var alarmItem=Spawn("PickupAlarmClock",new Vector3(0,1.25f,1));
            var alarm=alarmItem.GetComponent<PickupAlarm>();var tick=alarmItem.GetComponent<TickingClock>();
            tick.tickInterval=.12f;alarm.delayAfterPickup=.5f;alarm.ringInterval=.1f;
            recording=AudioClip.Create("Temporary silent alarm recording",22050,1,22050,false);alarm.alarmClip=recording;
            await Frames(280);
            Check(!alarm.Armed&&tick.Ticks>0,"Unpicked alarm ticks normally without starting its countdown");
            Check(pickup.TryPickUp(alarmItem),"Existing player pickup can hold the alarm");
            Check(alarm.Armed&&alarm.Remaining>0,"Successful pickup arms the timer");
            await Until(()=>alarm.IsRinging,"Alarm starts after delay while held");
            int ticks=tick.Ticks;
            await Frames(300);
            Check(tick.Ticks==ticks&&alarm.Rings>=3,"Rapid rings replace mechanical ticks");
            Check(alarmItem.GetComponent<AudioSource>().isPlaying,"Alarm recording reaches existing audio output");
            var output=alarmItem.GetComponent<AudioSource>();var source=alarmItem.GetComponent<SonarObjectEmitter>();
            var tickCounter=tick;var alarmCounter=alarm;
            int emissionsAtBreak=-1;alarmItem.Broken+=()=>emissionsAtBreak=field.Emissions;
            Check(pickup.TryThrow(),"Ringing alarm can be thrown with the existing player control");
            await Until(()=>alarmItem==null||alarmItem.IsBroken,"Real high-speed wall collision breaks alarm");
            var alarmRemains=fixture.transform.Find("AlarmClockBroken(Clone)");
            Check(alarmRemains!=null&&alarmRemains.GetComponentsInChildren<Rigidbody>().Length>=10,"Alarm leaves its model components as physical parts");
            Check(alarmRemains.GetComponentsInChildren<PickupItem>().Length==0&&pickup.HeldItem==null,"Alarm parts are not pickups and player hand is clear");
            Check(emissionsAtBreak>=0&&field.Emissions==emissionsAtBreak+1,"Break emits one pulse without a duplicate ordinary collision pulse");
            int emitted=field.Emissions;
            await Frames(350);
            Check(field.Emissions==emitted&&(output==null||!output.isPlaying),"Broken alarm produces no further rings or tick audio");
            Check(alarmRemains.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterial.name=="PureBlack"),"Alarm remnants use gameplay black material");
            Object.Destroy(alarmRemains.gameObject);await Frames(50);

            var dropped=Spawn("PickupAlarmClock",new Vector3(0,1.2f,1));
            var droppedAlarm=dropped.GetComponent<PickupAlarm>();droppedAlarm.delayAfterPickup=.6f;droppedAlarm.ringInterval=.1f;
            dropped.GetComponent<TickingClock>().tickInterval=.12f;
            Check(pickup.TryPickUp(dropped),"Second alarm can be picked up");await Frames(100);
            Check(pickup.TryPlace(),"Alarm can be gently placed before its delay expires");
            Check(!dropped.IsHeld&&!dropped.IsBroken,"Gentle placement does not break the alarm");
            await Until(()=>droppedAlarm.IsRinging,"Countdown continues after release and rings off-hand");
            Check(pickup.TryPickUp(dropped),"Ringing alarm can be picked up again");
            int rings=droppedAlarm.Rings;await Frames(180);
            Check(droppedAlarm.IsRinging&&droppedAlarm.Rings>rings&&droppedAlarm.Remaining==0,"Picking up again does not reset the alarm timer");
            Check(pickup.TryPlace(),"Ringing alarm can also be put down");
            Object.Destroy(dropped.gameObject);await Frames(50);

            var early=Spawn("PickupAlarmClock",new Vector3(0,1.2f,1));early.GetComponent<PickupAlarm>().delayAfterPickup=1;
            await Frames(80);
            Check(pickup.TryPickUp(early)&&pickup.TryThrow(),"Armed alarm can be thrown before countdown expires");
            await Until(()=>early==null||early.IsBroken,"Alarm can break before it begins ringing");
            emitted=field.Emissions;await Frames(1100);
            Check(field.Emissions==emitted,"Destroyed alarm does not ring when its former deadline passes");
            var earlyRemains=fixture.transform.Find("AlarmClockBroken(Clone)");if(earlyRemains!=null)Object.Destroy(earlyRemains.gameObject);await Frames(50);

            var cup=Spawn("PickupGlassMug",new Vector3(0,1.2f,1));var cupBody=cup.GetComponent<Rigidbody>();cupBody.useGravity=true;
            Check(pickup.TryPickUp(cup)&&pickup.TryPlace(),"Glass mug supports existing pickup and gentle placement controls");
            await Frames(450);Check(cup!=null&&!cup.IsBroken,"Actual gravity settling after right-click placement does not shatter mug");
            cupBody.useGravity=false;cupBody.position=center+new Vector3(2,.12f,1);cupBody.rotation=Quaternion.identity;cupBody.angularVelocity=Vector3.zero;cupBody.linearVelocity=Vector3.down*2;Physics.SyncTransforms();
            await Frames(230);
            Check(cup!=null&&!cup.IsBroken&&cup.GetComponent<CollisionSonarEmitter>().Emissions>0,"Real gentle cup landing emits impact feedback without shattering");
            cupBody.position=center+new Vector3(0,1,3.69f);cupBody.rotation=Quaternion.identity;cupBody.linearVelocity=new Vector3(8,0,.5f);cupBody.angularVelocity=Vector3.zero;Physics.SyncTransforms();
            await Frames(180);
            Check(cup!=null&&!cup.IsBroken,"Fast tangential wall contact below normal-speed threshold does not shatter cup");
            cup.breakLayers=0;cupBody.position=center+new Vector3(2,1,3);cupBody.rotation=Quaternion.identity;cupBody.linearVelocity=Vector3.forward*10;cupBody.angularVelocity=Vector3.zero;Physics.SyncTransforms();
            await Frames(180);
            Check(cup!=null&&!cup.IsBroken,"Break layer mask excludes otherwise destructive collision");
            cup.breakLayers=~0;cupBody.position=center+new Vector3(2,1,3);cupBody.rotation=Quaternion.identity;cupBody.linearVelocity=Vector3.forward*10;cupBody.angularVelocity=Vector3.zero;Physics.SyncTransforms();
            await Until(()=>cup==null||cup.IsBroken,"Real high-speed cup impact shatters it");
            var cupRemains=fixture.transform.Find("WineGlassBroken(Clone)");
            Check(cupRemains!=null&&cupRemains.childCount==49,"Glass leaves all 49 authored fragments");
            Check(cupRemains.GetComponentsInChildren<PickupItem>().Length==0&&cupRemains.GetComponentsInChildren<InteractableObject>().Length==0,"Cup fragments have no pickup or interaction components");
            var bodies=cupRemains.GetComponentsInChildren<Rigidbody>();var positions=bodies.Select(b=>b.position).ToArray();
            emitted=field.Emissions;await Frames(250);
            Check(bodies.Where((b,i)=>Vector3.Distance(b.position,positions[i])>.02f).Any(),"Fragments move under physics");
            Check(field.Emissions==emitted,"Settling fragments do not create repeated sonar or alarm output");
            var camera=player.GetComponentInChildren<Camera>();camera.transform.position=center+new Vector3(2,2,1.8f);camera.transform.LookAt(center+new Vector3(2,.6f,3.5f));
            int oldMask=camera.cullingMask;
            foreach(var t in cupRemains.GetComponentsInChildren<Transform>())t.gameObject.layer=31;
            camera.cullingMask=1<<31;
            field.ClearPulses();int dark=Capture(camera,"fragments-dark.png");field.EmitFrom(center+new Vector3(2,.5f,2.8f),8,20,1);
            await Frames(150);int lit=Capture(camera,"fragments-sonar.png");Check(lit>dark+50,"Fragments alone participate in point-cloud reveal: "+dark+" -> "+lit);
            camera.cullingMask=oldMask;
            Object.Destroy(cupRemains.gameObject);await Frames(50);

            var ball=Spawn("PickupBouncyBall",new Vector3(-2,1,3));ball.GetComponent<Rigidbody>().linearVelocity=Vector3.forward*12;
            await Frames(200);Check(ball!=null&&!ball.IsBroken,"Existing ball survives high-speed impacts with breakage disabled");
            var rebound=ball.GetComponent<Rigidbody>();rebound.position=center+new Vector3(0,1.2f,1);rebound.linearVelocity=Vector3.zero;Physics.SyncTransforms();
            Check(pickup.TryPickUp(ball)&&pickup.TryThrow(),"Existing ball pickup and throwing still work");
            Object.Destroy(ball.gameObject);await Frames(50);
            var heldOnDisable=Spawn("PickupGlassMug",new Vector3(0,1.2f,1));await Frames(50);
            Check(pickup.TryPickUp(heldOnDisable),"Cup can be held for player-disable lifecycle check");
            player.SetActive(false);await Frames(100);
            Check(heldOnDisable!=null&&!heldOnDisable.IsHeld&&heldOnDisable.transform.parent==fixture.transform
                && !heldOnDisable.GetComponent<Rigidbody>().isKinematic,"Disabling player safely releases held cup after hierarchy transition");
            player.SetActive(true);player.GetComponent<PlayerController>().InputSuspended=true;
            Report("play");return new {passed=true,checks};
        }
        finally
        {
            if(fixture!=null)Object.Destroy(fixture);
            if(recording!=null)Object.Destroy(recording);
            await Task.Delay(100);
            for(int i=0;i<originals.Length;i++)if(originals[i]!=null)originals[i].SetActive(active[i]);
            Report("play");
        }
    }
}
