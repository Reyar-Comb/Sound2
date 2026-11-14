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

public static class VerifyWoodenDoor
{
    static readonly List<string> checks = new();
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool ok, string name) { checks.Add((ok ? "PASS " : "FAIL ") + name); if (!ok) throw new Exception(name); }
    static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WoodenDoor.prefab");
    static bool Hit(LevelLayout l, string key, Vector2 origin, Vector2 direction, float y = 1, float distance = 2) =>
        l.generatedRoot.transform.Find(key).GetComponent<Collider>().Raycast(new Ray(l.World(origin, y), l.transform.TransformDirection(new Vector3(direction.x, 0, direction.y))), out _, distance);
    static bool Aim(LevelLayout l, Vector2 point, out Pose pose, GameObject source = null) => EmbeddedDoorPlacement.TryPose(l, source != null ? source : Prefab,
        new Ray(l.World(point, 5), -l.transform.up), out pose);
    static void Refuse(Action action, string label) { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Check(failed, label); }
    static void Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, Flags).Invoke(o, args);
    static void Click(Button b) => typeof(Clickable).GetMethod("Invoke", Flags).Invoke(b.clickable, new object[] { null });
    public static async Task<object> Edit()
    {
        checks.Clear(); Check(!Application.isPlaying, "Edit mode");
        var original = SceneManager.GetActiveScene(); var selection = Selection.objects;
        var windows = Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        foreach (var w in windows) Invoke(w, "OnDisable");
        Scene scene = default, copyScene = default; LevelDesignerWindow ui = null;
        string path = "Assets/Scenes/__FreePartitionTest_" + Guid.NewGuid().ToString("N") + ".unity";
        string sourcePath = path.Replace(".unity", "_Source.unity");
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var l = new GameObject("Free partition verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            l.transform.position = new Vector3(1300, 0, 1300); l.transform.rotation = Quaternion.Euler(0, 21, 0);
            var a = new LevelRoom { id = "A", center = new Vector2(0, -3), size = Vector2.one * 6 };
            var b = new LevelRoom { id = "B", center = new Vector2(0, 3), size = Vector2.one * 6 };
            l.rooms.Add(a); l.rooms.Add(b); LevelBuilder.Rebuild(l);
            Check(Prefab != null && Prefab.GetComponentsInChildren<MeshFilter>().Length == 4 && Prefab.GetComponent<DoorInteraction>() != null, "Wooden model and interaction preserved");
            Check(!Aim(l, Vector2.zero, out _), "Erased shared face alone rejects door");
            Check(!EmbeddedDoorPlacement.TryPose(l, Prefab, new Ray(l.World(new Vector2(0,-4),1), l.transform.TransformDirection(Vector3.back)), out _), "Actual room perimeter wall rejects door");
            Refuse(() => LevelPropPlacement.Place(l, Prefab, new Pose(l.World(new Vector2(0,-6.1f)), l.transform.rotation)), "Direct API rejects perimeter door");
            Check(!Hit(l, "Walls", new Vector2(2,-1), Vector2.up), "Room common-face elimination unchanged");
            var p = new RoomPartition { start = new Vector2(-3,0), end = new Vector2(3,0), height = 3, thickness = .25f };
            l.partitions.Add(p); LevelBuilder.Rebuild(l); string key = "Partition_Free_" + p.id;
            Check(Hit(l,key,new Vector2(0,-1),Vector2.up), "Independent partition spans erased room interface");
            Check(Aim(l, Vector2.zero, out var pose), "Top-down partition targeting accepted");
            Check(EmbeddedDoorPlacement.TryPose(l,Prefab,new Ray(l.World(new Vector2(-1,-1),1),l.transform.TransformDirection(new Vector3(1,0,1).normalized)),out _), "Oblique side targeting accepted");
            var go = LevelPropPlacement.Place(l, Prefab, pose); var door = go.GetComponent<EmbeddedDoor>();
            Check(door.partitionId == p.id && string.IsNullOrEmpty(door.roomId) && string.IsNullOrEmpty(door.sharedBoundaryId), "Door binds independent partition only");
            Check(!Hit(l,key,new Vector2(0,-1),Vector2.up) && Hit(l,key,new Vector2(2,-1),Vector2.up)
                && Hit(l,"EmbeddedLintel_0",new Vector2(0,-1),Vector2.up,2.7f), "Partition aperture and lintel have real colliders");
            Refuse(() => LevelPropPlacement.Place(l, Prefab, pose), "Duplicate doorway rejected");
            p.start += new Vector2(20,0); p.end += new Vector2(20,0); LevelBuilder.Rebuild(l);
            Check(Vector2.Distance(l.Local(go.transform.position),new Vector2(20,0))<.001f, "Partition and attached door move outside all rooms");
            p.height = 4; LevelBuilder.Rebuild(l);
            Check(Hit(l,key,new Vector2(22,-1),Vector2.up,3.8f), "Independent partition height is not capped by room wall height");
            l.rooms.Clear(); LevelBuilder.Rebuild(l);
            Check(!Hit(l,key,new Vector2(20,-1),Vector2.up), "Partition with door works in layout without rooms or corridors");
            Check(!Aim(l,new Vector2(22.8f,0),out _), "Insufficient end clearance rejected");
            Check(Aim(l,new Vector2(21,0),out var moved,go), "Existing door can target another position on partition");
            LevelPropPlacement.Reposition(l,go,moved); Undo.PerformUndo(); await Task.Delay(600);
            Check(Vector2.Distance(l.Local(go.transform.position),new Vector2(20,0))<.01f, "Undo door movement restores attachment");
            Undo.PerformRedo(); await Task.Delay(600);
            Check(Vector2.Distance(l.Local(go.transform.position),new Vector2(21,0))<.01f, "Redo door movement updates cut");
            l.corridors.Add(new LevelCorridor { width=1.3f, points=new List<Vector2>{new Vector2(0,-4),new Vector2(0,4)} }); LevelBuilder.Rebuild(l);
            Check(!Aim(l,Vector2.zero,out _), "Equal-width corridor alone rejects door");
            Refuse(() => LevelPropPlacement.Place(l,Prefab,new Pose(l.World(Vector2.zero),l.transform.rotation)), "Direct API rejects corridor-middle door");
            var across = new RoomPartition { start = new Vector2(-2,0), end = new Vector2(2,0), height=3 };
            l.partitions.Add(across); LevelBuilder.Rebuild(l);
            Check(Aim(l,Vector2.zero,out var acrossPose), "Partition across corridor can host door");
            var acrossDoor = LevelPropPlacement.Place(l,Prefab,acrossPose);
            l.rooms.Add(a); var legacy = new RoomPartition { start=new Vector2(-3,12),end=new Vector2(3,12),height=3 };
            a.partitions.Add(legacy); LevelBuilder.Rebuild(l);
            Check(Aim(l,new Vector2(0,9),out var legacyPose), "Existing room-relative partitions may extend outside room");
            var legacyDoor = LevelPropPlacement.Place(l,Prefab,legacyPose).GetComponent<EmbeddedDoor>();
            ui = ScriptableObject.CreateInstance<LevelDesignerWindow>(); ui.ShowUtility(); ui.CreateGUI();
            typeof(LevelDesignerWindow).GetField("layout",Flags).SetValue(ui,l); Invoke(ui,"Refresh");
            Check(ui.rootVisualElement.Q("shared-boundaries")==null && ui.rootVisualElement.Q("partition-objects")!=null
                && ui.rootVisualElement.Query<Button>().ToList().Any(x=>x.text=="隔板"), "UI exposes independent partition tool and removes connection choices");
            Click(ui.rootVisualElement.Query<Button>().ToList().Single(x=>x.text=="隔板"));
            typeof(LevelDesignerWindow).GetField("hover",Flags).SetValue(ui,new Vector2(40,0)); Invoke(ui,"PlacePartitionPoint");
            Check(l.partitions.Count==2,"First draw click only sets start point");
            typeof(LevelDesignerWindow).GetField("hover",Flags).SetValue(ui,new Vector2(45,2)); Invoke(ui,"PlacePartitionPoint");
            Check(l.partitions.Count==3 && l.partitions.Last().start==new Vector2(40,0) && l.partitions.Last().end==new Vector2(45,2),"Second draw click creates free partition outside rooms");
            var centeredPartition = l.partitions.Last(); string centeredId = centeredPartition.id;
            var center = (centeredPartition.start + centeredPartition.end) * .5f;
            var end = centeredPartition.end;
            centeredPartition.end = centeredPartition.start + Vector2.right * .5f;
            int priorDoors = EmbeddedDoorPlacement.Doors(l).Length;
            Click(ui.rootVisualElement.Query<Button>().ToList().Single(x=>x.text=="在隔板中心放门"));
            Check(EmbeddedDoorPlacement.Doors(l).Length == priorDoors && (bool)typeof(LevelDesignerWindow).GetField("hasError",Flags).GetValue(ui), "Center button rejects short partition without creating a door");
            centeredPartition.end = end;
            centeredPartition.height = 1;
            Check(!EmbeddedDoorPlacement.TryCenterPose(l,Prefab,"",centeredId,out _), "Center placement rejects insufficient height");
            centeredPartition.height = 3;
            var cross = new RoomPartition { start=center-Vector2.up*2,end=center+Vector2.up*2,height=3 };
            l.partitions.Add(cross);
            Check(!EmbeddedDoorPlacement.TryCenterPose(l,Prefab,"",centeredId,out _), "Center placement rejects intersecting partition");
            l.partitions.Remove(cross);
            Click(ui.rootVisualElement.Query<Button>().ToList().Single(x=>x.text=="在隔板中心放门"));
            var centerDoor = EmbeddedDoorPlacement.Doors(l).Single(x=>x.partitionId==centeredId);
            Check(Mathf.Abs(centerDoor.along-.5f)<.0001f && Vector3.Distance(centerDoor.transform.position,l.World(center))<.002f, "Button immediately creates grounded door at angled partition midpoint in rotated layout");
            Check(typeof(LevelDesignerWindow).GetField("tool",Flags).GetValue(ui).ToString()=="Select", "Button finishes placement without waiting for a Scene click");
            var tangent = (end-centeredPartition.start).normalized; var normal = new Vector2(-tangent.y,tangent.x);
            string centerKey = "Partition_Free_" + centeredId;
            Check(!Hit(l,centerKey,center-normal,normal) && Hit(l,centerKey,center+tangent*2-normal,normal), "Automatic center door cuts real collision at midpoint only");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo(); await Task.Delay(600);
            Check(!EmbeddedDoorPlacement.Doors(l).Any(x=>x.partitionId==centeredId) && Hit(l,centerKey,center-normal,normal), "Undo removes automatically placed door and repairs wall");
            Undo.PerformRedo(); await Task.Delay(600);
            Check(EmbeddedDoorPlacement.Doors(l).Any(x=>x.partitionId==centeredId && Mathf.Abs(x.along-.5f)<.0001f) && !Hit(l,centerKey,center-normal,normal), "Redo restores centered door and opening");
            var kindType=typeof(LevelDesignerWindow).GetNestedType("Kind",BindingFlags.NonPublic);
            Invoke(ui,"Select",Enum.Parse(kindType,"Partition"),l.partitions.FindIndex(x=>x.id==centeredId));
            Click(ui.rootVisualElement.Query<Button>().ToList().Single(x=>x.text=="在隔板中心放门"));
            Check(EmbeddedDoorPlacement.Doors(l).Count(x=>x.partitionId==centeredId)==1 && (bool)typeof(LevelDesignerWindow).GetField("hasError",Flags).GetValue(ui), "Repeated center click refuses a duplicate doorway");
            Click(ui.rootVisualElement.Query<Button>().ToList().Single(x=>x.text=="删除隔板及其门"));
            Invoke(ui,"DetachPartition",a,legacy);
            Check(l.partitions.Contains(legacy) && a.partitions.Count==0 && string.IsNullOrEmpty(legacyDoor.roomId)
                && Vector2.Distance(l.Local(legacyDoor.transform.position),new Vector2(0,9))<.01f, "Detach legacy partition keeps world pose and door binding");
            Check(ui.rootVisualElement.Query<Button>().ToList().Any(x=>x.text=="在隔板中心放门"), "Selected partition offers centered door-placement action");
            Click(ui.rootVisualElement.Query<Button>().ToList().Single(x=>x.text=="删除隔板及其门"));
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Check(!l.partitions.Contains(legacy) && legacyDoor==null, "Deleting partition also removes its embedded door");
            Undo.PerformUndo(); await Task.Delay(600);
            Check(l.partitions.Any(x=>x.id==legacy.id) && EmbeddedDoorPlacement.Doors(l).Any(x=>x.partitionId==legacy.id), $"Undo deletion restores partition and door together (partitions={string.Join(",", l.partitions.Select(x=>x.id))}, expected={legacy.id}, doors={string.Join(",", EmbeddedDoorPlacement.Doors(l).Select(x=>x.partitionId))})");
            Object.DestroyImmediate(ui); ui=null;
            LevelBuilder.Persist(l,sourcePath); EditorSceneManager.SaveScene(scene,sourcePath);
            LevelBuilder.Export(l,path,false); copyScene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            var copy=copyScene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<LevelLayout>()).Single();
            Check(copy.partitions.Count==3 && EmbeddedDoorPlacement.Doors(copy).Length==3, "Export/reopen retains independent partitions and doors");
            Check(EmbeddedDoorPlacement.Doors(copy).All(x=>PrefabUtility.IsPartOfPrefabInstance(x)) && copy.generatedRoot.GetComponentsInChildren<MeshFilter>(true).All(x=>AssetDatabase.Contains(x.sharedMesh)), "Export keeps prefab links and persistent collider meshes");
            return new { passed=true,checks };
        }
        finally
        {
            if(ui!=null) Object.DestroyImmediate(ui);
            if(copyScene.IsValid()&&copyScene.isLoaded) EditorSceneManager.CloseScene(copyScene,true);
            if(original.IsValid()) SceneManager.SetActiveScene(original);
            if(scene.IsValid()&&scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
            AssetDatabase.DeleteAsset(path); AssetDatabase.DeleteAsset(Path.ChangeExtension(path,null)+"_Geometry");
            AssetDatabase.DeleteAsset(sourcePath); AssetDatabase.DeleteAsset(Path.ChangeExtension(sourcePath,null)+"_Geometry");
            Selection.objects=selection;
            foreach(var w in windows){Invoke(w,"OnEnable");Invoke(w,"Refresh");}
            Directory.CreateDirectory("Logs/WoodenDoor"); File.WriteAllLines("Logs/WoodenDoor/edit.txt",checks);
        }
    }

    public static object Preview()
    {
        var original=SceneManager.GetActiveScene(); var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var go=new GameObject("独立隔板示例",typeof(LevelLayout)); SceneManager.MoveGameObjectToScene(go,scene);
        var l=go.GetComponent<LevelLayout>(); l.rooms.Add(new LevelRoom{size=Vector2.one*8});
        l.partitions.Add(new RoomPartition{name="自由隔板",start=new Vector2(-6,0),end=new Vector2(6,0),height=3.2f}); LevelBuilder.Rebuild(l);
        var ui=ScriptableObject.CreateInstance<LevelDesignerWindow>(); ui.titleContent=new GUIContent("隔板工具预览"); ui.position=new Rect(100,100,1000,800); ui.ShowUtility();ui.CreateGUI();
        typeof(LevelDesignerWindow).GetField("layout",Flags).SetValue(ui,l);
        var kind=typeof(LevelDesignerWindow).GetNestedType("Kind",BindingFlags.NonPublic);
        Invoke(ui,"Select",Enum.Parse(kind,"Partition"),0);
        ui.rootVisualElement.Q<Foldout>("settings-foldout").value=false;
        SceneManager.SetActiveScene(original);return "Preview ready";
    }
    public static object ClosePreview()
    {
        var ui=Resources.FindObjectsOfTypeAll<LevelDesignerWindow>().Single(w=>w.titleContent.text=="隔板工具预览");
        var scene=((LevelLayout)typeof(LevelDesignerWindow).GetField("layout",Flags).GetValue(ui)).gameObject.scene;
        ui.Close();EditorSceneManager.CloseScene(scene,true);return "Preview removed";
    }
}
