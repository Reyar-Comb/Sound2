using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ConfigureHospitalMobileProps
{
    const string Folder="Assets/Models/HospitalMobileProps";
    static GameObject root;
    static Material steel,dark,white,blue,teal;
    static readonly List<Mesh> temporary=new();
    public static object Main()=>Build(false);
    public static object Rebuild()=>Build(true);
    static object Build(bool replace)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play Mode before authoring.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        Material Existing(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/HouseholdProps/"+name+".mat")??throw new Exception(name);
        steel=Existing("Steel");dark=Existing("Charcoal");white=Existing("Enamel");blue=Existing("Blue");
        string materialPath=Folder+"/MedicalTeal.mat";teal=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(teal==null){teal=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="MedicalTeal",color=new Color(.22f,.48f,.47f)};teal.SetFloat("_Smoothness",.22f);AssetDatabase.CreateAsset(teal,materialPath);}
        var scene=EditorSceneManager.NewPreviewScene();var created=new List<string>();
        try
        {
            foreach(string name in new[]{"MobileHospitalBed","MedicalCart"})
            {
                string path="Assets/Prefabs/"+name+".prefab";
                if(!replace&&AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)continue;
                root=new GameObject(name);SceneManager.MoveGameObjectToScene(root,scene);
                try
                {
                    if(name=="MobileHospitalBed")Bed();else Cart();
                    Bake(name);
                    var prop=root.AddComponent<LevelProp>();prop.surface=PropSurface.Floor;prop.surfaceAnchor=Vector3.zero;prop.surfaceOffset=.01f;
                    prop.gameplayMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
                    ConfigurePush(name=="MobileHospitalBed");
                    PrefabUtility.SaveAsPrefabAsset(root,path);created.Add(path);
                }
                finally{Object.DestroyImmediate(root);foreach(var mesh in temporary)if(mesh!=null&&!AssetDatabase.Contains(mesh))Object.DestroyImmediate(mesh);temporary.Clear();}
            }
            AssetDatabase.SaveAssets();return new{created,interaction="Walk into the prop, same as ShoppingCart"};
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
    static GameObject Part(string name,PrimitiveType type,Material material,Vector3 position,Vector3 scale,Quaternion rotation=default)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(root.transform,false);
        go.transform.localPosition=position;go.transform.localScale=scale;go.transform.localRotation=rotation==default?Quaternion.identity:rotation;
        go.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    static void Box(string name,Material m,Vector3 p,Vector3 size)=>Part(name,PrimitiveType.Cube,m,p,size);
    static void Ball(string name,Material m,Vector3 p,Vector3 size,Quaternion q=default)=>Part(name,PrimitiveType.Sphere,m,p,size,q);
    static void Rod(string name,Material m,Vector3 a,Vector3 b,float diameter)=>Part(name,PrimitiveType.Cylinder,m,(a+b)*.5f,new Vector3(diameter,(b-a).magnitude*.5f,diameter),Quaternion.FromToRotation(Vector3.up,b-a));
    static void Collision(string name,Vector3 p,Vector3 size)
    {
        var go=new GameObject(name,typeof(BoxCollider));go.transform.SetParent(root.transform,false);go.transform.localPosition=p;go.GetComponent<BoxCollider>().size=size;
    }
    static void Custom(string name,Material material,List<Vector3> vertices,List<int> indices)
    {
        var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();temporary.Add(mesh);
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<Renderer>().sharedMaterial=material;
    }
    static void Soft(string name,Material material,Vector3 center,Vector3 size,float bevel)
    {
        float b=Mathf.Min(bevel,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.4f);var v=new List<Vector3>();var t=new List<int>();
        Vector3[] Ring(float y,float inset){float x=size.x*.5f-inset,z=size.z*.5f-inset,c=Mathf.Min(b,Mathf.Min(x,z)*.4f);return new[]{new Vector3(-x+c,y,-z),new Vector3(x-c,y,-z),new Vector3(x,y,-z+c),new Vector3(x,y,z-c),new Vector3(x-c,y,z),new Vector3(-x+c,y,z),new Vector3(-x,y,z-c),new Vector3(-x,y,-z+c)};}
        void Quad(Vector3 a,Vector3 b0,Vector3 c,Vector3 d){int n=v.Count;v.AddRange(new[]{a+center,b0+center,c+center,d+center});t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
        var rings=new[]{Ring(-size.y*.5f,b*.45f),Ring(-size.y*.5f+b,0),Ring(size.y*.5f-b,0),Ring(size.y*.5f,b*.45f)};
        for(int j=0;j<3;j++)for(int i=0;i<8;i++)Quad(rings[j][i],rings[j+1][i],rings[j+1][(i+1)%8],rings[j][(i+1)%8]);
        for(int i=1;i<7;i++){int n=v.Count;v.AddRange(new[]{rings[0][0]+center,rings[0][i]+center,rings[0][i+1]+center,rings[3][0]+center,rings[3][i+1]+center,rings[3][i]+center});t.AddRange(new[]{n,n+1,n+2,n+3,n+4,n+5});}Custom(name,material,v,t);
    }
    static void Wheel(float x,float z,float radius)
    {
        var center=new Vector3(x,radius,z);
        Part("Rubber caster",PrimitiveType.Cylinder,dark,center,new Vector3(radius*2,.035f,radius*2),Quaternion.Euler(0,0,90));
        foreach(int sign in new[]{-1,1})
        {
            Part("Wheel hub",PrimitiveType.Cylinder,steel,center+Vector3.right*(sign*.037f),new Vector3(radius*1.1f,.007f,radius*1.1f),Quaternion.Euler(0,0,90));
            Box("Swivel fork",steel,center+new Vector3(sign*.049f,radius*.65f,.012f),new Vector3(.012f,radius*1.2f,.055f));
        }
        Rod("Caster swivel",steel,new Vector3(x,radius*1.8f,z+.012f),new Vector3(x,radius*2.5f,z+.012f),.043f);
        Soft("Brake pedal",dark,center+new Vector3(0,radius*.6f,-radius),new Vector3(.064f,.025f,.07f),.006f);
        // A smooth compound contact, matching cart-style sliding rather than simulated wheel joints.
        var go=new GameObject("Caster contact",typeof(SphereCollider));go.transform.SetParent(root.transform,false);go.transform.localPosition=center;go.GetComponent<SphereCollider>().radius=radius;
    }
    static void Bed()
    {
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Wheel(x*.36f,z*.84f,.09f);
        Soft("Lower mobile frame",white,new Vector3(0,.27f,0),new Vector3(.79f,.075f,1.86f),.018f);
        Soft("Equipment shelf",steel,new Vector3(0,.315f,0),new Vector3(.61f,.025f,1.32f),.009f);
        foreach(int x in new[]{-1,1})
        {
            Rod("Scissor support",steel,new Vector3(x*.27f,.30f,-.66f),new Vector3(x*.27f,.71f,.62f),.055f);
            Rod("Scissor support",steel,new Vector3(x*.27f,.30f,.66f),new Vector3(x*.27f,.71f,-.62f),.055f);
            Part("Scissor pivot",PrimitiveType.Cylinder,blue,new Vector3(x*.30f,.505f,0),new Vector3(.085f,.015f,.085f),Quaternion.Euler(0,0,90));
        }
        Soft("Bed deck",white,new Vector3(0,.735f,0),new Vector3(.91f,.075f,2.08f),.025f);
        Soft("Mattress piping",blue,new Vector3(0,.792f,0),new Vector3(.83f,.045f,1.99f),.018f);
        Soft("Mattress",teal,new Vector3(0,.855f,0),new Vector3(.81f,.11f,1.97f),.035f);
        Soft("Pillow",white,new Vector3(0,.95f,.69f),new Vector3(.60f,.085f,.37f),.033f);
        for(int side=-1;side<=1;side+=2)
        {
            float x=side*.47f;
            Rod("Upper safety rail",steel,new Vector3(x,1.115f,-.76f),new Vector3(x,1.115f,.76f),.028f);
            Rod("Lower safety rail",steel,new Vector3(x,.85f,-.76f),new Vector3(x,.85f,.76f),.024f);
            foreach(float z in new[]{-.76f,-.38f,0,.38f,.76f})Rod("Rail upright",steel,new Vector3(x,.77f,z),new Vector3(x,1.115f,z),.024f);
            Soft("Rail latch",blue,new Vector3(x,.79f,-.8f),new Vector3(.07f,.07f,.10f),.012f);
            Collision("Safety rail collision",new Vector3(x,.96f,0),new Vector3(.045f,.34f,1.56f));
            foreach(int end in new[]{-1,1})
            {
                Rod("End handle upright",steel,new Vector3(side*.34f,.73f,end*1.06f),new Vector3(side*.34f,1.02f,end*1.06f),.029f);
                Soft("Corner bumper",dark,new Vector3(side*.43f,.735f,end*1.025f),new Vector3(.12f,.08f,.13f),.025f);
            }
        }
        foreach(int end in new[]{-1,1})
        {
            Rod("End push handle",steel,new Vector3(-.34f,1.02f,end*1.06f),new Vector3(.34f,1.02f,end*1.06f),.032f);
            Rod("Hand grip",blue,new Vector3(-.23f,1.02f,end*1.06f),new Vector3(.23f,1.02f,end*1.06f),.041f);
            Collision("End handle collision",new Vector3(0,.92f,end*1.06f),new Vector3(.72f,.25f,.045f));
        }
        Box("Height pedal",dark,new Vector3(.38f,.35f,-.45f),new Vector3(.22f,.035f,.12f));
        Collision("Mattress and deck",new Vector3(0,.814f,0),new Vector3(.91f,.193f,2.08f));
        Collision("Lower frame collision",new Vector3(0,.29f,0),new Vector3(.79f,.10f,1.86f));
        Collision("Support collision",new Vector3(0,.50f,0),new Vector3(.58f,.40f,1.32f));
    }
    static void Cart()
    {
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
        {
            Wheel(x*.30f,z*.235f,.065f);
            Rod("Corner column",steel,new Vector3(x*.30f,.16f,z*.235f),new Vector3(x*.30f,.98f,z*.235f),.028f);
        }
        Soft("Bottom tray",white,new Vector3(0,.245f,0),new Vector3(.67f,.035f,.53f),.012f);
        Collision("Lower tray collision",new Vector3(0,.245f,0),new Vector3(.67f,.035f,.53f));
        foreach(int s in new[]{-1,1})
        {
            Box("Lower tray rim",steel,new Vector3(s*.322f,.29f,0),new Vector3(.018f,.075f,.53f));
            Box("Lower tray rim",steel,new Vector3(0,.29f,s*.253f),new Vector3(.64f,.075f,.018f));
        }
        Soft("Drawer cabinet",white,new Vector3(0,.71f,0),new Vector3(.62f,.36f,.48f),.014f);
        for(int i=0;i<2;i++)
        {
            float y=.62f+i*.17f;
            Soft("Drawer face",teal,new Vector3(0,y,-.25f),new Vector3(.57f,.145f,.025f),.008f);
            foreach(int s in new[]{-1,1})Rod("Handle standoff",steel,new Vector3(s*.10f,y,-.266f),new Vector3(s*.10f,y,-.30f),.012f);
            Rod("Drawer handle",steel,new Vector3(-.10f,y,-.30f),new Vector3(.10f,y,-.30f),.016f);
            Box("Drawer label",white,new Vector3(.215f,y+.025f,-.264f),new Vector3(.065f,.025f,.002f));
        }
        Soft("Work tray",white,new Vector3(0,.935f,0),new Vector3(.71f,.045f,.57f),.014f);
        Soft("Tray insert",steel,new Vector3(0,.959f,0),new Vector3(.64f,.003f,.50f),.001f);
        foreach(int x in new[]{-1,1})
        {
            Rod("Tray guardrail",steel,new Vector3(x*.325f,1.035f,-.24f),new Vector3(x*.325f,1.035f,.24f),.014f);
            foreach(int z in new[]{-1,1})Rod("Tray rail post",steel,new Vector3(x*.325f,.96f,z*.24f),new Vector3(x*.325f,1.035f,z*.24f),.014f);
        }
        Rod("Rear tray guardrail",steel,new Vector3(-.325f,1.035f,.24f),new Vector3(.325f,1.035f,.24f),.014f);
        foreach(int z in new[]{-1,1})Rod("Push handle arm",steel,new Vector3(-.31f,.9f,z*.19f),new Vector3(-.46f,.99f,z*.19f),.025f);
        Rod("Push handle",blue,new Vector3(-.46f,.99f,-.19f),new Vector3(-.46f,.99f,.19f),.035f);
        Soft("Side utility bin",white,new Vector3(.407f,.60f,0),new Vector3(.17f,.27f,.28f),.025f);
        Soft("Bin lid",blue,new Vector3(.407f,.74f,0),new Vector3(.18f,.025f,.29f),.009f);
        Box("Medical badge vertical",blue,new Vector3(0,.78f,.241f),new Vector3(.035f,.12f,.002f));
        Box("Medical badge horizontal",blue,new Vector3(0,.78f,.242f),new Vector3(.12f,.035f,.002f));
        Collision("Cabinet collision",new Vector3(0,.71f,-.01f),new Vector3(.63f,.36f,.51f));
        Collision("Work surface collision",new Vector3(0,.936f,0),new Vector3(.71f,.049f,.57f));
        Collision("Utility bin collision",new Vector3(.407f,.61f,0),new Vector3(.18f,.29f,.29f));
        Collision("Push handle collision",new Vector3(-.46f,.99f,0),new Vector3(.04f,.04f,.41f));
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Collision("Column collision",new Vector3(x*.30f,.56f,z*.235f),new Vector3(.028f,.80f,.028f));
    }
    static void ConfigurePush(bool bed)
    {
        var cart=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ShoppingCart.prefab");
        var push=root.AddComponent<PushableProp>();EditorUtility.CopySerialized(cart.GetComponent<PushableProp>(),push);
        push.maximumPushSpeed=bed?1.4f:1.8f;push.maximumPushForce=bed?350:155;
        var body=root.GetComponent<Rigidbody>();body.mass=bed?45:20;body.useGravity=true;body.isKinematic=false;
        body.linearDamping=.05f;body.angularDamping=5;body.maxAngularVelocity=2;body.interpolation=RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.constraints=RigidbodyConstraints.FreezeRotationX|RigidbodyConstraints.FreezeRotationZ;
        body.centerOfMass=new Vector3(0,bed?.38f:.33f,0);
        var physics=cart.GetComponentsInChildren<Collider>().First(c=>c.sharedMaterial!=null).sharedMaterial;
        foreach(var c in root.GetComponentsInChildren<Collider>())c.sharedMaterial=physics;
        var origin=new GameObject("Sound Origin").transform;origin.SetParent(root.transform,false);origin.localPosition=new Vector3(0,.15f,0);
        var sonar=root.GetComponent<SonarObjectEmitter>();sonar.automatic=false;sonar.toggleKey=Key.None;sonar.emissionOrigin=origin;
        sonar.range=6;sonar.soundRange=14;sonar.waveSpeed=7;sonar.afterglow=.6f;
        var cue=root.GetComponent<SonarCueSource>();if(cue==null)cue=root.AddComponent<SonarCueSource>();
        cue.generateDefaultPing=false;cue.output=root.GetComponent<AudioSource>();cue.output.playOnAwake=false;cue.output.spatialBlend=1;
        var impact=root.GetComponent<CollisionSonarEmitter>();EditorUtility.CopySerialized(cart.GetComponent<CollisionSonarEmitter>(),impact);
    }
    static void Bake(string name)
    {
        var filters=root.GetComponentsInChildren<MeshFilter>();var groups=filters.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial).ToArray();var combined=new List<CombineInstance>();
        foreach(var group in groups)
        {
            var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};temporary.Add(mesh);
            mesh.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=root.transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray());
            combined.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.identity});
        }
        var result=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};result.CombineMeshes(combined.ToArray(),false);result=PropMeshAssets.Store(Folder,name,result);
        foreach(var filter in filters)Object.DestroyImmediate(filter.gameObject);
        var model=new GameObject("Model",typeof(MeshFilter),typeof(MeshRenderer));model.transform.SetParent(root.transform,false);
        model.GetComponent<MeshFilter>().sharedMesh=result;model.GetComponent<Renderer>().sharedMaterials=groups.Select(g=>g.Key).ToArray();
    }
}
