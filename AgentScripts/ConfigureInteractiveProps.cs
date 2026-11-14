using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ConfigureInteractiveProps
{
    const string Folder = "Assets/Models/InteractiveProps";
    static GameObject root;
    static Material dark, grey, cream, wood, metal, gold, screen, green, red, blue;
    static readonly List<Mesh> temporary = new();
    static readonly List<string> created = new();
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode before authoring props.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh(); created.Clear();
        dark = Mat("Charcoal", new Color(.055f,.065f,.075f)); grey = Mat("Grey",new Color(.35f,.39f,.42f));
        cream = Mat("Ivory",new Color(.76f,.73f,.64f)); wood = Mat("Walnut",new Color(.29f,.13f,.065f));
        metal = Mat("Chrome",new Color(.62f,.67f,.70f),.8f); gold = Mat("Gold",new Color(.79f,.49f,.12f),.8f);
        screen = Mat("Screen",new Color(.035f,.10f,.13f)); green = Mat("Signal",new Color(.15f,.78f,.38f));
        red = Mat("Red",new Color(.7f,.09f,.055f)); blue = Mat("Gem",new Color(.11f,.52f,.71f),.25f);
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            Build(stage,"Television",TV,new Vector3(0,.36f,-.245f));
            Build(stage,"Radio",Radio,new Vector3(0,.19f,-.13f));
            Build(stage,"TapeRecorder",Recorder,new Vector3(0,.14f,-.12f));
            Build(stage,"ServiceBell",Bell,new Vector3(0,.13f,0));
            Build(stage,"GameConsole",ConsoleModel,new Vector3(0,.09f,-.10f));
            Build(stage,"HeartRateMonitor",Monitor,new Vector3(0,.28f,-.17f));
            Build(stage,"Ring",Ring,new Vector3(0,.036f,0));
        }
        finally { if(root!=null)Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(stage); }
        ConfigureAlarm(); AssetDatabase.SaveAssets();
        return new { created, alarm = "PickupAlarmClock: separate interaction + pickup components", effects = "empty" };
    }
    static Material Mat(string name,Color color,float metallic=0)
    {
        string path=Folder+"/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null) { m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",metallic>0?.7f:.35f);AssetDatabase.CreateAsset(m,path); }
        return m;
    }
    static void Build(Scene stage,string name,Action model,Vector3 anchor)
    {
        string path="Assets/Prefabs/"+name+".prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)return;
        root=new GameObject(name);SceneManager.MoveGameObjectToScene(root,stage);
        try
        {
            model(); Bake(name);
            var focus=new GameObject("Interaction Anchor").transform;focus.SetParent(root.transform,false);focus.localPosition=anchor;
            var action=root.AddComponent<InteractableObject>();action.interactionAnchor=focus;action.interactionHint="交互";
            var prop=root.AddComponent<LevelProp>();prop.surface=PropSurface.Floor;prop.surfaceAnchor=Vector3.zero;prop.surfaceOffset=.005f;
            prop.gameplayMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            PrefabUtility.SaveAsPrefabAsset(root,path);created.Add(path);
        }
        finally { Object.DestroyImmediate(root);root=null;foreach(var mesh in temporary)Object.DestroyImmediate(mesh);temporary.Clear(); }
    }
    static void Bake(string name)
    {
        var parts=root.GetComponentsInChildren<MeshFilter>();
        var groups=parts.GroupBy(p=>p.GetComponent<Renderer>().sharedMaterial).ToArray();
        var submeshes=new List<CombineInstance>();
        foreach(var group in groups)
        {
            var mesh=new Mesh();temporary.Add(mesh);
            mesh.CombineMeshes(group.Select(p=>new CombineInstance{mesh=p.sharedMesh,transform=root.transform.worldToLocalMatrix*p.transform.localToWorldMatrix}).ToArray(),true,true);
            submeshes.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.identity});
        }
        var combined=new Mesh();combined.CombineMeshes(submeshes.ToArray(),false,true);
        combined=PropMeshAssets.Store(Folder,name,combined);
        foreach(var part in parts)Object.DestroyImmediate(part.gameObject);
        var model=new GameObject("Model",typeof(MeshFilter),typeof(MeshRenderer));model.transform.SetParent(root.transform,false);
        model.GetComponent<MeshFilter>().sharedMesh=combined;model.GetComponent<MeshRenderer>().sharedMaterials=groups.Select(g=>g.Key).ToArray();
    }
    static GameObject Part(string name,PrimitiveType type,Material material,Vector3 p,Vector3 scale,Quaternion rotation=default)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(root.transform,false);
        go.transform.localPosition=p;go.transform.localScale=scale;go.transform.localRotation=rotation==default?Quaternion.identity:rotation;
        go.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    static void Box(string name,Material material,Vector3 p,Vector3 size)=>Part(name,PrimitiveType.Cube,material,p,size);
    static void Cyl(string name,Material material,Vector3 p,float radius,float height,bool front=false)=>Part(name,PrimitiveType.Cylinder,material,p,new Vector3(radius*2,height*.5f,radius*2),front?Quaternion.Euler(90,0,0):Quaternion.identity);
    static void Rod(string name,Material material,Vector3 a,Vector3 b,float diameter)=>Part(name,PrimitiveType.Cylinder,material,(a+b)*.5f,new Vector3(diameter,Vector3.Distance(a,b)*.5f,diameter),Quaternion.FromToRotation(Vector3.up,b-a));
    static void Collision(Vector3 center,Vector3 size){var c=root.AddComponent<BoxCollider>();c.center=center;c.size=size;}
    static void Handle(float width,float y,float z,float height)
    {
        Rod("Handle left",dark,new Vector3(-width*.5f,y,z),new Vector3(-width*.5f,y+height,z),.026f);
        Rod("Handle top",dark,new Vector3(-width*.5f,y+height,z),new Vector3(width*.5f,y+height,z),.026f);
        Rod("Handle right",dark,new Vector3(width*.5f,y+height,z),new Vector3(width*.5f,y,z),.026f);
    }
    static void Lathe(string name,Material material,Vector2[] profile,int segments=48,Vector3 position=default)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        Vector3 P(Vector2 v,float a)=>new Vector3(v.x*Mathf.Cos(a),v.y,v.x*Mathf.Sin(a));
        for(int j=0;j<profile.Length;j++)for(int i=0;i<segments;i++)
        {
            int n=vertices.Count;float a=i*2*Mathf.PI/segments,b=(i+1)*2*Mathf.PI/segments;int next=(j+1)%profile.Length;
            vertices.AddRange(new[]{P(profile[j],a),P(profile[next],a),P(profile[next],b),P(profile[j],b)});
            triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();temporary.Add(mesh);
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.transform.localPosition=position;
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<Renderer>().sharedMaterial=material;
    }
    static void TV()
    {
        Box("Cabinet",dark,new Vector3(0,.36f,0),new Vector3(.9f,.62f,.46f));
        Box("Rear housing",grey,new Vector3(0,.36f,.25f),new Vector3(.68f,.45f,.14f));
        Box("Screen bezel",grey,new Vector3(-.075f,.37f,-.239f),new Vector3(.68f,.49f,.025f));
        Part("Curved CRT",PrimitiveType.Sphere,screen,new Vector3(-.075f,.37f,-.24f),new Vector3(.65f,.45f,.065f));
        Box("Lower trim",metal,new Vector3(0,.095f,-.238f),new Vector3(.78f,.016f,.012f));
        for(int i=0;i<2;i++)Cyl("Tuning dial",metal,new Vector3(.36f,.49f-i*.15f,-.255f),.045f,.035f,true);
        for(int i=0;i<6;i++)Box("Speaker slit",grey,new Vector3(.36f,.19f+i*.018f,-.238f),new Vector3(.10f,.006f,.009f));
        for(int x=-1;x<=1;x+=2)Box("Foot",dark,new Vector3(x*.32f,.025f,0),new Vector3(.10f,.05f,.32f));
        Cyl("Antenna base",grey,new Vector3(0,.69f,.07f),.055f,.04f);
        Rod("Antenna left",metal,new Vector3(0,.71f,.07f),new Vector3(-.24f,1.00f,.08f),.009f);
        Rod("Antenna right",metal,new Vector3(0,.71f,.07f),new Vector3(.24f,1.00f,.08f),.009f);
        Collision(new Vector3(0,.34f,.02f),new Vector3(.9f,.68f,.5f));
    }
    static void Radio()
    {
        Box("Wood cabinet",wood,new Vector3(0,.18f,0),new Vector3(.54f,.32f,.24f));
        Box("Faceplate",cream,new Vector3(0,.18f,-.124f),new Vector3(.50f,.28f,.015f));
        Box("Speaker cloth",dark,new Vector3(-.12f,.18f,-.135f),new Vector3(.24f,.23f,.01f));
        for(int i=0;i<10;i++)Box("Speaker grille",metal,new Vector3(-.12f,.077f+i*.023f,-.144f),new Vector3(.22f,.005f,.006f));
        Box("Tuner window",screen,new Vector3(.125f,.265f,-.137f),new Vector3(.21f,.055f,.009f));
        for(int i=0;i<10;i++)Box("Frequency mark",cream,new Vector3(.033f+i*.02f,.265f,-.143f),new Vector3(.003f,i%2==0?.032f:.018f,.004f));
        Box("Tuning needle",red,new Vector3(.15f,.265f,-.148f),new Vector3(.004f,.045f,.004f));
        Cyl("Tuner",dark,new Vector3(.13f,.14f,-.15f),.06f,.035f,true);Cyl("Dial inset",metal,new Vector3(.13f,.14f,-.17f),.04f,.007f,true);
        Handle(.31f,.34f,0,.08f);Rod("Antenna",metal,new Vector3(.22f,.34f,.06f),new Vector3(.30f,.65f,.06f),.008f);
        for(int x=-1;x<=1;x+=2)Box("Foot",dark,new Vector3(x*.19f,.01f,0),new Vector3(.07f,.02f,.15f));
        Collision(new Vector3(0,.17f,0),new Vector3(.54f,.34f,.24f));
    }
    static void Recorder()
    {
        Box("Case",grey,new Vector3(0,.15f,0),new Vector3(.60f,.28f,.23f));
        Box("Cassette door",dark,new Vector3(-.07f,.16f,-.122f),new Vector3(.30f,.19f,.02f));
        Box("Tape label",cream,new Vector3(-.07f,.21f,-.137f),new Vector3(.25f,.055f,.006f));
        Box("Tape window",screen,new Vector3(-.07f,.13f,-.137f),new Vector3(.25f,.075f,.007f));
        for(int i=-1;i<=1;i+=2){Cyl("Tape spool",metal,new Vector3(-.07f+i*.075f,.132f,-.145f),.03f,.008f,true);Cyl("Spool hub",dark,new Vector3(-.07f+i*.075f,.132f,-.151f),.012f,.008f,true);}
        for(int i=0;i<9;i++)Box("Speaker grille",dark,new Vector3(.215f,.066f+i*.022f,-.12f),new Vector3(.105f,.006f,.008f));
        for(int i=0;i<5;i++)Box("Transport key",i==0?red:cream,new Vector3(-.19f+i*.07f,.297f,-.04f),new Vector3(.055f,.025f,.065f));
        Handle(.38f,.29f,.06f,.09f);Collision(new Vector3(0,.15f,0),new Vector3(.60f,.30f,.23f));
    }
    static void Bell()
    {
        Cyl("Rubber base",dark,new Vector3(0,.014f,0),.145f,.028f);
        Lathe("Bell dome",metal,new[]{new Vector2(.14f,.03f),new Vector2(.14f,.045f),new Vector2(.13f,.085f),new Vector2(.10f,.13f),new Vector2(.055f,.16f),new Vector2(.025f,.165f),new Vector2(.025f,.15f),new Vector2(.09f,.12f),new Vector2(.12f,.07f),new Vector2(.125f,.03f)});
        Cyl("Plunger stem",gold,new Vector3(0,.18f,0),.012f,.055f);Cyl("Push button",metal,new Vector3(0,.21f,0),.04f,.018f);
        Collision(new Vector3(0,.1f,0),new Vector3(.28f,.2f,.28f));
    }
    static void ConsoleModel()
    {
        Box("Console base",dark,new Vector3(0,.035f,.07f),new Vector3(.42f,.07f,.30f));
        Box("Console shell",cream,new Vector3(0,.095f,.07f),new Vector3(.42f,.07f,.30f));
        Box("Cartridge bay",grey,new Vector3(.035f,.135f,.08f),new Vector3(.22f,.013f,.17f));
        Box("Cartridge slot",dark,new Vector3(.035f,.144f,.09f),new Vector3(.18f,.008f,.025f));
        for(int i=0;i<7;i++)Box("Vent",dark,new Vector3(-.155f,.132f,-.025f+i*.031f),new Vector3(.055f,.004f,.006f));
        Box("Power",red,new Vector3(-.12f,.086f,-.085f),new Vector3(.06f,.025f,.018f));Box("Reset",grey,new Vector3(-.04f,.086f,-.085f),new Vector3(.06f,.025f,.018f));
        Box("Controller",grey,new Vector3(.04f,.025f,-.23f),new Vector3(.26f,.05f,.115f));
        Box("Controller inset",dark,new Vector3(.04f,.053f,-.23f),new Vector3(.24f,.006f,.095f));
        Box("Dpad X",cream,new Vector3(-.035f,.062f,-.23f),new Vector3(.065f,.012f,.023f));Box("Dpad Z",cream,new Vector3(-.035f,.062f,-.23f),new Vector3(.023f,.012f,.065f));
        for(int i=0;i<2;i++)Cyl("Action button",red,new Vector3(.105f+i*.035f,.062f,-.23f),.013f,.012f);
        Rod("Controller cable",dark,new Vector3(.04f,.022f,-.17f),new Vector3(.04f,.022f,-.08f),.008f);
        Collision(new Vector3(0,.065f,.07f),new Vector3(.42f,.13f,.30f));Collision(new Vector3(.04f,.03f,-.23f),new Vector3(.26f,.06f,.115f));
    }
    static void Monitor()
    {
        Box("Monitor body",cream,new Vector3(0,.25f,0),new Vector3(.52f,.44f,.30f));
        Box("Screen bezel",grey,new Vector3(-.045f,.28f,-.157f),new Vector3(.38f,.30f,.018f));
        Box("Display",screen,new Vector3(-.045f,.28f,-.17f),new Vector3(.34f,.26f,.009f));
        var wave=new[]{new Vector3(-.20f,.27f,-.178f),new Vector3(-.15f,.27f,-.178f),new Vector3(-.12f,.29f,-.178f),new Vector3(-.09f,.24f,-.178f),new Vector3(-.065f,.36f,-.178f),new Vector3(-.04f,.20f,-.178f),new Vector3(-.01f,.27f,-.178f),new Vector3(.10f,.27f,-.178f)};
        for(int i=1;i<wave.Length;i++)Rod("ECG trace",green,wave[i-1],wave[i],.004f);
        for(int row=0;row<2;row++)for(int i=0;i<4;i++)Box("Readout segment",green,new Vector3(-.16f+i*.06f,.18f+row*.20f,-.178f),new Vector3(.025f,.006f,.004f));
        for(int i=0;i<3;i++)Cyl("Control",i==2?red:grey,new Vector3(.205f,.18f+i*.10f,-.16f),.023f,.022f,true);
        Handle(.25f,.47f,.015f,.06f);
        for(int x=-1;x<=1;x+=2)Box("Foot",grey,new Vector3(x*.17f,.015f,0),new Vector3(.07f,.03f,.23f));
        Rod("Sensor lead",dark,new Vector3(.26f,.12f,0),new Vector3(.36f,.015f,-.03f),.008f);
        Rod("Sensor lead end",dark,new Vector3(.36f,.015f,-.03f),new Vector3(.34f,.015f,-.16f),.008f);
        Box("Finger sensor",grey,new Vector3(.34f,.026f,-.19f),new Vector3(.045f,.052f,.075f));
        Collision(new Vector3(0,.235f,0),new Vector3(.52f,.47f,.30f));
    }
    static void Ring()
    {
        // Oversized display ring keeps its silhouette legible in the point cloud.
        Lathe("Band",gold,new[]{new Vector2(.036f,0),new Vector2(.042f,.004f),new Vector2(.042f,.018f),new Vector2(.036f,.023f),new Vector2(.029f,.018f),new Vector2(.029f,.004f)},48);
        var verts=new List<Vector3>();var tris=new List<int>();
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;int n=verts.Count;
            var p=new Vector3(Mathf.Cos(a)*.023f,.039f,Mathf.Sin(a)*.023f-.033f);
            var q=new Vector3(Mathf.Cos(b)*.023f,.039f,Mathf.Sin(b)*.023f-.033f);
            verts.AddRange(new[]{new Vector3(0,.055f,-.033f),q,p,new Vector3(0,.024f,-.033f),p,q});tris.AddRange(new[]{n,n+1,n+2,n+3,n+4,n+5});
        }
        var gem=new Mesh();gem.SetVertices(verts);gem.SetTriangles(tris,0);gem.RecalculateNormals();temporary.Add(gem);
        var go=new GameObject("Faceted stone",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.GetComponent<MeshFilter>().sharedMesh=gem;go.GetComponent<Renderer>().sharedMaterial=blue;
        for(int i=-1;i<=1;i+=2)Rod("Prong",gold,new Vector3(i*.018f,.018f,-.033f),new Vector3(i*.018f,.042f,-.033f),.004f);
        Collision(new Vector3(0,.0275f,-.006f),new Vector3(.084f,.055f,.096f));
    }
    static void ConfigureAlarm()
    {
        const string path="Assets/Prefabs/PickupAlarmClock.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var action=prefab.GetComponents<InteractableObject>().FirstOrDefault(c=>!(c is PickupItem));
            if(action==null)action=prefab.AddComponent<InteractableObject>();
            action.interactionAnchor=prefab.GetComponent<PickupItem>().interactionAnchor;action.interactionHint="交互";
            PrefabUtility.SaveAsPrefabAsset(prefab,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
    }
}
