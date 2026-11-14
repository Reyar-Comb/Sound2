using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Object=UnityEngine.Object;

public static class VerifyIndependentLevels
{
    static string[] names={"Bed","Sofa","Counter","HospitalBed","WardCurtain","Shelf","Checkout","Pine","SnowRock","Bench","Pillar","Bedside","Wardrobe","Sink","Toilet","Washer","ProduceBin"};
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    public static object Edit()
    {
        checks=0;
        foreach(var name in names)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+name+".prefab");
            Check(prefab!=null,name+" prefab");
            var filters=prefab.GetComponentsInChildren<MeshFilter>();
            Check(filters.Length==1&&filters[0].sharedMesh.vertexCount>100,name+" combined detailed mesh");
            Check(prefab.GetComponentsInChildren<Collider>().Length>0,name+" colliders");
            Check(prefab.GetComponent<LevelProp>().gameplayMaterial!=null,name+" black presentation");
            var backup=Directory.GetFiles("Logs/IndependentLevels","Adventure"+name+".prefab.meta",SearchOption.AllDirectories).Single();
            Check(File.ReadAllLines(backup).Single(l=>l.StartsWith("guid:"))==File.ReadAllLines("Assets/Prefabs/"+name+".prefab.meta").Single(l=>l.StartsWith("guid:")),name+" GUID retained");
        }
        Check(!AssetDatabase.IsValidFolder("Assets/Prefabs/Adventure"),"Adventure removed");
        var world=Object.FindAnyObjectByType<LevelWorld>();
        Check(world.levels.Sum(l=>l.rooms.Count)==96,"original rooms retained");
        Check(world.connections.Count==7&&world.levels.Sum(l=>l.transform.Find("Props").childCount)==709,"original props retained plus endpoints");
        foreach(var level in world.levels)
        {
            Check(level.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"no missing scripts "+level.name);
            foreach(var f in level.GetComponentsInChildren<MeshFilter>(true))Check(f.sharedMesh!=null,"no missing mesh "+f.name);
        }
        var go=new GameObject("Partition validation",typeof(LevelLayout));go.transform.position=new(8000,0,8000);
        var layout=go.GetComponent<LevelLayout>();var room=new LevelRoom{size=new(12,10),center=new(1,2),rotation=33};layout.rooms.Add(room);
        room.partitions.Add(new RoomPartition{start=new(-3,0),end=new(3,0),height=2,thickness=.2f});
        try
        {
            LevelBuilder.Rebuild(layout);var part=layout.generatedRoot.transform.Find("Partition_0_0");
            Check(part!=null&&part.GetComponent<MeshCollider>().sharedMesh!=null,"partition mesh/collider");
            var p=room.PartitionPoint(Vector2.zero);var side=room.PartitionPoint(Vector2.up)-p;
            var center=layout.transform.TransformPoint(new Vector3(p.x,1,p.y));var direction=new Vector3(side.x,0,side.y);
            Check(Physics.Raycast(center+direction*2,-direction,out var hit,3)&&hit.collider==part.GetComponent<Collider>(),"rotated partition blocks ray");
            Check(part.GetComponent<Renderer>().sharedMaterial==layout.wallMaterial,"partition preview material");
            layout.SetGameplayPresentation(true);Check(part.GetComponent<Renderer>().sharedMaterial==layout.gameplayMaterial,"partition runtime black");layout.SetGameplayPresentation(false);
            room.center+=new Vector2(3,4);room.rotation=70;LevelBuilder.Rebuild(layout);
            Check(Vector2.Distance(room.PartitionLocal(room.PartitionPoint(new(1,2))),new(1,2))<.001f,"partition follows room transform");
            var props=new GameObject("Props").transform;props.SetParent(go.transform,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Washer.prefab"));model.transform.SetParent(props,false);
            const string export="Assets/__IndependentVerify.prefab";
            try
            {
                string originalStore=layout.geometryAsset;LevelBuilder.ExportPrefab(layout,export);
                var saved=AssetDatabase.LoadAssetAtPath<GameObject>(export).GetComponent<LevelLayout>();
                Check(saved.rooms[0].partitions.Count==1&&saved.generatedRoot.transform.Find("Partition_0_0")!=null,"partition prefab persistence");
                Check(saved.transform.position==Vector3.zero&&layout.geometryAsset==originalStore,"export independent origin and source mesh store unchanged");
                Check(layout.generatedRoot.GetComponentsInChildren<MeshFilter>(true).All(f=>f.sharedMesh!=null),"export retains scene preview meshes");
                Check(PrefabUtility.GetCorrespondingObjectFromSource(saved.transform.Find("Props").GetChild(0).gameObject)!=null,"nested prop prefab retained");
            }
            finally{AssetDatabase.DeleteAsset(export);AssetDatabase.DeleteAsset("Assets/__IndependentVerify_Geometry");}
            var previous=layout.generatedRoot;room.partitions[0].end=new(90,0);bool rejected=false;try{LevelBuilder.Rebuild(layout);}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&layout.generatedRoot==previous,"invalid partition preserves geometry");
        }
        finally{if(layout.generatedRoot!=null)foreach(var f in layout.generatedRoot.GetComponentsInChildren<MeshFilter>(true))Object.DestroyImmediate(f.sharedMesh);Object.DestroyImmediate(go);}
        File.WriteAllText("Logs/IndependentLevels/edit.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{checks}));return new{checks};
    }
    public static object Preview()
    {
        const int w=440,h=360;var atlas=new Texture2D(w*4,h*5,TextureFormat.RGB24,false);
        atlas.SetPixels(Enumerable.Repeat(new Color(.07f,.08f,.1f),w*4*h*5).ToArray());
        try
        {
            for(int i=0;i<names.Length;i++)
            {
                var preview=new PreviewRenderUtility();
                try
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+names[i]+".prefab");var model=Object.Instantiate(prefab);
                    model.GetComponent<LevelProp>().enabled=false;preview.AddSingleGO(model);
                    var bounds=model.GetComponentInChildren<Renderer>().bounds;
                    preview.lights[0].intensity=2.5f;preview.lights[0].transform.rotation=Quaternion.Euler(45,-30,0);preview.lights[1].intensity=.8f;preview.lights[1].transform.rotation=Quaternion.Euler(20,150,0);
                    var cam=preview.camera;cam.cameraType=CameraType.Preview;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new(.07f,.08f,.1f);cam.orthographic=true;cam.nearClipPlane=.01f;cam.farClipPlane=100;
                    cam.orthographicSize=Mathf.Max(bounds.size.y,Mathf.Max(bounds.size.x,bounds.size.z))*.72f;cam.transform.position=bounds.center+new Vector3(.9f,.65f,-1.5f).normalized*20;cam.transform.LookAt(bounds.center);
                    preview.BeginStaticPreview(new Rect(0,0,w,h));preview.Render(true);var frame=preview.EndStaticPreview();atlas.SetPixels(i%4*w,(4-i/4)*h,w,h,frame.GetPixels());Object.DestroyImmediate(frame);
                }finally{preview.Cleanup();}
            }
            atlas.Apply();File.WriteAllBytes("Logs/IndependentLevels/models.png",atlas.EncodeToPNG());return names;
        }finally{Object.DestroyImmediate(atlas);}
    }
    public static async Task<object> Browser()
    {
        GameObject selected=null;PropBrowserWindow.Open(p=>selected=p);var window=EditorWindow.GetWindow<PropBrowserWindow>();window.position=new Rect(200,160,690,620);
        window.rootVisualElement.Q<ToolbarSearchField>("prop-search").value="";
        await Task.Delay(3500);var cards=window.rootVisualElement.Query<Button>().ToList();var images=window.rootVisualElement.Query<Image>().ToList();
        Check(cards.Count>40,"browser catalog");Check(images.All(i=>i.image!=null),"all real previews loaded");
        Check(cards.Select(b=>Mathf.Round(b.worldBound.x)).Distinct().Count()>1,"wrapping grid");
        window.rootVisualElement.Q<ToolbarSearchField>("prop-search").value="洗衣机";await Task.Delay(200);
        cards=window.rootVisualElement.Query<Button>().ToList().Where(b=>b.name.StartsWith("prop-card-")).ToList();Check(cards.Count==1&&cards[0].name=="prop-card-Washer","Chinese search");
        cards[0].Focus();
        using(var e=NavigationSubmitEvent.GetPooled())cards[0].SendEvent(e);
        await Task.Delay(100);
        Check(selected!=null&&selected.name=="Washer","selection callback");
        window.rootVisualElement.Q<ToolbarSearchField>("prop-search").value="";
        window.position=new Rect(200,160,360,440);await Task.Delay(250);
        cards=window.rootVisualElement.Query<Button>().ToList().Where(b=>b.name.StartsWith("prop-card-")).ToList();
        Check(cards.All(b=>b.worldBound.xMax<=window.rootVisualElement.worldBound.xMax),"narrow window cards within bounds");
        for(int i=0;i<cards.Count;i++)for(int j=i+1;j<cards.Count;j++)Check(!cards[i].worldBound.Overlaps(cards[j].worldBound),"cards never overlap");
        window.position=new Rect(200,160,690,620);window.Close();return new{count=images.Count};
    }
    public static object FinalizeAssets()
    {
        var world=Object.FindAnyObjectByType<LevelWorld>();
        foreach(var level in world.levels)
        {
            var actors=level.transform.Find("Encounters");if(actors!=null)actors.gameObject.SetActive(true);
            string path="Assets/Levels/"+level.name.Split(' ')[0]+".prefab";
            LevelBuilder.ExportPrefab(level,path,false);
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var savedActors=root.transform.Find("Encounters");if(savedActors!=null)savedActors.gameObject.SetActive(true);
                Check(root.GetComponent<LevelWorld>()==null,"single level has no world graph");
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);EditorSceneManager.SaveScene(world.gameObject.scene);AssetDatabase.SaveAssets();return "Saved independent levels and world";
    }
    public static async Task<object> PrefabEditing()
    {
        const string path="Assets/__PrefabEditingVerify.prefab";
        var world=Object.FindAnyObjectByType<LevelWorld>();var go=new GameObject("Prefab editing verification",typeof(LevelLayout));var layout=go.GetComponent<LevelLayout>();layout.rooms.Add(new LevelRoom());
        var window=EditorWindow.GetWindow<LevelDesignerWindow>();var originalRect=window.position;
        try
        {
            LevelBuilder.ExportPrefab(layout,path);window.Bind(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<LevelLayout>());
            var stage=PrefabStageUtility.GetCurrentPrefabStage();Check(stage!=null&&stage.assetPath==path,"opens prefab in editable stage");
            var edited=stage.prefabContentsRoot.GetComponent<LevelLayout>();edited.rooms[0].partitions.Add(new RoomPartition());
            typeof(LevelDesignerWindow).GetMethod("Save",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(window,null);
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<LevelLayout>().rooms[0].partitions.Count==1,"saves partitions in prefab stage");
            window.position=new Rect(120,100,600,440);await Task.Delay(250);var buttons=window.rootVisualElement.Q("footer").Query<Button>().ToList();
            Check(buttons.Count==4&&buttons.All(b=>b.worldBound.xMax<=window.rootVisualElement.worldBound.xMax+.1f),"footer fits minimum width");
            for(int i=0;i<buttons.Count;i++)for(int j=i+1;j<buttons.Count;j++)Check(!buttons[i].worldBound.Overlaps(buttons[j].worldBound),"footer no overlaps");
            return "Prefab stage save and minimum window layout passed";
        }
        finally
        {
            StageUtility.GoToMainStage();window.position=originalRect;window.Bind(world.startingLevel);
            if(layout.generatedRoot!=null)foreach(var f in layout.generatedRoot.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh!=null&&!AssetDatabase.Contains(f.sharedMesh))Object.DestroyImmediate(f.sharedMesh);
            Object.DestroyImmediate(go);AssetDatabase.DeleteAsset(path);AssetDatabase.DeleteAsset("Assets/__PrefabEditingVerify_Geometry");
        }
    }
    public static object Audit()
    {
        var world=Object.FindAnyObjectByType<LevelWorld>();
        Check(world!=null&&!Application.isPlaying,"world remains in edit mode");
        foreach(var level in world.levels)
        {
            var saved=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Levels/"+level.name.Split(' ')[0]+".prefab");
            Check(saved!=null,"independent prefab exists");
            Check(saved.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"prefab no missing scripts");
            foreach(var f in saved.GetComponentsInChildren<MeshFilter>(true))Check(f.sharedMesh!=null&&AssetDatabase.Contains(f.sharedMesh),"persistent mesh references");
            foreach(var e in saved.GetComponentsInChildren<LevelElevator>(true))Check(e.leftDoor!=null&&e.rightDoor!=null&&e.Level!=null,"endpoint local references");
            Check(PrefabUtility.GetCorrespondingObjectFromSource(saved.transform.Find("Props").GetChild(0).gameObject)!=null,"exported nested prefab remains connected");
        }
        Check(world.connections.All(c=>c.source!=null&&c.destination!=null&&c.source.Level!=c.destination.Level),"scene endpoint graph intact");
        EditorSceneManager.SaveScene(world.gameObject.scene);
        File.WriteAllText("Logs/IndependentLevels/audit.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{checks,levels=world.levels.Count}));return new{checks,levels=world.levels.Count};
    }
    static async Task Until(Func<bool> condition,string message,float timeout=12)
    {
        double deadline=EditorApplication.timeSinceStartup+timeout;
        while(!condition()&&EditorApplication.timeSinceStartup<deadline)await Task.Delay(30);
        Check(condition(),message);
    }
    public static async Task<object> Play()
    {
        Check(Application.isPlaying,"requires Play mode");checks=0;int frame=Time.frameCount;await Task.Delay(300);Check(Time.frameCount>frame,"player loop advances");
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();if(menu!=null)menu.BeginGame();await Until(()=>!SonicSpaceStartMenu.BlocksGameplay,"menu completes");
        var world=Object.FindAnyObjectByType<LevelWorld>();var player=world.player;
        Check(world.CurrentLevel==world.startingLevel,"starting level");Check(world.levels.Count(l=>l.gameObject.activeSelf)==1,"only current level active");
        Check(!world.Travel(null,player),"null endpoint rejected");
        var connection=world.connections[0];var source=connection.source;var destination=connection.destination;
        // Exercise a different orientation and arbitrary displacement without altering saved scene data.
        var destinationLevel=destination.Level;destinationLevel.transform.position+=new Vector3(900,31,400);destinationLevel.transform.Rotate(0,90,0);connection.travelSeconds=.15f;connection.bidirectional=true;
        var interaction=player.GetComponent<PlayerInteraction>();source.Use(interaction,false);await Until(()=>source.DoorOpen>.999f,"outside call opens doors");
        player.Teleport(source.transform.TransformPoint(new Vector3(0,1,0)));await Task.Delay(100);
        var ball=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));ball.transform.position=source.transform.TransformPoint(new Vector3(.9f,1,0));
        var body=ball.GetComponent<Rigidbody>();body.isKinematic=true;Physics.SyncTransforms();var offset=source.transform.InverseTransformPoint(body.position);
        var held=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));held.transform.position=player.GetComponentInChildren<Camera>().transform.position+player.transform.forward*.6f;Physics.SyncTransforms();
        var pickup=player.GetComponent<PlayerPickup>();Check(pickup.TryPickUp(held.GetComponent<PickupItem>()),"pickup before travel");
        source.Use(interaction,true);Check(world.Travelling&&player.TransportLocked,"travel locks passenger");
        await Until(()=>!world.Travelling,"travel completes");
        Check(world.CurrentLevel==destinationLevel&&destination.Contains(player.transform.position),"arrives at rotated displaced endpoint");
        Check(!player.TransportLocked&&destination.DoorOpen>.999f,"arrival unlocks and opens");
        Check(Vector3.Distance(body.position,destination.transform.TransformPoint(offset))<.05f&&body.isKinematic,"cargo offset and physics retained");
        Check(pickup.HeldItem==held.GetComponent<PickupItem>(),"held item remains held");
        held.GetComponent<PickupItem>().Release(player.transform.position+Vector3.right,Vector3.zero);await Task.Delay(100);
        Check(held.transform.IsChildOf(destinationLevel.transform),"release belongs to destination level");
        destination.Use(interaction,true);await Until(()=>!world.Travelling,"bidirectional return completes");Check(world.CurrentLevel==source.Level,"returns by world connection");
        source.Use(interaction,true);Check(world.Travelling,"second trip begins");world.enabled=false;Check(!world.Travelling&&!player.TransportLocked&&!source.Busy,"cancel restores input and endpoint");world.enabled=true;
        Object.Destroy(ball);Object.Destroy(held);
        File.WriteAllText("Logs/IndependentLevels/play.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{checks,frame=Time.frameCount}));return new{checks,frame=Time.frameCount};
    }
}
