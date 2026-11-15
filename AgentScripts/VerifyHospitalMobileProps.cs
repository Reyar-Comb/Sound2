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

public static class VerifyHospitalMobileProps
{
    static readonly string[] Names={"MobileHospitalBed","MedicalCart"};
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
        Scene scene=default,copy=default;string path="Assets/Scenes/__HospitalMobile_"+Guid.NewGuid().ToString("N")+".unity";
        try
        {
            Check(PropCatalog.All().Count==62&&PropCatalog.All().Count(p=>PropCatalog.Category(p)=="医院")==5,"Catalog has 62 props and five Hospital entries");
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var layout=new GameObject("Hospital mobile verification",typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.SetPositionAndRotation(new Vector3(1700,0,1700),Quaternion.Euler(0,31,0));layout.rooms.Add(new LevelRoom{size=Vector2.one*20});LevelBuilder.Rebuild(layout);
            foreach(var name in Names)
            {
                var prefab=Prefab(name);
                Check(PropCatalog.Category(prefab)=="医院"&&prefab.GetComponent<LevelProp>().surface==PropSurface.Floor,name+" is a Hospital floor prop");
                Check(prefab.GetComponentsInChildren<Renderer>().Length==1&&prefab.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh!=null&&AssetDatabase.Contains(f.sharedMesh)),name+" uses a single baked persistent visual mesh");
                Check(prefab.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m!=null&&!ShaderUtil.ShaderHasError(m.shader))),name+" has valid shared materials");
                Check(Mathf.Abs(BoundsOf(prefab).min.y)<.001f,name+" wheels touch the floor anchor");
                Check(prefab.GetComponentsInChildren<SphereCollider>().Count(c=>c.name=="Caster contact")==4,name+" has four wheel contacts");
                Check(prefab.GetComponent<PushableProp>()!=null&&!prefab.GetComponent<Rigidbody>().isKinematic&&prefab.GetComponent<PickupItem>()==null,name+" uses ShoppingCart push system without pickup");
                Check(prefab.GetComponentsInChildren<Collider>().All(c=>!c.isTrigger&&(!(c is MeshCollider mesh)||mesh.convex)&&c.sharedMaterial!=null),name+" has dynamic-compatible low-friction collision");
                var cue=prefab.GetComponent<SonarCueSource>();var sonar=prefab.GetComponent<SonarObjectEmitter>();
                Check(cue.output==prefab.GetComponent<AudioSource>()&&sonar.emissionOrigin.IsChildOf(prefab.transform),name+" audio and emission references belong to this prefab");
                var point=new Vector2(name==Names[0]?-2:2,0);
                Check(LevelPropPlacement.TryPose(layout,prefab,new Ray(layout.World(point,4),-layout.transform.up),PropSurface.Floor,.01f,17,out var pose),name+" snaps to rotated floor");
                var item=LevelPropPlacement.Place(layout,prefab,pose);
                Check(Mathf.Abs(layout.transform.InverseTransformPoint(item.transform.position).y-.01f)<.001f,name+" placement does not sink into floor");
                if(name=="MedicalCart")
                {
                    var box=Prefab("ShelfBlock");var ray=new Ray(item.transform.TransformPoint(new Vector3(0,1.3f,0)),-item.transform.up);
                    Check(LevelPropPlacement.TryPose(layout,box,ray,PropSurface.Floor,.01f,0,out var boxPose),"Medical cart work tray accepts another prop");
                    Check(Mathf.Abs(item.transform.InverseTransformPoint(boxPose.position).y-.9705f)<.002f,"Work tray placement hits actual top surface");
                }
            }
            LevelBuilder.Persist(layout,path);EditorSceneManager.SaveScene(scene,path);EditorSceneManager.CloseScene(scene,true);scene=default;
            copy=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            var saved=copy.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PushableProp>()).ToArray();
            Check(saved.Length==2&&saved.All(p=>PrefabUtility.IsPartOfPrefabInstance(p)),"Save/reopen retains both pushable prefab instances");
            Check(saved.All(p=>p.GetComponent<Rigidbody>().constraints==(RigidbodyConstraints.FreezeRotationX|RigidbodyConstraints.FreezeRotationZ)),"Saved upright constraints remain intact");
            return new{passed=true,count=checks.Count,checks};
        }
        finally
        {
            if(original.IsValid())SceneManager.SetActiveScene(original);
            if(copy.IsValid()&&copy.isLoaded)EditorSceneManager.CloseScene(copy,true);
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            AssetDatabase.DeleteAsset(path);AssetDatabase.DeleteAsset(Path.ChangeExtension(path,null)+"_Geometry");
            Selection.objects=selection;foreach(var w in windows){Call(w,"OnEnable");Call(w,"Refresh");}
            Directory.CreateDirectory("Logs/HospitalMobileProps");File.WriteAllLines("Logs/HospitalMobileProps/edit.txt",checks);
        }
    }
    public static object Preview()
    {
        var atlas=new Texture2D(1600,700,TextureFormat.RGB24,false);
        try
        {
            for(int i=0;i<2;i++)
            {
                var preview=new PreviewRenderUtility();
                try
                {
                    var model=Object.Instantiate(Prefab(Names[i]));model.GetComponent<LevelProp>().enabled=false;preview.AddSingleGO(model);
                    preview.lights[0].intensity=2.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-30,0);
                    preview.lights[1].intensity=1.1f;preview.lights[1].transform.rotation=Quaternion.Euler(15,100,0);
                    var camera=preview.camera;camera.cameraType=CameraType.Preview;camera.clearFlags=CameraClearFlags.SolidColor;
                    camera.backgroundColor=new Color(.055f,.07f,.09f);camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=50;
                    var bounds=BoundsOf(model);camera.orthographicSize=i==0?1.42f:.80f;
                    var direction=i==0?new Vector3(1.7f,1.1f,-1.5f):new Vector3(-1.1f,.75f,-2);
                    camera.transform.position=bounds.center+direction.normalized*10;camera.transform.LookAt(bounds.center);
                    preview.BeginStaticPreview(new Rect(0,0,800,700));preview.Render(true);var frame=preview.EndStaticPreview();
                    atlas.SetPixels(i*800,0,800,700,frame.GetPixels());Object.DestroyImmediate(frame);
                }
                finally{preview.Cleanup();}
            }
            atlas.Apply();Directory.CreateDirectory("Logs/HospitalMobileProps");File.WriteAllBytes("Logs/HospitalMobileProps/models.png",atlas.EncodeToPNG());return "Logs/HospitalMobileProps/models.png";
        }
        finally{Object.DestroyImmediate(atlas);}
    }
}
