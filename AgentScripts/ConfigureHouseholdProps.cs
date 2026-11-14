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

public static class ConfigureHouseholdProps
{
    const string Folder = "Assets/Models/HouseholdProps";
    static Material wood, dark, metal, white, blue, ceramic, glass, paper, red, green, black;
    static GameObject root;
    static Scene stage;
    static readonly List<string> created = new();

    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode before authoring props.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh(); created.Clear();
        wood=Mat("Wood",new Color(.42f,.24f,.12f)); dark=Mat("Charcoal",new Color(.065f,.085f,.10f));
        metal=Mat("Steel",new Color(.42f,.50f,.54f)); white=Mat("Enamel",new Color(.82f,.84f,.80f));
        blue=Mat("Blue",new Color(.10f,.28f,.40f)); ceramic=Mat("Ceramic",new Color(.72f,.43f,.24f));
        glass=Mat("Glass",new Color(.40f,.72f,.78f)); paper=Mat("Paper",new Color(.77f,.72f,.59f));
        red=Mat("Red",new Color(.42f,.12f,.10f)); green=Mat("Green",new Color(.16f,.31f,.22f));
        black=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        MigrateCoffeeMug();
        stage=EditorSceneManager.NewPreviewScene();
        try
        {
            Build("Chair",Chair); Build("ShoppingCart",Cart); Build("LibraryBookshelf",Bookshelf);
            Build("WritingDesk",Desk); Build("Computer",Computer); Build("PickupWaterCup",WaterCup);
            Build("PickupDeskLamp",Lamp); Build("Refrigerator",Fridge); Build("Stove",Stove);
            Build("TrashBin",Bin); Build("PickupCeramicVase",Vase); Build("PickupGlassMug",Wineglass,true);
        }
        finally { if(root!=null)Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(stage); }
        AssetDatabase.SaveAssets(); return new { created, coffee="Assets/Prefabs/PickupCoffeeMug.prefab" };
    }

    static Material Mat(string name,Color color)
    {
        string path=Folder+"/"+name+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color}; material.SetFloat("_Smoothness",.3f); AssetDatabase.CreateAsset(material,path); }
        return material;
    }
    static void Presentation(GameObject go)
    {
        var prop=go.GetComponent<LevelProp>();if(prop==null)prop=go.AddComponent<LevelProp>();
        prop.surface=PropSurface.Floor; prop.surfaceAnchor=Vector3.zero; prop.surfaceOffset=.01f; prop.gameplayMaterial=black;
    }
    static void Build(string name,Action model,bool replace=false)
    {
        string path="Assets/Prefabs/"+name+".prefab";
        if(!replace && AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)return;
        bool loaded=AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null;
        root=loaded?PrefabUtility.LoadPrefabContents(path):new GameObject(name);
        if(!loaded)SceneManager.MoveGameObjectToScene(root,stage);
        try
        {
            if(loaded)
                foreach(Transform part in root.transform.Cast<Transform>().ToArray())
                    if(part.name!="Interaction Anchor" && part.name!="Sound Origin")Object.DestroyImmediate(part.gameObject);
            model(); Presentation(root); PrefabUtility.SaveAsPrefabAsset(root,path); created.Add(path);
        }
        finally { if(loaded)PrefabUtility.UnloadPrefabContents(root);else Object.DestroyImmediate(root); root=null; }
    }
    static GameObject Primitive(string name,PrimitiveType type,Material mat,Vector3 p,Vector3 scale,Quaternion rotation=default)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(root.transform,false);
        go.transform.localPosition=p;go.transform.localScale=scale;go.transform.localRotation=rotation==default?Quaternion.identity:rotation;
        go.GetComponent<Renderer>().sharedMaterial=mat;
        if(type==PrimitiveType.Cylinder)
        {Object.DestroyImmediate(go.GetComponent<Collider>());var c=go.AddComponent<MeshCollider>();c.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;c.convex=true;}
        return go;
    }
    static GameObject Box(string name,Material mat,Vector3 p,Vector3 size)=>Primitive(name,PrimitiveType.Cube,mat,p,size);
    static GameObject Cylinder(string name,Material mat,Vector3 p,float radius,float height,Quaternion rotation=default)=>Primitive(name,PrimitiveType.Cylinder,mat,p,new Vector3(radius*2,height*.5f,radius*2),rotation);
    static void Rod(string name,Material mat,Vector3 a,Vector3 b,float diameter)
    {Primitive(name,PrimitiveType.Cylinder,mat,(a+b)*.5f,new Vector3(diameter,Vector3.Distance(a,b)*.5f,diameter),Quaternion.FromToRotation(Vector3.up,b-a));}
    static Mesh Store(string name,Mesh mesh)
    {
        return PropMeshAssets.Store(Folder,name,mesh);
    }
    static GameObject MeshPart(GameObject parent,string name,Mesh mesh,Material mat,Vector3 position,bool collider=true)
    {
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent.transform,false);go.transform.localPosition=position;
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<Renderer>().sharedMaterial=mat;
        if(collider){var c=go.AddComponent<MeshCollider>();c.sharedMesh=mesh;c.convex=true;}
        return go;
    }
    static Transform Anchor(string name,Vector3 p)
    {
        var t=root.transform.Find(name);if(t==null){t=new GameObject(name).transform;t.SetParent(root.transform,false);}t.localPosition=p;return t;
    }
    static void Pickup(float mass,Vector3 centre,GameObject remains=null,float threshold=4)
    {
        var body=root.GetComponent<Rigidbody>();if(body==null)body=root.AddComponent<Rigidbody>();body.mass=mass;
        body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        var audio=root.GetComponent<AudioSource>();if(audio==null)audio=root.AddComponent<AudioSource>();audio.playOnAwake=false;audio.spatialBlend=1;
        var cue=root.GetComponent<SonarCueSource>();if(cue==null)cue=root.AddComponent<SonarCueSource>();cue.output=audio;cue.clip=null;cue.generateDefaultPing=false;
        var item=root.GetComponent<PickupItem>();if(item==null)item=root.AddComponent<PickupItem>();item.breakable=remains!=null;item.brokenPrefab=remains;item.breakSpeed=threshold;
        item.breakClip=null;item.fragmentLifetime=0;item.breakPulseRange=10;item.breakHearingRange=16;item.interactionAnchor=Anchor("Interaction Anchor",centre);
        var sonar=root.GetComponent<SonarObjectEmitter>();sonar.automatic=false;sonar.toggleKey=Key.None;
        sonar.range=5;sonar.soundRange=8;sonar.waveSpeed=7;sonar.afterglow=.5f;sonar.emissionOrigin=Anchor("Sound Origin",centre);
    }
    static void Chair()
    {
        Box("Seat",wood,new Vector3(0,.46f,0),new Vector3(.48f,.06f,.48f));
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
            Box("Leg "+x+" "+z,wood,new Vector3(x*.19f,.22f,z*.19f),new Vector3(.055f,.44f,.055f));
        for(int x=-1;x<=1;x+=2)Box("Back post "+x,wood,new Vector3(x*.19f,.71f,.2f),new Vector3(.055f,.5f,.055f));
        for(int i=0;i<3;i++)Box("Back slat "+i,wood,new Vector3(0,.68f+i*.1f,.2f),new Vector3(.4f,.065f,.045f));
    }
    static void Desk()
    {
        Box("Desktop",wood,new Vector3(0,.77f,0),new Vector3(1.4f,.06f,.7f));
        Box("Left support",wood,new Vector3(-.63f,.37f,0),new Vector3(.065f,.74f,.61f));
        Box("Drawer pedestal",wood,new Vector3(.47f,.36f,0),new Vector3(.38f,.72f,.6f));
        Box("Back modesty panel",wood,new Vector3(0,.5f,.28f),new Vector3(1.25f,.35f,.04f));
        for(int i=0;i<3;i++)
        {Box("Drawer "+i,white,new Vector3(.47f,.14f+i*.22f,-.31f),new Vector3(.33f,.19f,.025f));Box("Drawer pull "+i,dark,new Vector3(.47f,.17f+i*.22f,-.34f),new Vector3(.12f,.018f,.025f));}
    }
    static void Bookshelf()
    {
        Box("Back",wood,new Vector3(0,1.05f,.19f),new Vector3(1.5f,2.1f,.04f));
        for(int s=-1;s<=1;s+=2)Box("Side "+s,wood,new Vector3(s*.73f,1.05f,0),new Vector3(.06f,2.1f,.42f));
        var colors=new[]{blue,red,green,white};
        for(int row=0;row<5;row++)
        {
            float y=.08f+row*.49f;Box("Shelf "+row,wood,new Vector3(0,y,0),new Vector3(1.45f,.05f,.42f));
            if(row==4)continue;
            for(int i=0;i<13;i++)
            {
                float h=.27f+(i*7%5)*.025f,x=-.63f+i*.101f;
                Box("Book "+row+" "+i,colors[(i+row)%4],new Vector3(x,y+.025f+h*.5f,-.015f),new Vector3(.075f,h,.28f));
                Box("Spine label "+row+" "+i,paper,new Vector3(x,y+.09f,-.158f),new Vector3(.045f,.03f,.006f));
            }
        }
    }
    static void Computer()
    {
        Box("Monitor foot",dark,new Vector3(-.12f,.018f,0),new Vector3(.26f,.036f,.2f));
        Box("Monitor stand",metal,new Vector3(-.12f,.14f,.045f),new Vector3(.055f,.25f,.045f));
        Box("Monitor case",dark,new Vector3(-.12f,.36f,.045f),new Vector3(.56f,.34f,.045f));
        Box("Screen",blue,new Vector3(-.12f,.36f,.019f),new Vector3(.515f,.295f,.008f));
        Box("Tower",dark,new Vector3(.36f,.23f,.04f),new Vector3(.19f,.46f,.38f));
        for(int i=0;i<6;i++)Box("Tower vent "+i,metal,new Vector3(.36f,.12f+i*.025f,-.153f),new Vector3(.12f,.006f,.006f));
        Cylinder("Power button",white,new Vector3(.36f,.37f,-.154f),.012f,.007f,Quaternion.Euler(90,0,0));
        Box("Keyboard",dark,new Vector3(-.12f,.02f,-.26f),new Vector3(.46f,.04f,.15f));
        for(int r=0;r<4;r++)for(int c=0;c<12;c++)Box("Key "+r+" "+c,white,new Vector3(-.325f+c*.037f,.044f,-.312f+r*.033f),new Vector3(.027f,.008f,.023f));
        Primitive("Mouse",PrimitiveType.Sphere,dark,new Vector3(.19f,.028f,-.26f),new Vector3(.065f,.055f,.11f));
    }
    static void Cart()
    {
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
        {
            Cylinder("Wheel "+x+" "+z,dark,new Vector3(x*.29f,.085f,z*.36f),.085f,.045f,Quaternion.Euler(0,0,90));
            Rod("Wheel fork "+x+" "+z,metal,new Vector3(x*.29f,.09f,z*.36f),new Vector3(x*.29f,.23f,z*.36f),.035f);
        }
        foreach(int x in new[]{-1,1})
        {
            Rod("Chassis "+x,metal,new Vector3(x*.29f,.23f,-.43f),new Vector3(x*.29f,.23f,.43f),.035f);
            Rod("Handle post "+x,metal,new Vector3(x*.29f,.23f,.36f),new Vector3(x*.29f,1.05f,.5f),.035f);
            Rod("Front post "+x,metal,new Vector3(x*.23f,.5f,-.4f),new Vector3(x*.31f,.92f,-.46f),.025f);
            for(int i=0;i<5;i++)
            {float t=i/4f;Rod("Basket side rail "+x+" "+i,metal,new Vector3(x*Mathf.Lerp(.23f,.31f,t),Mathf.Lerp(.5f,.92f,t),Mathf.Lerp(-.4f,-.46f,t)),new Vector3(x*Mathf.Lerp(.23f,.31f,t),Mathf.Lerp(.5f,.92f,t),.36f),.014f);}
            for(int i=0;i<10;i++)
            {float z=Mathf.Lerp(-.4f,.36f,i/9f);Rod("Basket side wire "+x+" "+i,metal,new Vector3(x*.23f,.5f,z),new Vector3(x*.31f,.92f,z),.011f);}
        }
        for(int i=0;i<8;i++)
        {
            float x=Mathf.Lerp(-.23f,.23f,i/7f);Rod("Basket floor wire "+i,metal,new Vector3(x,.5f,-.4f),new Vector3(x,.5f,.36f),.014f);
            for(int z=-1;z<=1;z+=2)Rod("Basket end wire "+z+" "+i,metal,new Vector3(x,.5f,z<0?-.4f:.36f),new Vector3(x*1.35f,.92f,z<0?-.46f:.36f),.012f);
        }
        for(int i=0;i<5;i++)for(int z=-1;z<=1;z+=2)
        {float t=i/4f,y=Mathf.Lerp(.5f,.92f,t),half=Mathf.Lerp(.23f,.31f,t);Rod("End rail "+z+" "+i,metal,new Vector3(-half,y,z<0?Mathf.Lerp(-.4f,-.46f,t):.36f),new Vector3(half,y,z<0?Mathf.Lerp(-.4f,-.46f,t):.36f),.014f);}
        Rod("Push handle",red,new Vector3(-.32f,1.05f,.5f),new Vector3(.32f,1.05f,.5f),.045f);
        // Five compound boxes provide smooth collision around the open basket; wires remain visual detail.
        foreach(var c in root.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
        void Collision(Vector3 p,Vector3 size){var go=new GameObject("Basket collision");go.transform.SetParent(root.transform,false);var c=go.AddComponent<BoxCollider>();c.center=p;c.size=size;}
        Collision(new Vector3(0,.5f,0),new Vector3(.62f,.035f,.86f));
        Collision(new Vector3(-.3f,.72f,0),new Vector3(.03f,.44f,.9f));Collision(new Vector3(.3f,.72f,0),new Vector3(.03f,.44f,.9f));
        Collision(new Vector3(0,.72f,-.44f),new Vector3(.6f,.44f,.03f));Collision(new Vector3(0,.72f,.37f),new Vector3(.6f,.44f,.03f));
        Collision(new Vector3(0,.16f,0),new Vector3(.65f,.32f,.86f));
    }
    static void Fridge()
    {
        Box("Cabinet",white,new Vector3(0,.9f,.02f),new Vector3(.72f,1.8f,.68f));
        Box("Freezer door",white,new Vector3(0,1.53f,-.345f),new Vector3(.70f,.50f,.045f));
        Box("Fridge door",white,new Vector3(0,.65f,-.345f),new Vector3(.70f,1.22f,.045f));
        Box("Door seam",dark,new Vector3(0,1.267f,-.369f),new Vector3(.69f,.012f,.006f));
        Box("Upper handle",metal,new Vector3(-.25f,1.48f,-.4f),new Vector3(.025f,.30f,.04f));
        Box("Lower handle",metal,new Vector3(-.25f,.98f,-.4f),new Vector3(.025f,.35f,.04f));
        for(int i=0;i<6;i++)Box("Base vent "+i,dark,new Vector3(-.25f+i*.1f,.055f,-.369f),new Vector3(.055f,.018f,.008f));
    }
    static void Stove()
    {
        Box("Oven body",white,new Vector3(0,.43f,0),new Vector3(.76f,.86f,.65f));
        Box("Cooktop",metal,new Vector3(0,.88f,0),new Vector3(.8f,.04f,.69f));
        Box("Oven door",dark,new Vector3(0,.39f,-.334f),new Vector3(.65f,.47f,.025f));
        Box("Oven glass",blue,new Vector3(0,.38f,-.35f),new Vector3(.52f,.32f,.01f));
        Box("Oven handle",metal,new Vector3(0,.64f,-.38f),new Vector3(.55f,.025f,.035f));
        for(int i=0;i<4;i++)Cylinder("Control "+i,dark,new Vector3(-.24f+i*.16f,.77f,-.34f),.03f,.028f,Quaternion.Euler(90,0,0));
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
        {Cylinder("Burner "+x+" "+z,dark,new Vector3(x*.2f,.913f,z*.17f),.105f,.026f);Box("Grate X "+x+" "+z,metal,new Vector3(x*.2f,.94f,z*.17f),new Vector3(.25f,.018f,.015f));Box("Grate Z "+x+" "+z,metal,new Vector3(x*.2f,.94f,z*.17f),new Vector3(.015f,.018f,.25f));}
    }
    static void Lamp()
    {
        Cylinder("Base",dark,new Vector3(0,.025f,0),.13f,.05f);
        Rod("Lower arm",metal,new Vector3(0,.05f,0),new Vector3(.08f,.32f,.04f),.025f);
        Rod("Upper arm",metal,new Vector3(.08f,.32f,.04f),new Vector3(0,.51f,-.1f),.025f);
        Primitive("Elbow",PrimitiveType.Sphere,dark,new Vector3(.08f,.32f,.04f),Vector3.one*.06f);
        var shade=MeshPart(root,"Shade",Store("LampShade",Sector(new[]{new Vector2(.13f,0),new Vector2(.06f,.14f),new Vector2(.045f,.14f),new Vector2(.115f,0)},0,Mathf.PI*2,32)),blue,new Vector3(0,.39f,-.1f),false);
        var collider=shade.AddComponent<MeshCollider>();collider.sharedMesh=shade.GetComponent<MeshFilter>().sharedMesh;collider.convex=true;
        Primitive("Bulb",PrimitiveType.Sphere,white,new Vector3(0,.405f,-.1f),Vector3.one*.07f);
        Pickup(.65f,new Vector3(0,.25f,-.03f));
    }
    static void WaterCup()
    {
        Vessel("WaterCup",new[]{new Vector2(.075f,.025f),new Vector2(.09f,.24f)},.008f,glass,false);
        Pickup(.18f,new Vector3(0,.12f,0));
    }
    static void Bin()
    {
        Vessel("TrashBin",new[]{new Vector2(.17f,.025f),new Vector2(.22f,.52f)},.018f,metal,false);
        MeshPart(root,"Rim",Store("BinRim",Sector(new[]{new Vector2(.22f,.50f),new Vector2(.232f,.50f),new Vector2(.232f,.53f),new Vector2(.205f,.53f)},0,Mathf.PI*2,32)),dark,Vector3.zero,false);
    }
    static void Vase()
    {
        var broken=Vessel("CeramicVase",new[]{new Vector2(.10f,.025f),new Vector2(.17f,.13f),new Vector2(.19f,.25f),new Vector2(.11f,.37f),new Vector2(.065f,.44f),new Vector2(.08f,.49f)},.018f,ceramic,true);
        Pickup(.75f,new Vector3(0,.245f,0),broken,4.5f);
    }
    static void Wineglass()
    {
        var broken=Vessel("WineGlass",new[]{new Vector2(.025f,.21f),new Vector2(.10f,.26f),new Vector2(.125f,.34f),new Vector2(.10f,.46f)},.009f,glass,true,true);
        Pickup(.18f,new Vector3(0,.33f,0),broken,3.8f);
    }

    static Mesh Sector(Vector2[] profile,float a,float b,int segments)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        Vector3 P(Vector2 p,float angle)=>new Vector3(p.x*Mathf.Cos(angle),p.y,p.x*Mathf.Sin(angle));
        void Quad(Vector3 p,Vector3 q,Vector3 r,Vector3 s)
        {int n=vertices.Count;vertices.AddRange(new[]{p,q,r,s});triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
        // Profiles run clockwise in radius/height space, giving outward normals.
        for(int j=0;j<profile.Length;j++)for(int i=0;i<segments;i++)
        {float t=Mathf.Lerp(a,b,i/(float)segments),u=Mathf.Lerp(a,b,(i+1f)/segments);int next=(j+1)%profile.Length;Quad(P(profile[j],t),P(profile[next],t),P(profile[next],u),P(profile[j],u));}
        if(b-a<Mathf.PI*2-.001f)
        {
            for(int j=1;j<profile.Length-1;j++)
            {int n=vertices.Count;vertices.AddRange(new[]{P(profile[0],a),P(profile[j+1],a),P(profile[j],a),P(profile[0],b),P(profile[j],b),P(profile[j+1],b)});triangles.AddRange(new[]{n,n+1,n+2,n+3,n+4,n+5});}
        }
        var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static GameObject Vessel(string name,Vector2[] profile,float thickness,Material material,bool breakable,bool stemmed=false)
    {
        var debris=new GameObject(name+"Broken");SceneManager.MoveGameObjectToScene(debris,stage);
        var combines=new List<CombineInstance>();
        try
        {
            void Piece(string partName,Mesh mesh)
            {
                var centre=mesh.bounds.center;mesh.vertices=mesh.vertices.Select(v=>v-centre).ToArray();mesh.RecalculateBounds();mesh=Store(name+partName,mesh);
                var part=MeshPart(debris,partName,mesh,material,centre);
                var body=part.AddComponent<Rigidbody>();body.mass=.025f;body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.maxDepenetrationVelocity=2;
                combines.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.Translate(centre)});
                var c=new GameObject(partName+" Collider");c.transform.SetParent(root.transform,false);c.transform.localPosition=centre;
                var collider=c.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;
            }
            int slices=12;
            for(int band=0;band<profile.Length-1;band++)for(int slice=0;slice<slices;slice++)
            {
                var lo=profile[band];var hi=profile[band+1];
                Piece("Wall "+band+" "+slice,Sector(new[]{lo,hi,new Vector2(Mathf.Max(.006f,hi.x-thickness),hi.y),new Vector2(Mathf.Max(.006f,lo.x-thickness),lo.y)},slice*Mathf.PI*2/slices,(slice+1)*Mathf.PI*2/slices,3));
            }
            float baseY=stemmed?.20f:0,baseTop=profile[0].y;
            for(int i=0;i<6;i++)Piece("Base "+i,Sector(new[]{new Vector2(.001f,baseY),new Vector2(profile[0].x,baseY),new Vector2(profile[0].x,baseTop),new Vector2(.001f,baseTop)},i*Mathf.PI/3,(i+1)*Mathf.PI/3,4));
            if(stemmed)
            {
                Piece("Stem",Sector(new[]{new Vector2(.001f,.02f),new Vector2(.012f,.02f),new Vector2(.012f,.21f),new Vector2(.001f,.21f)},0,Mathf.PI*2,16));
                for(int i=0;i<6;i++)Piece("Foot "+i,Sector(new[]{new Vector2(.001f,0),new Vector2(.09f,0),new Vector2(.09f,.012f),new Vector2(.015f,.023f),new Vector2(.001f,.023f)},i*Mathf.PI/3,(i+1)*Mathf.PI/3,4));
            }
            var combined=new Mesh();combined.CombineMeshes(combines.ToArray(),true,true);
            MeshPart(root,"Model",Store(name,combined),material,Vector3.zero,false);
            if(!breakable)return null;
            Presentation(debris);PropFragments.SetLayer(debris);return PrefabUtility.SaveAsPrefabAsset(debris,"Assets/Prefabs/"+name+"Broken.prefab");
        }
        finally {Object.DestroyImmediate(debris);}
    }
    static void MigrateCoffeeMug()
    {
        const string target="Assets/Prefabs/PickupCoffeeMug.prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(target)!=null)return;
        var old=PrefabUtility.LoadPrefabContents("Assets/Prefabs/PickupGlassMug.prefab");
        try
        {
            old.name="PickupCoffeeMug";
            var item=old.GetComponent<PickupItem>();
            var remains=Object.Instantiate(item.brokenPrefab);remains.name="CoffeeMugBroken";
            try
            {
                foreach(var renderer in remains.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=white;
                PropFragments.SetLayer(remains);
                item.brokenPrefab=PrefabUtility.SaveAsPrefabAsset(remains,"Assets/Prefabs/CoffeeMugBroken.prefab");
            }
            finally {Object.DestroyImmediate(remains);}
            foreach(var renderer in old.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=white;
            PrefabUtility.SaveAsPrefabAsset(old,target);created.Add(target);
        }
        finally {PrefabUtility.UnloadPrefabContents(old);}
    }
}
