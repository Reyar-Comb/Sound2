using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VerifyPartitionAlignment
{
    public static async Task<object> Run()
    {
        var checks = new System.Collections.Generic.List<string>();
        void Check(bool ok, string label) { checks.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new Exception(label); }
        var original = SceneManager.GetActiveScene(); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var l = new GameObject("Alignment test",typeof(LevelLayout)).GetComponent<LevelLayout>();
            l.transform.position = new Vector3(1500,0,1500); l.transform.rotation = Quaternion.Euler(0,32,0);
            l.rooms.Add(new LevelRoom { center=new Vector2(.5f,-18),size=new Vector2(10,10) });
            l.corridors.Add(new LevelCorridor { width=3,points=new System.Collections.Generic.List<Vector2>{new Vector2(3,-5),new Vector2(3,-13.5f)} });
            var p = new RoomPartition {start=new Vector2(4.5f,-13),end=new Vector2(1.5f,-13),thickness=.15f,height=3.2f}; l.partitions.Add(p); LevelBuilder.Rebuild(l);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WoodenDoor.prefab");
            var door=LevelPropPlacement.Place(l,prefab,new Pose(l.World(new Vector2(3,-13)),l.transform.rotation));
            var originalPose=door.transform.position;
            PartitionAlignment.Align(l,p);
            Check(Mathf.Abs(p.thickness-.2f)<.001f && Mathf.Abs(p.start.y+12.9f)<.001f && Mathf.Abs(p.end.y+12.9f)<.001f,"Matches outer-wall thickness and center, not thickness alone");
            Check(Mathf.Abs(p.start.y-p.thickness/2+13)<.001f && Mathf.Abs(p.start.y+p.thickness/2+12.8f)<.001f,"Both partition faces coincide with neighboring wall faces");
            Check(Vector3.Distance(door.transform.position,originalPose+l.transform.forward*.1f)<.001f && door.GetComponent<EmbeddedDoor>().partitionId==p.id,"Door follows aligned partition with stable binding");
            var collider=l.generatedRoot.transform.Find("Partition_Free_"+p.id).GetComponent<Collider>();
            Check(!collider.Raycast(new Ray(l.World(new Vector2(3,-14),1),l.transform.forward),out _,2)
                && collider.Raycast(new Ray(l.World(new Vector2(4,-14),1),l.transform.forward),out _,2),"Door aperture remains passable, side wall solid");
            Undo.PerformUndo(); await Task.Delay(500); p=l.partitions.Single();
            Check(Mathf.Abs(p.thickness-.15f)<.001f && Vector3.Distance(door.transform.position,originalPose)<.001f,$"Undo restores partition and door (thickness={p.thickness}, doorDelta={Vector3.Distance(door.transform.position,originalPose)})");
            Undo.PerformRedo(); await Task.Delay(500); p=l.partitions.Single();
            Check(Mathf.Abs(p.start.y+12.9f)<.001f && Vector3.Distance(door.transform.position,originalPose+l.transform.forward*.1f)<.001f,"Redo realigns partition and door");
            PartitionAlignment.Align(l,p);
            Check(Mathf.Abs(p.start.y+12.9f)<.001f,"Repeated alignment does not drift");
            var detached=new RoomPartition {start=new Vector2(30,0),end=new Vector2(35,0)}; l.partitions.Add(detached); LevelBuilder.Rebuild(l);
            bool rejected=false; try {PartitionAlignment.Align(l,detached);}catch(InvalidOperationException){rejected=true;}
            Check(rejected && detached.start==new Vector2(30,0) && detached.thickness==.15f,"Unconnected partition is left unchanged with explanation");
            return new {passed=true,checks};
        }
        finally
        {
            SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);
            Directory.CreateDirectory("Logs/WoodenDoor");File.WriteAllLines("Logs/WoodenDoor/alignment.txt",checks);
        }
    }
    public static object FixCurrent()
    {
        var l=UnityEngine.Object.FindAnyObjectByType<LevelLayout>();
        if(l==null || l.gameObject.scene.path!="Assets/Scenes/test.unity") throw new Exception("Expected current test scene");
        var p=l.partitions.Single(x=>x.id=="e74c1c2b76584ef8b8026397334de1ad");
        PartitionAlignment.Align(l,p);
        return new {p.start,p.end,p.thickness,dirty=l.gameObject.scene.isDirty};
    }
}
