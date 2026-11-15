using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyDisplayProps
{
    static readonly string[] Names={"EmptyDoubleShelf","ShelfBlock","Earmuffs","TeddyBear"};
    static readonly List<string> checks=new();
    static GameObject Prefab(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+name+".prefab");
    static void Check(bool pass,string label){checks.Add((pass?"PASS: ":"FAIL: ")+label);if(!pass)throw new Exception(label);}
    static Bounds BoundsOf(GameObject go)
    {
        var renderers=go.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
        foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);return bounds;
    }
    public static object Edit()
    {
        checks.Clear();Check(!Application.isPlaying,"Edit mode");
        var original=SceneManager.GetActiveScene();var selection=Selection.objects;
        var windows=Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        void Call(LevelDesignerWindow w,string method)=>typeof(LevelDesignerWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,null);
        foreach(var w in windows)Call(w,"OnDisable");
        Scene scene=default,copy=default;string path="Assets/Scenes/__DisplayProps_"+Guid.NewGuid().ToString("N")+".unity";
        try
        {
            Check(PropCatalog.All().Count==60,"Catalog contains all 60 props");
            Check(PropCatalog.Category(Prefab(Names[0]))=="超市"&&PropCatalog.Category(Prefab(Names[1]))=="通用"&&PropCatalog.Category(Prefab(Names[2]))=="通用"&&PropCatalog.Category(Prefab(Names[3]))=="家","Four props appear in intended categories");
            foreach(var name in Names)
            {
                var prefab=Prefab(name);Check(prefab!=null&&prefab.GetComponent<LevelProp>().surface==PropSurface.Floor,name+" has floor placement metadata");
                Check(prefab.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh!=null&&AssetDatabase.Contains(f.sharedMesh)&&f.sharedMesh.vertexCount>0),name+" uses persistent nonempty meshes");
                Check(prefab.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&!ShaderUtil.ShaderHasError(m.shader))),name+" has valid materials");
                Check(prefab.GetComponentsInChildren<Collider>().All(c=>!c.isTrigger)&&prefab.GetComponentsInChildren<Collider>().Length>0,name+" has usable solid collision");
                Check(Mathf.Abs(BoundsOf(prefab).min.y)<.001f,name+" bottom matches floor anchor");
            }
            var shelfPrefab=Prefab(Names[0]);
            Check(Mathf.Abs(BoundsOf(shelfPrefab).size.y-BoundsOf(Prefab("LibraryBookshelf")).size.y)<.001f,"Shelf matches actual bookshelf height (2.1 m)");
            Check(shelfPrefab.GetComponentsInChildren<BoxCollider>().Count(c=>c.name.StartsWith("Shelf surface "))==3,"Exactly three separately collidable shelf boards");
            Check(shelfPrefab.GetComponentsInChildren<Collider>().Length==7,"No enclosing collider blocks either side or compartments");
            Check(Vector3.Distance(BoundsOf(Prefab("ShelfBlock")).size,Vector3.one*.18f)<.001f,"Small block is an 18 cm cube");
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var layout=new GameObject("Display props verification",typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.SetPositionAndRotation(new Vector3(1500,0,1500),Quaternion.Euler(0,29,0));layout.rooms.Add(new LevelRoom{size=Vector2.one*20});LevelBuilder.Rebuild(layout);
            Check(LevelPropPlacement.TryPose(layout,shelfPrefab,new Ray(layout.World(Vector2.zero,4),-layout.transform.up),PropSurface.Floor,.01f,0,out var pose),"Shelf snaps to rotated layout floor");
            var shelf=LevelPropPlacement.Place(layout,shelfPrefab,pose);
            foreach(var board in shelf.GetComponentsInChildren<BoxCollider>().Where(c=>c.name.StartsWith("Shelf surface ")).OrderBy(c=>c.transform.localPosition.y))
            foreach(int side in new[]{-1,1})
            {
                float y=board.transform.localPosition.y+board.size.y*.5f;var position=new Vector3(-.3f,y,side*.18f);
                var ray=new Ray(shelf.transform.TransformPoint(position+Vector3.up*.3f),-shelf.transform.up);
                Check(LevelPropPlacement.TryPose(layout,Prefab("ShelfBlock"),ray,PropSurface.Floor,.01f,15,out var blockPose),board.name+" accepts block on side "+side);
                var block=LevelPropPlacement.Place(layout,Prefab("ShelfBlock"),blockPose);
                Check(Mathf.Abs(shelf.transform.InverseTransformPoint(block.transform.position).y-y-.01f)<.001f,board.name+" block rests on correct layer without penetration (side "+side+")");
                var access=new Ray(shelf.transform.TransformPoint(new Vector3(.3f,y+.15f,side*1.2f)),shelf.transform.TransformDirection(new Vector3(0,0,-side)));
                Check(!shelf.GetComponentsInChildren<Collider>().Any(c=>c.Raycast(access,out _,2.4f)),board.name+" remains accessible from side "+side);
            }
            foreach(var name in new[]{"Earmuffs","TeddyBear"})
            {
                var top=shelf.GetComponentsInChildren<BoxCollider>().Where(c=>c.name.StartsWith("Shelf surface ")).OrderBy(c=>c.transform.localPosition.y).Last();
                float y=top.transform.localPosition.y+top.size.y*.5f;
                var ray=new Ray(shelf.transform.TransformPoint(new Vector3(.25f,y+.6f,name=="Earmuffs"?.18f:-.18f)),-shelf.transform.up);
                Check(LevelPropPlacement.TryPose(layout,Prefab(name),ray,PropSurface.Floor,.01f,0,out var itemPose),name+" snaps onto shelf");
                LevelPropPlacement.Place(layout,Prefab(name),itemPose);
            }
            LevelBuilder.Persist(layout,path);EditorSceneManager.SaveScene(scene,path);
            EditorSceneManager.CloseScene(scene,true);scene=default;copy=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            var items=copy.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelProp>()).ToArray();
            Check(items.Length==9&&items.All(p=>PrefabUtility.IsPartOfPrefabInstance(p)),"Save/reopen retains shelf, six blocks, earmuffs and bear as prefab instances");
            Check(items.SelectMany(p=>p.GetComponentsInChildren<MeshFilter>()).All(f=>AssetDatabase.Contains(f.sharedMesh)),"Saved prop meshes remain persistent");
            return new {passed=true,count=checks.Count,checks};
        }
        finally
        {
            if(original.IsValid())SceneManager.SetActiveScene(original);
            if(copy.IsValid()&&copy.isLoaded)EditorSceneManager.CloseScene(copy,true);
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            AssetDatabase.DeleteAsset(path);AssetDatabase.DeleteAsset(Path.ChangeExtension(path,null)+"_Geometry");
            Selection.objects=selection;foreach(var w in windows){Call(w,"OnEnable");Call(w,"Refresh");}
            Directory.CreateDirectory("Logs/DisplayProps");File.WriteAllLines("Logs/DisplayProps/edit.txt",checks);
        }
    }
    public static object Preview()
    {
        var atlas=new Texture2D(1440,1200,TextureFormat.RGB24,false);
        try
        {
            for(int i=0;i<4;i++)
            {
                var preview=new PreviewRenderUtility();
                try
                {
                    var model=Object.Instantiate(Prefab(Names[i]));model.GetComponent<LevelProp>().enabled=false;preview.AddSingleGO(model);
                    preview.lights[0].intensity=2.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-30,0);
                    preview.lights[1].intensity=1.1f;preview.lights[1].transform.rotation=Quaternion.Euler(15,100,0);
                    var camera=preview.camera;camera.cameraType=CameraType.Preview;camera.clearFlags=CameraClearFlags.SolidColor;
                    camera.backgroundColor=new Color(.055f,.07f,.09f);camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=50;
                    var bounds=BoundsOf(model);camera.orthographicSize=Mathf.Max(bounds.size.x/1.2f,bounds.size.y,bounds.size.z)*(i==1?.91f:.68f);
                    var direction=i==0?new Vector3(1,.55f,-2.2f):i==1?new Vector3(1,.8f,-2):new Vector3(.65f,.35f,-2.3f);
                    camera.transform.position=bounds.center+direction.normalized*10;camera.transform.LookAt(bounds.center);
                    preview.BeginStaticPreview(new Rect(0,0,720,600));preview.Render(true);var frame=preview.EndStaticPreview();
                    atlas.SetPixels(i%2*720,(1-i/2)*600,720,600,frame.GetPixels());Object.DestroyImmediate(frame);
                }
                finally{preview.Cleanup();}
            }
            atlas.Apply();Directory.CreateDirectory("Logs/DisplayProps");File.WriteAllBytes("Logs/DisplayProps/models.png",atlas.EncodeToPNG());return "Logs/DisplayProps/models.png";
        }
        finally{Object.DestroyImmediate(atlas);}
    }
}
