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

public static class ConfigureDisplayProps
{
    const string Folder = "Assets/Models/DisplayProps";
    public static readonly string[] Names = { "EmptyDoubleShelf", "ShelfBlock", "Earmuffs", "TeddyBear" };
    static GameObject root;
    static Material wood, steel, dark, blue, cream, fur, muzzle, thread, red;
    static readonly List<Mesh> temporary = new();
    static float shelfHeight;
    public static Bounds BoundsOf(GameObject source)
    {
        bool found = false; Bounds result = default;
        foreach (var f in source.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null) continue;
            var b = f.sharedMesh.bounds; var m = source.transform.worldToLocalMatrix * f.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                if (!found) { result = new Bounds(p, Vector3.zero); found = true; } else result.Encapsulate(p);
            }
        }
        return result;
    }
    public static object Main() => BuildAll(false);
    public static object Rebuild() => BuildAll(true);
    static object BuildAll(bool replace)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/HouseholdProps/"+name+".mat") ?? throw new Exception(name);
        wood=Existing("Wood"); steel=Existing("Steel"); dark=Existing("Charcoal"); blue=Existing("Blue"); red=Existing("Red");
        cream=Mat("WarmCloth",new Color(.77f,.66f,.47f)); fur=Mat("TeddyFur",new Color(.43f,.24f,.105f));
        muzzle=Mat("TeddyMuzzle",new Color(.69f,.47f,.25f)); thread=Mat("Stitch",new Color(.22f,.12f,.065f));
        shelfHeight=BoundsOf(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LibraryBookshelf.prefab")).size.y;
        var scene=EditorSceneManager.NewPreviewScene(); var created=new List<string>();
        try
        {
            Action[] models={Shelf,Block,Earmuffs,Bear};
            for(int i=0;i<Names.Length;i++)
            {
                string path="Assets/Prefabs/"+Names[i]+".prefab";
                if(!replace && AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)continue;
                root=new GameObject(Names[i]); SceneManager.MoveGameObjectToScene(root,scene);
                try
                {
                    models[i](); Bake(Names[i]);
                    var prop=root.AddComponent<LevelProp>();prop.surface=PropSurface.Floor;prop.surfaceAnchor=Vector3.zero;prop.surfaceOffset=.01f;
                    prop.gameplayMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
                    PrefabUtility.SaveAsPrefabAsset(root,path);created.Add(path);
                }
                finally { Object.DestroyImmediate(root);foreach(var mesh in temporary)if(mesh!=null&&!AssetDatabase.Contains(mesh))Object.DestroyImmediate(mesh);temporary.Clear(); }
            }
            AssetDatabase.SaveAssets();return new {created,shelfHeight};
        }
        finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
    static Material Mat(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m!=null)return m;
        m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};m.SetFloat("_Smoothness",.08f);AssetDatabase.CreateAsset(m,path);return m;
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
    static void Shelf()
    {
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
        {
            var p=new Vector3(x*.72f,shelfHeight*.5f,z*.36f);
            Soft("Upright",blue,p,new Vector3(.055f,shelfHeight,.055f),.006f);Collision("Post",p,new Vector3(.055f,shelfHeight,.055f));
            Soft("Rubber foot",dark,new Vector3(p.x,.025f,p.z),new Vector3(.075f,.05f,.075f),.009f);
            for(int j=0;j<12;j++)Box("Height slot",dark,new Vector3(p.x,shelfHeight*(.09f+j*.072f),p.z+z*.028f),new Vector3(.014f,.024f,.0015f));
        }
        for(int level=0;level<3;level++)
        {
            float y=shelfHeight*(.075f+level*.34f);
            Soft("Shelf board "+level,wood,new Vector3(0,y,0),new Vector3(1.44f,.045f,.72f),.008f);
            Collision("Shelf surface "+level,new Vector3(0,y,0),new Vector3(1.44f,.045f,.72f));
            foreach(int sign in new[]{-1,1})
            {
                Box("Front and rear support",blue,new Vector3(0,y-.035f,sign*.337f),new Vector3(1.44f,.035f,.035f));
                Soft("Label holder",steel,new Vector3(.42f,y-.025f,sign*.361f),new Vector3(.18f,.035f,.008f),.003f);
                Box("Blank paper label",cream,new Vector3(.42f,y-.025f,sign*.366f),new Vector3(.15f,.022f,.001f));
            }
        }
        foreach(int sign in new[]{-1,1})
        {
            Rod("Side diagonal",steel,new Vector3(sign*.72f,.3f,-.36f),new Vector3(sign*.72f,shelfHeight-.16f,.36f),.018f);
            Rod("Side diagonal",steel,new Vector3(sign*.72f,.3f,.36f),new Vector3(sign*.72f,shelfHeight-.16f,-.36f),.018f);
            Box("Side top tie",blue,new Vector3(sign*.72f,shelfHeight-.03f,0),new Vector3(.055f,.04f,.72f));
        }
    }
    static void Block()
    {
        Soft("Small wooden cube",wood,new Vector3(0,.09f,0),Vector3.one*.18f,.009f);
        Collision("Cube collider",new Vector3(0,.09f,0),Vector3.one*.18f);
    }
    static void Arc(string name,Material material,float rx,float ry,float y,float thickness,float depth,float start,float end)
    {
        var v=new List<Vector3>();var t=new List<int>();const int segments=36;
        for(int i=0;i<=segments;i++)
        {
            float a=Mathf.Lerp(start,end,i/(float)segments)*Mathf.Deg2Rad;
            foreach(var p in new[]{new Vector2(-thickness*.5f,-depth*.5f),new Vector2(thickness*.5f,-depth*.5f),new Vector2(thickness*.5f,depth*.5f),new Vector2(-thickness*.5f,depth*.5f)})
                v.Add(new Vector3((rx+p.x)*Mathf.Cos(a),y+(ry+p.x)*Mathf.Sin(a),p.y));
        }
        for(int i=0;i<segments;i++)for(int j=0;j<4;j++){int a=i*4+j,b=i*4+(j+1)%4,c=(i+1)*4+(j+1)%4,d=(i+1)*4+j;t.AddRange(new[]{a,b,c,a,c,d});}
        t.AddRange(new[]{0,2,1,0,3,2,segments*4,segments*4+1,segments*4+2,segments*4,segments*4+2,segments*4+3});
        for(int i=0;i<t.Count;i+=3){int swap=t[i+1];t[i+1]=t[i+2];t[i+2]=swap;}
        Custom(name,material,v,t);
    }
    static void Earmuffs()
    {
        Arc("Continuous headband",dark,.118f,.16f,.115f,.012f,.035f,0,180);
        Arc("Soft head cushion",cream,.108f,.15f,.115f,.012f,.038f,42,138);
        foreach(int s in new[]{-1,1})
        {
            Ball("Ear cup shell",blue,new Vector3(s*.123f,.08f,0),new Vector3(.060f,.16f,.115f));
            Ball("Cushioned seal",dark,new Vector3(s*.096f,.08f,0),new Vector3(.027f,.147f,.103f));
            Ball("Cloth ear pad",cream,new Vector3(s*.083f,.08f,0),new Vector3(.009f,.112f,.073f));
            Ball("Outer shell inset",steel,new Vector3(s*.151f,.08f,0),new Vector3(.008f,.094f,.063f));
            Rod("Adjustable yoke",steel,new Vector3(s*.119f,.16f,-.025f),new Vector3(s*.138f,.104f,-.025f),.008f);
            Rod("Adjustable yoke",steel,new Vector3(s*.119f,.16f,.025f),new Vector3(s*.138f,.104f,.025f),.008f);
            Part("Pivot",PrimitiveType.Cylinder,dark,new Vector3(s*.151f,.106f,0),new Vector3(.014f,.004f,.014f),Quaternion.Euler(0,0,90));
            Collision("Ear cup collision",new Vector3(s*.12f,.08f,0),new Vector3(.071f,.16f,.11f));
        }
        Collision("Headband top collision",new Vector3(0,.27f,0),new Vector3(.11f,.023f,.035f));
    }
    static void Bear()
    {
        Ball("Plush body",fur,new Vector3(0,.183f,.015f),new Vector3(.245f,.29f,.20f));
        Ball("Belly patch",muzzle,new Vector3(0,.18f,-.077f),new Vector3(.158f,.19f,.033f));
        Ball("Round head",fur,new Vector3(0,.365f,0),new Vector3(.25f,.225f,.205f));
        foreach(int s in new[]{-1,1})
        {
            Ball("Round ear",fur,new Vector3(s*.094f,.45f,.005f),new Vector3(.101f,.105f,.065f));
            Ball("Inner ear cloth",muzzle,new Vector3(s*.094f,.45f,-.026f),new Vector3(.065f,.066f,.014f));
            Ball("Floppy arm",fur,new Vector3(s*.135f,.20f,-.015f),new Vector3(.098f,.205f,.105f),Quaternion.Euler(0,0,s*28));
            Ball("Arm paw",muzzle,new Vector3(s*.173f,.125f,-.042f),new Vector3(.063f,.066f,.038f));
            Ball("Seated foot",fur,new Vector3(s*.087f,.0575f,-.062f),new Vector3(.14f,.115f,.175f));
            Ball("Foot paw cloth",muzzle,new Vector3(s*.087f,.062f,-.14f),new Vector3(.088f,.068f,.024f));
            Ball("Button eye",dark,new Vector3(s*.049f,.384f,-.096f),new Vector3(.019f,.021f,.012f));
            Ball("Eye glint",cream,new Vector3(s*.049f-.003f,.388f,-.102f),Vector3.one*.004f);
            for(int j=0;j<3;j++)Rod("Paw embroidery",thread,new Vector3(s*.087f-.025f+j*.025f,.077f,-.152f),new Vector3(s*.087f-.025f+j*.025f,.066f,-.153f),.002f);
            Ball("Bow loop",red,new Vector3(s*.035f,.275f,-.10f),new Vector3(.069f,.038f,.021f),Quaternion.Euler(0,0,s*18));
        }
        Ball("Soft muzzle",muzzle,new Vector3(0,.343f,-.094f),new Vector3(.115f,.076f,.059f));
        Ball("Embroidered nose",thread,new Vector3(0,.356f,-.124f),new Vector3(.031f,.021f,.014f));
        Rod("Mouth center",thread,new Vector3(0,.349f,-.130f),new Vector3(0,.332f,-.124f),.0025f);
        foreach(int s in new[]{-1,1})Rod("Smile stitch",thread,new Vector3(0,.332f,-.124f),new Vector3(s*.018f,.338f,-.122f),.0025f);
        Ball("Bow knot",red,new Vector3(0,.275f,-.112f),new Vector3(.029f,.031f,.024f));
        for(int j=0;j<14;j++)
        {
            float a=j*Mathf.PI*2/14;var p=new Vector3(.071f*Mathf.Cos(a),.18f+.085f*Mathf.Sin(a),-.091f);
            Rod("Belly blanket stitch",thread,p,p+new Vector3(-Mathf.Cos(a)*.007f,-Mathf.Sin(a)*.007f,-.001f),.002f);
        }
        // Compound volumes leave the outline recognisable without a solid enclosing box.
        var body=root.AddComponent<SphereCollider>();body.center=new Vector3(0,.19f,0);body.radius=.13f;
        var head=root.AddComponent<SphereCollider>();head.center=new Vector3(0,.365f,0);head.radius=.115f;
        Collision("Feet collision",new Vector3(0,.052f,-.06f),new Vector3(.30f,.104f,.16f));
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
