using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Sound2.LevelEditing;
using Object=UnityEngine.Object;

public static class RebuildFurniture
{
    const string Folder="Assets/Models/Furniture";
    public static readonly string[] Kinds={"Bed","Sofa","Counter","HospitalBed","WardCurtain","Shelf","Checkout","Pine","SnowRock","Bench","Pillar","Bedside","Wardrobe","Sink","Toilet","Washer","ProduceBin"};
    static GameObject root;
    static Material wood,dark,metal,white,fabric,blue,green,red,snow,stone;
    static readonly List<Mesh> temporary=new();
    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Exit Play mode first");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        wood=Mat("Oak",new(.38f,.21f,.10f));dark=Mat("Charcoal",new(.055f,.063f,.07f));metal=Mat("Steel",new(.48f,.55f,.59f),.65f);
        white=Mat("Porcelain",new(.88f,.87f,.80f));fabric=Mat("Linen",new(.46f,.53f,.50f));blue=Mat("Medical blue",new(.22f,.43f,.51f));
        green=Mat("Foliage",new(.10f,.23f,.16f));red=Mat("Produce red",new(.56f,.12f,.07f));snow=Mat("Snow",new(.89f,.94f,.96f));stone=Mat("Stone",new(.36f,.40f,.43f));
        var results=new List<object>();
        foreach(string kind in Kinds)
        {
            string old="Assets/Prefabs/Adventure/Adventure"+kind+".prefab",path="Assets/Prefabs/"+kind+".prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(old)!=null)
            {
                if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)throw new Exception("Destination already exists: "+path);
                string error=AssetDatabase.MoveAsset(old,path);if(error!="")throw new Exception(error);
            }
            root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(Transform child in root.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
                foreach(var collider in root.GetComponents<Collider>())Object.DestroyImmediate(collider);
                root.name=kind;
                switch(kind)
                {
                    case "Bed": Bed(false);break;case "HospitalBed":Bed(true);break;case "Sofa":Sofa();break;
                    case "Counter":Counter();break;case "Shelf":Shelf();break;case "Checkout":Checkout();break;
                    case "WardCurtain":Curtain();break;case "Bedside":Bedside();break;case "Wardrobe":Wardrobe();break;
                    case "Sink":Sink();break;case "Toilet":Toilet();break;case "Washer":Washer();break;
                    case "ProduceBin":Produce();break;case "Pine":Pine();break;case "SnowRock":Rock();break;
                    case "Bench":Bench();break;case "Pillar":Pillar();break;
                }
                Bake(kind);
                if(kind=="WardCurtain")ConfigureCurtainLength();
                var prop=root.GetComponent<LevelProp>();prop.surfaceAnchor=Vector3.zero;prop.surface=PropSurface.Floor;
                prop.gameplayMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
                PrefabUtility.SaveAsPrefabAsset(root,path);
                results.Add(new{kind,mesh=Folder+"/"+kind+".asset",colliders=root.GetComponents<Collider>().Length});
            }
            finally{PrefabUtility.UnloadPrefabContents(root);root=null;foreach(var mesh in temporary)Object.DestroyImmediate(mesh);temporary.Clear();}
        }
        AssetDatabase.SaveAssets();return results;
    }
    static Material Mat(string n,Color color,float metallic=0){string p=Folder+"/"+n+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=n,color=color};m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",metallic>0?.65f:.32f);AssetDatabase.CreateAsset(m,p);}return m;}
    static GameObject Primitive(string name,PrimitiveType type,Material mat,Vector3 p,Vector3 s,Quaternion q=default)
    {var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(root.transform,false);go.transform.localPosition=p;go.transform.localScale=s;go.transform.localRotation=q==default?Quaternion.identity:q;go.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(go.GetComponent<Collider>());return go;}
    static void Box(Material m,Vector3 p,Vector3 s)=>Primitive("Panel",PrimitiveType.Cube,m,p,s);
    static void Ball(Material m,Vector3 p,Vector3 s)=>Primitive("Rounded part",PrimitiveType.Sphere,m,p,s);
    static void Cyl(Material m,Vector3 p,float radius,float h,bool front=false)=>Primitive("Turned part",PrimitiveType.Cylinder,m,p,new(radius*2,h*.5f,radius*2),front?Quaternion.Euler(90,0,0):Quaternion.identity);
    static void Rod(Material m,Vector3 a,Vector3 b,float width)=>Primitive("Rail",PrimitiveType.Cylinder,m,(a+b)*.5f,new(width,Vector3.Distance(a,b)*.5f,width),Quaternion.FromToRotation(Vector3.up,b-a));
    static void Collide(Vector3 p,Vector3 s){var c=root.AddComponent<BoxCollider>();c.center=p;c.size=s;}
    static void Custom(Material mat,List<Vector3> vertices,List<int> triangles)
    {var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();temporary.Add(mesh);var go=new GameObject("Shaped surface",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<Renderer>().sharedMaterial=mat;}
    static void Soft(Material mat,Vector3 center,Vector3 size,float bevel=.04f)
    {
        float b=Mathf.Min(bevel,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.4f);var v=new List<Vector3>();var t=new List<int>();
        Vector3[] Ring(float y,float inset){float x=size.x*.5f-inset,z=size.z*.5f-inset,c=Mathf.Min(b,Mathf.Min(x,z)*.4f);return new[]{new Vector3(-x+c,y,-z),new Vector3(x-c,y,-z),new Vector3(x,y,-z+c),new Vector3(x,y,z-c),new Vector3(x-c,y,z),new Vector3(-x+c,y,z),new Vector3(-x,y,z-c),new Vector3(-x,y,-z+c)};}
        void Quad(Vector3 a,Vector3 b0,Vector3 c,Vector3 d){int n=v.Count;v.AddRange(new[]{a+center,b0+center,c+center,d+center});t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
        var rings=new[]{Ring(-size.y*.5f,b*.45f),Ring(-size.y*.5f+b,0),Ring(size.y*.5f-b,0),Ring(size.y*.5f,b*.45f)};
        for(int j=0;j<3;j++)for(int i=0;i<8;i++)Quad(rings[j][i],rings[j+1][i],rings[j+1][(i+1)%8],rings[j][(i+1)%8]);
        for(int i=1;i<7;i++){int n=v.Count;v.AddRange(new[]{rings[0][0]+center,rings[0][i]+center,rings[0][i+1]+center,rings[3][0]+center,rings[3][i+1]+center,rings[3][i]+center});t.AddRange(new[]{n,n+1,n+2,n+3,n+4,n+5});}Custom(mat,v,t);
    }
    static void Lathe(Material mat,Vector3 center,Vector2[] profile,float xScale=1,float zScale=1,int segments=32,Quaternion rotation=default)
    {
        var v=new List<Vector3>();var t=new List<int>();var q=rotation==default?Quaternion.identity:rotation;
        Vector3 P(Vector2 p,float a)=>center+q*new Vector3(p.x*Mathf.Cos(a)*xScale,p.y,p.x*Mathf.Sin(a)*zScale);
        for(int j=0;j<profile.Length;j++)for(int i=0;i<segments;i++){int n=v.Count;float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;var next=profile[(j+1)%profile.Length];v.AddRange(new[]{P(profile[j],a),P(next,a),P(next,b),P(profile[j],b)});t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}Custom(mat,v,t);
    }
    static void Feet(float x,float z,float height,Material mat){foreach(int a in new[]{-1,1})foreach(int b in new[]{-1,1})Box(mat,new(a*x,height*.5f,b*z),new(.09f,height,.09f));}
    static void Bed(bool hospital)
    {
        var frame=hospital?metal:wood;Feet(.62f,1,.32f,frame);Soft(frame,new(0,.37f,0),new(1.52f,.18f,2.45f));
        Soft(white,new(0,.57f,0),new(1.43f,.25f,2.30f),.09f);Soft(hospital?blue:fabric,new(0,.72f,-.28f),new(1.45f,.065f,1.67f),.025f);
        for(int s=-1;s<=1;s+=2)Soft(white,new(s*.34f,.77f,.78f),new(.62f,.16f,.47f),.07f);
        Soft(frame,new(0,.8f,1.24f),new(1.58f,1.22f,.12f));
        if(!hospital){Soft(fabric,new(0,.96f,1.16f),new(1.35f,.63f,.06f));for(int i=-2;i<=2;i++)Cyl(wood,new(i*.24f,.95f,1.12f),.015f,.015f,true);}
        else
        {
            Soft(white,new(0,.58f,-1.22f),new(1.55f,.48f,.09f));
            foreach(int s in new[]{-1,1}){Rod(metal,new(s*.78f,.96f,-.8f),new(s*.78f,.96f,.75f),.045f);for(int j=0;j<4;j++)Rod(metal,new(s*.78f,.5f,-.7f+j*.45f),new(s*.78f,.96f,-.7f+j*.45f),.035f);}
            foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Primitive("Caster",PrimitiveType.Cylinder,dark,new(x*.62f,.09f,z),new(.16f,.035f,.16f),Quaternion.Euler(0,0,90));
        }
        Collide(new(0,.43f,0),new(1.56f,.86f,2.55f));Collide(new(0,1.02f,1.23f),new(1.58f,.80f,.14f));
    }
    static void Sofa()
    {
        Feet(1.05f,.36f,.16f,wood);Soft(dark,new(0,.28f,0),new(2.45f,.3f,.94f));
        Soft(fabric,new(0,.86f,.35f),new(2.45f,.75f,.26f),.09f);
        foreach(int s in new[]{-1,1})Soft(fabric,new(s*1.13f,.68f,-.02f),new(.26f,.6f,.98f),.09f);
        for(int i=-1;i<=1;i++){Soft(fabric,new(i*.69f,.52f,-.08f),new(.66f,.22f,.73f),.07f);Soft(fabric,new(i*.69f,.90f,.19f),new(.65f,.49f,.21f),.07f);}
        Collide(new(0,.44f,0),new(2.45f,.88f,.98f));Collide(new(0,1.03f,.34f),new(2.45f,.4f,.28f));
    }
    static void Counter(){Box(wood,new(0,.50f,0),new(2.2f,1,.82f));Soft(stone,new(0,1.075f,0),new(2.32f,.15f,.94f));Box(dark,new(0,.07f,-.43f),new(2.1f,.14f,.025f));for(int i=-1;i<=1;i++){Soft(wood,new(i*.7f,.58f,-.43f),new(.67f,.78f,.035f),.009f);Rod(metal,new(i*.7f-.17f,.88f,-.47f),new(i*.7f+.17f,.88f,-.47f),.025f);}Collide(new(0,.575f,0),new(2.32f,1.15f,.94f));}
    static void Shelf()
    {
        foreach(int s in new[]{-1,1}){Box(metal,new(s*1.1f,1.15f,0),new(.075f,2.3f,.08f));Box(metal,new(s*1.1f,.045f,0),new(.16f,.09f,1.1f));}
        Box(white,new(0,1.2f,0),new(2.2f,2.2f,.04f));
        for(int j=0;j<5;j++)foreach(int side in new[]{-1,1}){float y=.16f+j*.44f;Box(white,new(0,y,side*.25f),new(2.3f,.055f,.5f));Box(blue,new(0,y+.025f,side*.505f),new(2.3f,.06f,.025f));for(int k=0;k<7;k++){var mat=k%3==0?red:k%3==1?blue:white;Box(mat,new(-.92f+k*.3f,y+.15f,side*.29f),new(.20f,.25f,.23f));}}
        Collide(new(0,1.15f,0),new(2.35f,2.3f,1.08f));
    }
    static void Checkout()
    {
        Soft(blue,new(0,.52f,0),new(2.8f,1.04f,.85f));Soft(metal,new(0,1.075f,0),new(2.9f,.1f,.94f));Box(dark,new(-.45f,1.135f,0),new(1.6f,.025f,.70f));
        for(int i=0;i<12;i++)Box(greyMat(),new(-1.17f+i*.13f,1.15f,0),new(.012f,.008f,.66f));
        Box(metal,new(.55f,1.14f,0),new(.32f,.035f,.50f));Box(dark,new(.54f,1.163f,0),new(.20f,.012f,.32f));
        Rod(metal,new(1.05f,1.12f,.22f),new(1.05f,1.48f,.22f),.07f);Soft(dark,new(1.05f,1.6f,.20f),new(.42f,.28f,.06f));Box(blue,new(1.05f,1.6f,.165f),new(.36f,.22f,.008f));
        Soft(dark,new(.95f,1.18f,-.22f),new(.46f,.065f,.22f));for(int r=0;r<3;r++)for(int c=0;c<6;c++)Box(white,new(.77f+c*.065f,1.22f,-.28f+r*.05f),new(.045f,.013f,.032f));Collide(new(0,.57f,0),new(2.9f,1.14f,.94f));
    }
    static Material greyMat()=>metal;
    static void Curtain()
    {
        foreach(int s in new[]{-1,1}){Rod(metal,new(s*1.25f,.07f,0),new(s*1.25f,2.32f,0),.04f);Rod(metal,new(s*1.25f,.07f,-.36f),new(s*1.25f,.07f,.36f),.055f);foreach(int z in new[]{-1,1})Ball(dark,new(s*1.25f,.055f,z*.3f),Vector3.one*.10f);}
        Rod(metal,new(-1.25f,2.32f,0),new(1.25f,2.32f,0),.04f);
        var v=new List<Vector3>();var t=new List<int>();for(int i=0;i<40;i++){float x=-1.18f+i*.059f,x2=x+.059f,z=Mathf.Sin(i*Mathf.PI*.5f)*.055f,z2=Mathf.Sin((i+1)*Mathf.PI*.5f)*.055f;int n=v.Count;v.AddRange(new[]{new Vector3(x,.35f,z),new Vector3(x,2.23f,z),new Vector3(x2,2.23f,z2),new Vector3(x2,.35f,z2)});v.AddRange(new[]{v[n],v[n+1],v[n+2],v[n+3]});t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3,n+6,n+5,n+4,n+7,n+6,n+4});}Custom(blue,v,t);for(int i=0;i<12;i++)Rod(metal,new(-1.1f+i*.2f,2.22f,0),new(-1.1f+i*.2f,2.32f,0),.018f);Collide(new(0,1.3f,0),new(2.5f,2.05f,.16f));
    }
    static void ConfigureCurtainLength()
    {
        var rod=GameObject.CreatePrimitive(PrimitiveType.Cylinder);var wheel=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        try
        {
            var curtain=root.GetComponent<ResizableWardCurtain>()??root.AddComponent<ResizableWardCurtain>();
            var model=root.transform.Find("Model").GetComponent<MeshFilter>();
            curtain.Configure(model,rod.GetComponent<MeshFilter>().sharedMesh,wheel.GetComponent<MeshFilter>().sharedMesh,root.GetComponent<BoxCollider>());
            curtain.UseStoredMesh(PropMeshAssets.Store(Folder,"WardCurtain",Object.Instantiate(model.sharedMesh)));
        }
        finally{Object.DestroyImmediate(rod);Object.DestroyImmediate(wheel);}
    }
    static void Bedside(){Feet(.24f,.19f,.10f,wood);Soft(wood,new(0,.4f,0),new(.6f,.6f,.5f));Soft(stone,new(0,.74f,0),new(.65f,.08f,.55f));for(int i=0;i<2;i++){Soft(wood,new(0,.28f+i*.29f,-.265f),new(.54f,.26f,.035f),.012f);Rod(metal,new(-.1f,.3f+i*.29f,-.30f),new(.1f,.3f+i*.29f,-.30f),.023f);}Collide(new(0,.39f,0),new(.65f,.78f,.55f));}
    static void Wardrobe(){Soft(wood,new(0,1.2f,0),new(2,2.4f,.65f));Soft(dark,new(0,.08f,0),new(1.9f,.16f,.6f));foreach(int s in new[]{-1,1}){Soft(wood,new(s*.48f,1.24f,-.35f),new(.94f,2.24f,.045f),.016f);Box(fabric,new(s*.48f,1.4f,-.378f),new(.77f,1.78f,.009f));Rod(metal,new(s*.09f,1.08f,-.4f),new(s*.09f,1.38f,-.4f),.025f);}Soft(wood,new(0,2.4f,0),new(2.08f,.07f,.70f));Collide(new(0,1.22f,0),new(2.08f,2.44f,.70f));}
    static void Sink()
    {
        Lathe(white,Vector3.zero,new[]{new Vector2(.19f,0),new Vector2(.17f,.12f),new Vector2(.11f,.58f),new Vector2(.19f,.76f),new Vector2(.08f,.76f),new Vector2(.08f,0)},1,.85f);
        Lathe(white,new(0,0,-.025f),new[]{new Vector2(.14f,.68f),new Vector2(.33f,.76f),new Vector2(.38f,.86f),new Vector2(.38f,.90f),new Vector2(.32f,.90f),new Vector2(.27f,.79f),new Vector2(.035f,.75f),new Vector2(.035f,.70f)},1,.77f);
        Box(white,new(0,.855f,.24f),new(.65f,.09f,.12f));Rod(metal,new(0,.90f,.24f),new(0,1.1f,.24f),.045f);Rod(metal,new(0,1.1f,.24f),new(0,1.1f,.02f),.045f);Rod(metal,new(0,1.1f,.02f),new(0,1.04f,.02f),.045f);for(int x=-1;x<=1;x+=2)Cyl(metal,new(x*.12f,.94f,.24f),.028f,.07f);Cyl(metal,new(0,.755f,-.025f),.03f,.008f);
        Collide(new(0,.4f,0),new(.30f,.8f,.28f));Collide(new(0,.81f,.24f),new(.7f,.18f,.14f));foreach(int s in new[]{-1,1})Collide(new(s*.33f,.82f,0),new(.08f,.17f,.42f));Collide(new(0,.8f,-.26f),new(.55f,.15f,.07f));
    }
    static void Toilet()
    {
        Soft(white,new(0,.20f,.05f),new(.37f,.40f,.62f),.09f);
        Lathe(white,new(0,0,-.10f),new[]{new Vector2(.15f,.17f),new Vector2(.24f,.28f),new Vector2(.28f,.42f),new Vector2(.28f,.45f),new Vector2(.23f,.45f),new Vector2(.18f,.29f),new Vector2(.07f,.22f)},1,1.35f);
        Lathe(white,new(0,0,-.10f),new[]{new Vector2(.29f,.45f),new Vector2(.29f,.485f),new Vector2(.22f,.485f),new Vector2(.22f,.45f)},1,1.35f);
        Soft(white,new(0,.61f,.35f),new(.59f,.68f,.27f),.055f);Soft(white,new(0,.97f,.35f),new(.62f,.055f,.30f),.018f);Cyl(metal,new(.12f,1.005f,.35f),.03f,.012f);
        Collide(new(0,.23f,-.02f),new(.5f,.46f,.72f));Collide(new(0,.63f,.35f),new(.62f,.76f,.30f));
    }
    static void Washer()
    {
        Soft(white,new(0,.46f,0),new(.85f,.88f,.75f));Box(dark,new(0,.085f,-.38f),new(.76f,.065f,.018f));Soft(white,new(0,.88f,0),new(.88f,.065f,.78f));
        Lathe(metal,new(0,.43f,-.39f),new[]{new Vector2(.27f,0),new Vector2(.27f,.035f),new Vector2(.22f,.05f),new Vector2(.20f,.03f),new Vector2(.20f,0)},1,1,48,Quaternion.Euler(90,0,0));
        Cyl(dark,new(0,.43f,-.43f),.205f,.024f,true);Cyl(blue,new(0,.43f,-.45f),.17f,.008f,true);Soft(metal,new(.245f,.43f,-.44f),new(.04f,.12f,.035f));
        Box(greyMat(),new(0,.79f,-.386f),new(.79f,.13f,.016f));Soft(white,new(-.23f,.79f,-.4f),new(.27f,.09f,.02f));Cyl(metal,new(.22f,.79f,-.42f),.045f,.05f,true);Box(dark,new(.02f,.79f,-.40f),new(.13f,.045f,.012f));Collide(new(0,.46f,0),new(.88f,.92f,.8f));
    }
    static void Produce()
    {
        Feet(1.04f,.45f,.80f,wood);Box(wood,new(0,.68f,0),new(2.35f,.18f,1.15f));
        for(int i=0;i<3;i++)for(int j=0;j<2;j++)
        {float x=-.78f+i*.78f,z=-.29f+j*.58f;Box(wood,new(x,.96f,z),new(.74f,.06f,.54f));foreach(int s in new[]{-1,1}){Box(wood,new(x+s*.35f,1.08f,z),new(.035f,.24f,.54f));for(int h=0;h<2;h++)Box(wood,new(x,1.01f+h*.12f,z+s*.25f),new(.74f,.055f,.025f));}
            for(int a=0;a<3;a++)for(int b=0;b<2;b++)Ball(i==0?red:i==1?green:white,new(x-.21f+a*.21f,1.08f,z-.12f+b*.23f),new(.16f,.17f,.16f));}
        Collide(new(0,.59f,0),new(2.4f,1.18f,1.2f));
    }
    static void Pine()
    {
        Cyl(wood,new(0,1.35f,0),.18f,2.7f);
        for(int level=0;level<5;level++)
        {float y=.8f+level*.75f,r=1.65f-level*.28f;Lathe(green,new(0,y,0),new[]{new Vector2(r,0),new Vector2(r*.77f,.28f),new Vector2(r*.48f,.85f),new Vector2(.025f,1.55f),new Vector2(.02f,0)},1,1,12);
            Lathe(snow,new(0,y+.16f,0),new[]{new Vector2(r*.89f,0),new Vector2(r*.68f,.30f),new Vector2(.02f,1.38f),new Vector2(.02f,0)},1,1,12);
            for(int k=0;k<6;k++){float a=k*Mathf.PI/3+level*.3f;Rod(wood,new(0,y+.2f,0),new(Mathf.Cos(a)*r*.8f,y,Mathf.Sin(a)*r*.8f),.04f);}}
        var c=root.AddComponent<CapsuleCollider>();c.center=new(0,2.2f,0);c.radius=.3f;c.height=4.4f;
    }
    static void Rock()
    {
        for(int j=0;j<3;j++){var go=Primitive("Faceted stone",PrimitiveType.Sphere,stone,new(-.65f+j*.65f,.32f,j%2*.23f),new(1.25f,.9f,1.0f));
            var mesh=Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);temporary.Add(mesh);var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++){var p=vertices[i];float f=1+Mathf.Sin(p.x*21+p.y*17+p.z*13+j)*.10f;vertices[i]=p*f;}mesh.vertices=vertices;mesh.RecalculateNormals();go.GetComponent<MeshFilter>().sharedMesh=mesh;
            Soft(snow,new(-.65f+j*.65f,.63f,j%2*.23f),new(.94f,.18f,.77f),.07f);}
        Collide(new(0,.34f,.10f),new(2.5f,.68f,1.2f));
    }
    static void Bench()
    {foreach(int x in new[]{-1,1}){Rod(metal,new(x*.96f,.08f,-.24f),new(x*.96f,.52f,-.24f),.075f);Rod(metal,new(x*.96f,.08f,.23f),new(x*.96f,1.14f,.34f),.075f);Rod(metal,new(x*1.08f,.73f,-.25f),new(x*1.08f,.73f,.29f),.055f);}
        for(int i=0;i<4;i++){Soft(wood,new(0,.52f,-.24f+i*.16f),new(2.5f,.075f,.13f),.018f);Soft(wood,new(0,.76f+i*.13f,.32f+i*.013f),new(2.5f,.10f,.065f),.015f);}Collide(new(0,.30f,0),new(2.5f,.6f,.65f));Collide(new(0,.93f,.34f),new(2.5f,.65f,.1f));}
    static void Pillar(){Soft(stone,new(0,.13f,0),new(1.9f,.26f,1.9f));Soft(stone,new(0,.34f,0),new(1.6f,.16f,1.6f));Cyl(stone,new(0,2.55f,0),.62f,4.3f);for(int i=0;i<12;i++){float a=i*Mathf.PI/6;Rod(white,new(Mathf.Cos(a)*.60f,.6f,Mathf.Sin(a)*.60f),new(Mathf.Cos(a)*.60f,4.45f,Mathf.Sin(a)*.60f),.045f);}Soft(stone,new(0,4.8f,0),new(1.6f,.24f,1.6f));Soft(stone,new(0,5.01f,0),new(1.9f,.18f,1.9f));Collide(new(0,2.55f,0),new(1.25f,5.1f,1.25f));Collide(new(0,.2f,0),new(1.9f,.4f,1.9f));}
    static void Bake(string name)
    {
        var filters=root.GetComponentsInChildren<MeshFilter>();var groups=filters.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial).ToArray();var combines=new List<CombineInstance>();
        foreach(var group in groups){var mesh=new Mesh();temporary.Add(mesh);mesh.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=root.transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);combines.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.identity});}
        var result=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};result.CombineMeshes(combines.ToArray(),false,true);result=PropMeshAssets.Store(Folder,name,result);
        // Refresh GPU channels after updating an existing mesh without changing its asset identity.
        result.vertices=result.vertices;result.normals=result.normals;result.UploadMeshData(false);
        foreach(var filter in filters)Object.DestroyImmediate(filter.gameObject);
        var model=new GameObject("Model",typeof(MeshFilter),typeof(MeshRenderer));model.transform.SetParent(root.transform,false);model.GetComponent<MeshFilter>().sharedMesh=result;model.GetComponent<Renderer>().sharedMaterials=groups.Select(g=>g.Key).ToArray();
    }
}
