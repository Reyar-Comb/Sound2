using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class VerifyClockProps
{
    static readonly List<string> checks = new();
    static void Check(bool pass, string label) { checks.Add((pass ? "PASS: " : "FAIL: ") + label); if (!pass) throw new Exception(label); }
    static void Report(string phase) { Directory.CreateDirectory("Logs/ClockProps"); File.WriteAllLines("Logs/ClockProps/"+phase+".txt",checks); }

    public static object Edit()
    {
        checks.Clear();
        var original = SceneManager.GetActiveScene();
        var originalLayout = original.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>()).FirstOrDefault();
        const string path = "Assets/Scenes/__ClockPropsVerification.unity";
        const string folder = "Assets/Scenes/__ClockPropsVerification_Geometry";
        const string fixturePath = "Assets/Scenes/__ClockPropsSource.unity";
        const string fixtureFolder = "Assets/Scenes/__ClockPropsSource_Geometry";
        Check(!File.Exists(path) && !Directory.Exists(folder) && !File.Exists(fixturePath) && !Directory.Exists(fixtureFolder), "Verification destination is unused");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        Scene exported = default;
        var window = EditorWindow.GetWindow<LevelDesignerWindow>();
        try
        {
            SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Prop verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.position = new Vector3(200,0,200);
            layout.transform.rotation = Quaternion.Euler(0,25,0);
            layout.rooms.Add(new LevelRoom { size = new Vector2(10,10) });
            LevelBuilder.Rebuild(layout); window.Bind(layout);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TickingClock.prefab");
            var ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab");
            Check(prefab != null && prefab.GetComponentsInChildren<MeshRenderer>().Length == 3, "Clock model consists of ring and two bars");
            Check(prefab.GetComponent<PickupItem>() == null && prefab.GetComponent<InteractableObject>() == null && prefab.GetComponent<Rigidbody>() == null, "Clock cannot be picked up and stays mounted");
            Check(prefab.GetComponent<SonarCueSource>().clip == null && !prefab.GetComponent<SonarCueSource>().generateDefaultPing, "Clock audio remains empty without synthesized fallback");
            Check(LevelPropPlacement.TryPose(layout,prefab,new Ray(layout.World(Vector2.zero,1.8f),layout.transform.forward),PropSurface.Wall,.015f,0,out var pose), "Wall snapping hits rotated layout walls");
            Check(Vector3.Dot(pose.rotation*Vector3.forward,-layout.transform.forward)>.999f, "Clock face points into the room and stays upright");
            Check(!LevelPropPlacement.TryPose(layout,prefab,new Ray(layout.World(Vector2.zero,2),-layout.transform.up),PropSurface.Wall,0,0,out _), "Wall mode rejects floors");
            var clock = LevelPropPlacement.Place(layout,prefab,pose);
            Check(PrefabUtility.IsPartOfPrefabInstance(clock), "Placed clock keeps its prefab connection");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
            Check(layout.GetComponentsInChildren<TickingClock>(true).Length==0, "Undo removes clock placement and first Props container");
            Undo.PerformRedo();
            clock = layout.GetComponentInChildren<TickingClock>(true).gameObject;
            Check(clock != null && clock.transform.parent.name=="Props", "Redo restores the clock under Props");
            Check(LevelPropPlacement.TryPose(layout,ballPrefab,new Ray(layout.World(new Vector2(1,1),3),-layout.transform.up),PropSurface.Floor,.01f,0,out var ballPose), "Ball snaps to floor");
            var ball = LevelPropPlacement.Place(layout,ballPrefab,ballPose);
            Check(Mathf.Abs(layout.transform.InverseTransformPoint(ball.transform.position).y-.21f)<.001f, "Ball bottom anchor accounts for prefab scale");
            Check(!LevelPropPlacement.TryPose(layout,ballPrefab,new Ray(layout.World(Vector2.zero,1.8f),layout.transform.forward),PropSurface.Floor,0,0,out _), "Floor mode rejects walls");
            var value = clock.GetComponent<TickingClock>(); value.tickInterval=.37f; PrefabUtility.RecordPrefabInstancePropertyModifications(value);
            var location = clock.transform.position;
            LevelBuilder.Rebuild(layout);
            Check(layout.GetComponentInChildren<TickingClock>()==value && clock.transform.position==location && ball!=null, "Rebuild preserves prop identity, transforms and clock tuning");
            typeof(LevelDesignerWindow).GetMethod("ChooseProp",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{prefab});
            Check(window.rootVisualElement.Query<Button>().ToList().Any(b=>b.text=="钟表（贴墙）") && window.rootVisualElement.Query<Button>().ToList().Any(b=>b.text=="球（落地）"), "Editor exposes both built-in prop choices");
            LevelBuilder.Persist(layout,fixturePath); EditorSceneManager.SaveScene(scene,fixturePath);
            LevelBuilder.Export(layout,path,false);
            exported = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            var copy = exported.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>()).Single();
            var copyClock=copy.GetComponentInChildren<TickingClock>(); var copyBall=copy.GetComponentInChildren<PickupItem>();
            Check(copyClock!=null && copyBall!=null && copy.transform.Find("Props").childCount==2, "Export and reload retains clock and ball");
            Check(Mathf.Abs(copyClock.tickInterval-.37f)<.0001f && Vector3.Distance(copyClock.transform.position,location)<.001f, "Export retains per-instance clock tuning and transform");
            Check(copyClock.longHand.IsChildOf(copyClock.transform) && copyClock.shortHand.IsChildOf(copyClock.transform), "Export remaps hand references into exported clock");
            bool linked = PrefabUtility.IsPartOfPrefabInstance(copyClock.gameObject);
            Check(linked, "Export retains prop prefab connection");
            EditorSceneManager.CloseScene(exported,true); exported=default;
            layout.rooms[0].shape=RoomShape.Circle; LevelBuilder.Rebuild(layout);
            Check(LevelPropPlacement.TryPose(layout,prefab,new Ray(layout.World(Vector2.zero,1.8f),layout.transform.forward),PropSurface.Wall,.015f,0,out var curvedPose)
                && Vector3.Dot(curvedPose.rotation*Vector3.up,layout.transform.up)>.999f, "Curved wall placement follows surface normal while upright");
            var actualClock=originalLayout.GetComponentInChildren<TickingClock>();
            Check(actualClock!=null && PrefabUtility.IsPartOfPrefabInstance(actualClock.gameObject), "test scene contains the clock prefab instance");
            Capture(actualClock,false,"editor-clock.png");
            Report("edit"); return new { passed=true, checks };
        }
        finally
        {
            if(exported.IsValid()&&exported.isLoaded)EditorSceneManager.CloseScene(exported,true);
            if(original.IsValid())SceneManager.SetActiveScene(original);
            if(originalLayout!=null)window.Bind(originalLayout);
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            if(File.Exists(path))AssetDatabase.DeleteAsset(path);
            if(Directory.Exists(folder))AssetDatabase.DeleteAsset(folder);
            if(File.Exists(fixturePath))AssetDatabase.DeleteAsset(fixturePath);
            if(Directory.Exists(fixtureFolder))AssetDatabase.DeleteAsset(fixtureFolder);
            Report("edit");
        }
    }

    public static async Task<object> Play()
    {
        checks.Clear();
        Check(Application.isPlaying,"Running in Play mode");
        var clock=Object.FindAnyObjectByType<TickingClock>();
        var field=Object.FindAnyObjectByType<SonarPulseField>();
        Check(clock!=null&&field!=null,"test scene clock and player wave field are active");
        bool running=clock.running; float interval=clock.tickInterval;
        try
        {
            clock.running=false;
            int longStart=clock.LongStep, shortStart=clock.ShortStep;
            var emitter=clock.GetComponent<SonarObjectEmitter>();
            int emitted=emitter.Emissions, ticks=clock.Ticks;
            for(int i=1;i<=144;i++)
            {
                clock.Tick();
                int expectedLong=(longStart+i)%12;
                int expectedShort=(shortStart+(longStart+i)/12)%12;
                if(clock.LongStep!=expectedLong||clock.ShortStep!=expectedShort)throw new Exception("Clock step mismatch at "+i);
                if(Quaternion.Angle(clock.longHand.localRotation,Quaternion.Euler(0,0,30*expectedLong))>.01f
                    ||Quaternion.Angle(clock.shortHand.localRotation,Quaternion.Euler(0,0,30*expectedShort))>.01f)throw new Exception("Hand rotation mismatch at "+i);
                if(expectedLong==3 && Vector3.Dot(clock.longHand.up,-clock.transform.right)<.999f)throw new Exception("Clockwise direction mismatch");
            }
            Check(clock.LongStep==longStart&&clock.ShortStep==shortStart,"144 mechanical ticks complete the full 12 by 12 cycle");
            Check(clock.Ticks==ticks+144&&emitter.Emissions==emitted+144,"Every mechanical tick emits exactly one shared sonar pulse");
            Check(!emitter.automatic&&emitter.toggleKey==UnityEngine.InputSystem.Key.None,"No second timer or keyboard emitter produces extra ticks");
            var cue=clock.GetComponent<SonarCueSource>();
            Check(cue.clip==null&&!cue.generateDefaultPing&&!clock.GetComponent<AudioSource>().isPlaying,"Empty audio stays silent while wave emissions continue");
            var recording=AudioClip.Create("Temporary silent recording for interface verification",4410,1,22050,false);
            try
            {
                cue.clip=recording; clock.Tick();
                Check(clock.GetComponent<AudioSource>().isPlaying,"An assigned tick recording reaches the existing audio output");
            }
            finally {clock.GetComponent<AudioSource>().Stop();cue.clip=null;Object.Destroy(recording);}
            var black=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            Check(clock.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterial==black),"Clock surfaces switch to black during gameplay");
            clock.tickInterval=.2f; clock.running=true;
            int frames=Time.frameCount, count=clock.Ticks;
            await Task.Delay(650);
            Check(Time.frameCount>frames&&clock.Ticks>count,"Real player loop advances clock using configured interval");
            Check(emitter.Emissions-emitted==clock.Ticks-ticks,"Real-time ticks remain one-to-one with sound emitter events");
            clock.running=false; count=clock.Ticks;
            await Task.Delay(250);
            Check(clock.Ticks==count,"Running switch pauses clock");
            field.ClearPulses();
            int dark=Capture(clock,true,"game-clock-dark.png");
            clock.Tick();
            await Task.Delay(150);
            int lit=Capture(clock,true,"game-clock-tick.png");
            Check(lit>dark+20,"Actual tick reveals clock and wall pixels: "+dark+" -> "+lit);
            Report("play"); return new { passed=true, checks };
        }
        finally {clock.running=running;clock.tickInterval=interval; Report("play");}
    }

    static int Capture(TickingClock clock,bool gameplay,string filename)
    {
        var go=new GameObject("Clock verification camera",typeof(Camera));
        var camera=go.GetComponent<Camera>(); camera.enabled=false;
        camera.transform.position=clock.transform.position+clock.transform.forward*2.2f;
        camera.transform.rotation=Quaternion.LookRotation(-clock.transform.forward,clock.transform.up);
        camera.fieldOfView=45;camera.nearClipPlane=.03f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=gameplay?Color.black:new Color(.07f,.085f,.1f);
        var rt=new RenderTexture(800,800,24);var image=new Texture2D(800,800,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,800,800),0,0);image.Apply();
            Directory.CreateDirectory("Logs/ClockProps"); File.WriteAllBytes("Logs/ClockProps/"+filename,image.EncodeToPNG());
            return image.GetPixels32().Count(c=>c.r>30||c.g>30||c.b>30);
        }
        finally {RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(go);Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }
}
