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
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

public static class VerifyHouseholdProps
{
    static readonly string[] Furniture={"Chair","ShoppingCart","LibraryBookshelf","WritingDesk","Computer","Refrigerator","Stove","TrashBin"};
    static readonly string[] Small={"PickupWaterCup","PickupDeskLamp","PickupCoffeeMug","PickupCeramicVase","PickupGlassMug"};
    static readonly List<string> checks=new();
    static GameObject Prefab(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+name+".prefab");
    static void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
    public static object Edit()
    {
        checks.Clear();
        foreach(string name in Furniture.Concat(Small))
        {
            var go=Prefab(name);Check(go!=null,name+": prefab exists");
            Check(go.GetComponent<LevelProp>()?.gameplayMaterial!=null,name+": runtime presentation configured");
            Check(go.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterial!=null)&&go.GetComponentsInChildren<Collider>().Length>0,name+": visible geometry and colliders");
            Check((go.GetComponent<PickupItem>()!=null)==Small.Contains(name),name+": correct pickup permission");
            if(Small.Contains(name))
            {
                var item=go.GetComponent<PickupItem>();bool breaks=name!="PickupWaterCup"&&name!="PickupDeskLamp";
                Check(item.breakable==breaks&&item.interactionAnchor!=null,name+": correct breakage and focus configuration");
                Check(!go.GetComponent<SonarCueSource>().generateDefaultPing&&go.GetComponent<SonarCueSource>().clip==null,name+": empty recording interface without synthesis");
                if(breaks)Check(item.brokenPrefab!=null&&item.brokenPrefab.GetComponentsInChildren<Rigidbody>().Length>5&&item.brokenPrefab.GetComponentsInChildren<InteractableObject>().Length==0,name+": physical, non-pickup remnants");
            }
        }
        Check(Prefab("PickupCoffeeMug").GetComponentInChildren<MeshFilter>().sharedMesh==AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/BreakableProps/CoffeeMug.asset"),"Coffee mug reuses original handled cup mesh");
        Check(Prefab("PickupGlassMug").GetComponentInChildren<MeshFilter>().sharedMesh.name=="WineGlass","Existing glass prefab now contains stemmed glass");
        var window=Resources.FindObjectsOfTypeAll<LevelDesignerWindow>().FirstOrDefault();
        if(window!=null)
        {
            window.CreateGUI();var dropdown=window.rootVisualElement.Q<PopupField<GameObject>>("prop-choice");
            if(dropdown!=null)Check(Furniture.Concat(Small).All(n=>dropdown.choices.Contains(Prefab(n))),"Editor dropdown contains all new props");
        }
        VerifySurface();
        Capture(Furniture,"furniture.png",4);Capture(Small,"small-props.png",3);
        return new{passed=true,count=checks.Count,checks};
    }
    static void VerifySurface()
    {
        var stage=EditorSceneManager.NewPreviewScene();GameObject layoutRoot=null;
        try
        {
            layoutRoot=new GameObject("Placement verification");SceneManager.MoveGameObjectToScene(layoutRoot,stage);
            var layout=layoutRoot.AddComponent<LevelLayout>();
            var geometry=new GameObject("Generated Geometry");geometry.transform.SetParent(layoutRoot.transform,false);layout.generatedRoot=geometry;
            var props=new GameObject("Props");props.transform.SetParent(layoutRoot.transform,false);
            var desk=(GameObject)PrefabUtility.InstantiatePrefab(Prefab("WritingDesk"),stage);desk.transform.SetParent(props.transform,false);desk.transform.localPosition=new Vector3(5000,0,5000);
            Physics.SyncTransforms();
            Check(LevelPropPlacement.TryPose(layout,Prefab("PickupWaterCup"),new Ray(desk.transform.position+Vector3.up*3,Vector3.down),PropSurface.Floor,.01f,0,out var pose)&&Mathf.Abs(pose.position.y-.81f)<.005f,"Cup placement snaps onto desk surface");
            var placement=LevelPropPlacement.Place(layout,Prefab("PickupWaterCup"),pose);
            Check(PrefabUtility.GetCorrespondingObjectFromSource(placement)==Prefab("PickupWaterCup"),"Tabletop placement preserves prefab connection");
        }
        finally{if(layoutRoot!=null)Object.DestroyImmediate(layoutRoot);EditorSceneManager.ClosePreviewScene(stage);}
    }
    static void Capture(string[] names,string file,int columns)
    {
        var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        const int width=440,height=400;int rows=(names.Length+columns-1)/columns;
        var atlas=new Texture2D(width*columns,height*rows,TextureFormat.RGB24,false);
        var pixels=Enumerable.Repeat(new Color(.07f,.085f,.10f),atlas.width*atlas.height).ToArray();atlas.SetPixels(pixels);
        var rt=new RenderTexture(width,height,24);var frame=new Texture2D(width,height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try
        {
            SceneManager.SetActiveScene(scene);Vector3 centre=new Vector3(6000,0,6000);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=centre+Vector3.down*.025f;floor.transform.localScale=new Vector3(15,.05f,15);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Floor.mat");
            var light=new GameObject("Preview light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=2.2f;light.transform.rotation=Quaternion.Euler(45,-30,0);
            var camera=new GameObject("Preview camera",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.085f,.10f);camera.fieldOfView=34;camera.targetTexture=rt;
            for(int i=0;i<names.Length;i++)
            {
                var instance=Object.Instantiate(Prefab(names[i]),centre,Quaternion.identity);
                var renderers=instance.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
                camera.transform.position=bounds.center+new Vector3(1.2f,.85f,-1.8f).normalized*size*2.7f;camera.transform.LookAt(bounds.center);
                camera.nearClipPlane=.01f;camera.farClipPlane=30;camera.Render();RenderTexture.active=rt;
                frame.ReadPixels(new Rect(0,0,width,height),0,0);frame.Apply();atlas.SetPixels(i%columns*width,(rows-1-i/columns)*height,width,height,frame.GetPixels());
                Object.DestroyImmediate(instance);
            }
            atlas.Apply();Directory.CreateDirectory("Logs/HouseholdProps");File.WriteAllBytes("Logs/HouseholdProps/"+file,atlas.EncodeToPNG());
        }
        finally {RenderTexture.active=previous;Object.DestroyImmediate(rt);Object.DestroyImmediate(frame);Object.DestroyImmediate(atlas);SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);}
    }
    static async Task Wait(int milliseconds)
    {int frame=Time.frameCount;await Task.Delay(milliseconds);Check(Time.frameCount>frame,"Player loop advances");}
    public static async Task<object> Play()
    {
        checks.Clear();Check(Application.isPlaying,"Play mode active");
        var fixture=new GameObject("Household props verification");
        try
        {
            Vector3 centre=new Vector3(4000,0,4000);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(fixture.transform);floor.transform.position=centre+Vector3.down*.5f;floor.transform.localScale=new Vector3(40,1,40);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(fixture.transform);wall.transform.position=centre+new Vector3(0,2,4);wall.transform.localScale=new Vector3(30,4,.2f);
            var player=Object.Instantiate(Prefab("Player"),centre+Vector3.up,Quaternion.identity,fixture.transform);
            var controller=player.GetComponent<PlayerController>();controller.InputSuspended=true;
            var pickup=player.GetComponent<PlayerPickup>();
            await Wait(100);
            foreach(string name in Small)
            {
                var instance=Object.Instantiate(Prefab(name),centre+new Vector3(0,1.2f,1),Quaternion.identity,fixture.transform);
                var item=instance.GetComponent<PickupItem>();var body=instance.GetComponent<Rigidbody>();body.useGravity=false;
                Physics.SyncTransforms();Check(pickup.TryPickUp(item),name+": player can pick up");
                Check(instance.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger).All(c=>!c.enabled),name+": held colliders disabled");
                Check(instance.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterial==instance.GetComponent<LevelProp>().gameplayMaterial),name+": runtime black material");
                item.Release(centre+new Vector3(0,1.3f,2.5f),Vector3.forward*9,0);
                bool breaks=item.breakable;var remainsPrefab=item.brokenPrefab;
                await Wait(450);
                if(breaks)
                {
                    Check(item==null||item.IsBroken,name+": physical impact breaks object");
                    var remains=fixture.transform.Find(remainsPrefab.name+"(Clone)");
                    Check(remains!=null&&remains.GetComponentsInChildren<PickupItem>().Length==0,name+": remnants cannot be picked up");
                    Object.Destroy(remains.gameObject);
                }
                else
                {Check(item!=null&&!item.IsBroken&&item.GetComponent<CollisionSonarEmitter>().Emissions>0,name+": survives impact and emits collision pulse");Object.Destroy(instance);}
                await Wait(80);
            }
            // Gentle settling must not count as breaking, even for the narrow wineglass foot.
            for(int i=0;i<3;i++)
            {
                string name=Small[i+2];var item=Object.Instantiate(Prefab(name),centre+new Vector3(3+i, .04f,0),Quaternion.identity,fixture.transform).GetComponent<PickupItem>();
                await Wait(400);Check(item!=null&&!item.IsBroken,name+": gentle floor contact does not break");
            }
            return new{passed=true,count=checks.Count,checks};
        }
        finally {Object.Destroy(fixture);}
    }
}
