using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class ConfigureBreakableProps
{
    const string Folder="Assets/Models/BreakableProps";
    static Material metal,face,glass,black;

    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Exit Play mode before authoring.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        metal=Material("AlarmMetal",new Color(.55f,.64f,.68f));
        face=Material("AlarmFace",new Color(.88f,.84f,.69f));
        glass=Material("GlassPreview",new Color(.42f,.75f,.82f));
        black=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        CreateAlarm();CreateMug();FixAlarmColliders();AssetDatabase.SaveAssets();
        return new {alarm="Assets/Prefabs/PickupAlarmClock.prefab",mug="Assets/Prefabs/PickupGlassMug.prefab"};
    }

    static Material Material(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null)
        {
            material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};
            material.SetFloat("_Smoothness",.7f);AssetDatabase.CreateAsset(material,path);
        }
        return material;
    }

    static Mesh Store(string name,Mesh mesh)
    {
        return PropMeshAssets.Store(Folder,name,mesh,false);
    }

    static GameObject Part(GameObject parent,string name,Mesh mesh,Material material,Vector3 position,Quaternion rotation,Vector3 scale)
    {
        var part=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        part.transform.SetParent(parent.transform,false);part.transform.localPosition=position;
        part.transform.localRotation=rotation;part.transform.localScale=scale;
        part.GetComponent<MeshFilter>().sharedMesh=mesh;part.GetComponent<MeshRenderer>().sharedMaterial=material;
        return part;
    }

    static GameObject Primitive(GameObject parent,string name,PrimitiveType type,Material material,Vector3 position,Vector3 scale,Quaternion rotation=default)
    {
        var part=GameObject.CreatePrimitive(type);part.name=name;
        part.transform.SetParent(parent.transform,false);part.transform.localPosition=position;
        part.transform.localRotation=rotation==default?Quaternion.identity:rotation;part.transform.localScale=scale;
        part.GetComponent<MeshRenderer>().sharedMaterial=material;
        if(type==PrimitiveType.Cylinder)
        {
            Object.DestroyImmediate(part.GetComponent<Collider>());
            var collider=part.AddComponent<MeshCollider>();collider.sharedMesh=part.GetComponent<MeshFilter>().sharedMesh;collider.convex=true;
        }
        return part;
    }

    static void FixAlarmColliders()
    {
        foreach(string name in new[]{"PickupAlarmClock","AlarmClockBroken"})
        {
            string path="Assets/Prefabs/"+name+".prefab";var root=PrefabUtility.LoadPrefabContents(path);bool changed=false;
            try
            {
                foreach(string partName in new[]{"Case","Face"})
                {
                    var part=root.transform.Find(partName).gameObject;var capsule=part.GetComponent<CapsuleCollider>();if(capsule==null)continue;
                    Object.DestroyImmediate(capsule);var collider=part.AddComponent<MeshCollider>();collider.sharedMesh=part.GetComponent<MeshFilter>().sharedMesh;collider.convex=true;changed=true;
                }
                if(changed)PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
    }

    static void PhysicalPart(GameObject part,float mass)
    {
        if(part.GetComponentsInChildren<Collider>().Length==0)
            foreach(var filter in part.GetComponentsInChildren<MeshFilter>())
            {var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;collider.convex=true;}
        var body=part.AddComponent<Rigidbody>();body.mass=mass;body.interpolation=RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        body.maxDepenetrationVelocity=2;
    }

    static void Presentation(GameObject root,Vector3 anchor)
    {
        var prop=root.AddComponent<LevelProp>();prop.surface=PropSurface.Floor;
        prop.surfaceAnchor=anchor;prop.gameplayMaterial=black;
    }

    static PickupItem Pickup(GameObject root,GameObject broken,float threshold,float mass,Vector3 originPosition)
    {
        var body=root.AddComponent<Rigidbody>();body.mass=mass;body.interpolation=RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        var audio=root.AddComponent<AudioSource>();audio.playOnAwake=false;audio.spatialBlend=1;
        var cue=root.AddComponent<SonarCueSource>();cue.output=audio;cue.generateDefaultPing=false;
        var item=root.AddComponent<PickupItem>();item.breakable=true;item.breakSpeed=threshold;item.brokenPrefab=broken;
        item.fragmentLifetime=0;item.breakPulseRange=10;item.breakHearingRange=16;
        var emitter=root.GetComponent<SonarObjectEmitter>();emitter.automatic=false;emitter.toggleKey=Key.None;
        emitter.range=5;emitter.soundRange=8;emitter.waveSpeed=7;emitter.afterglow=.5f;
        var origin=new GameObject("Sound Origin").transform;origin.SetParent(root.transform,false);origin.localPosition=originPosition;
        emitter.emissionOrigin=origin;
        return item;
    }

    static void CreateAlarm()
    {
        const string path="Assets/Prefabs/PickupAlarmClock.prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)return;
        var stage=EditorSceneManager.NewPreviewScene();GameObject root=null,debris=null;
        try
        {
            root=new GameObject("PickupAlarmClock");SceneManager.MoveGameObjectToScene(root,stage);
            Primitive(root,"Case",PrimitiveType.Cylinder,metal,new Vector3(0,0,-.045f),new Vector3(.6f,.035f,.6f),Quaternion.Euler(90,0,0));
            Primitive(root,"Face",PrimitiveType.Cylinder,face,new Vector3(0,0,.005f),new Vector3(.55f,.012f,.55f),Quaternion.Euler(90,0,0));
            for(int i=0;i<4;i++)
            {
                var mesh=Store("AlarmRim"+i,Annular(i*Mathf.PI/2,(i+1)*Mathf.PI/2,.285f,.325f,0,.055f,12));
                var part=Part(root,"Rim "+i,mesh,metal,Vector3.zero,Quaternion.identity,Vector3.one);
                var collider=part.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;
            }
            for(int sign=-1;sign<=1;sign+=2)
            {
                Primitive(root,sign<0?"Left Bell":"Right Bell",PrimitiveType.Sphere,metal,new Vector3(sign*.215f,.345f,-.015f),new Vector3(.23f,.15f,.19f));
                Primitive(root,sign<0?"Left Foot":"Right Foot",PrimitiveType.Cube,metal,new Vector3(sign*.18f,-.32f,-.01f),new Vector3(.065f,.17f,.12f),Quaternion.Euler(0,0,sign*18));
            }
            Primitive(root,"Alarm Button",PrimitiveType.Cube,metal,new Vector3(0,.345f,-.015f),new Vector3(.06f,.09f,.06f));
            Transform Hand(string name,float length,float width,float z,float angle)
            {
                var pivot=new GameObject(name).transform;pivot.SetParent(root.transform,false);pivot.localPosition=new Vector3(0,0,z);
                pivot.localRotation=Quaternion.Euler(0,0,angle);
                Primitive(pivot.gameObject,"Bar",PrimitiveType.Cube,metal,new Vector3(0,length*.5f,0),new Vector3(width,length,.018f));
                return pivot;
            }
            var longHand=Hand("Long Hand",.235f,.022f,.035f,0);
            var shortHand=Hand("Short Hand",.15f,.035f,.058f,90);
            debris=Object.Instantiate(root);debris.name="AlarmClockBroken";
            foreach(Transform part in debris.transform)PhysicalPart(part.gameObject,.06f);
            Presentation(debris,new Vector3(0,-.41f,0));
            PropFragments.SetLayer(debris);
            var broken=PrefabUtility.SaveAsPrefabAsset(debris,"Assets/Prefabs/AlarmClockBroken.prefab");
            // A compound collider keeps the front and feet usable without a hollow convex-hull enclosing other parts.
            Pickup(root,broken,7,.7f,new Vector3(0,.08f,.14f)).heldEulerAngles=new Vector3(0,180,0);
            var clock=root.AddComponent<TickingClock>();clock.longHand=longHand;clock.shortHand=shortHand;clock.tickInterval=1;
            root.AddComponent<InteractableObject>();
            var alarm=root.AddComponent<PickupAlarm>();alarm.delayAfterPickup=5;alarm.ringInterval=.2f;alarm.ringRange=12;
            Presentation(root,new Vector3(0,-.411f,0));
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally {if(root!=null)Object.DestroyImmediate(root);if(debris!=null)Object.DestroyImmediate(debris);EditorSceneManager.ClosePreviewScene(stage);}
    }

    static void CreateMug()
    {
        const string path="Assets/Prefabs/PickupGlassMug.prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)return;
        var stage=EditorSceneManager.NewPreviewScene();GameObject root=null,debris=null;
        try
        {
            debris=new GameObject("GlassMugBroken");SceneManager.MoveGameObjectToScene(debris,stage);
            var combines=new List<CombineInstance>();
            void Shard(string name,Mesh mesh)
            {
                var center=mesh.bounds.center;var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++)vertices[i]-=center;
                mesh.vertices=vertices;mesh.RecalculateBounds();mesh=Store(name,mesh);
                var part=Part(debris,name,mesh,glass,center,Quaternion.identity,Vector3.one);
                PhysicalPart(part,.012f);
                combines.Add(new CombineInstance {mesh=mesh,transform=Matrix4x4.Translate(center)});
            }
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;
                float seamA=.22f+Mathf.Sin(i*2.1f)*.06f,seamB=.22f+Mathf.Sin((i+1)*2.1f)*.06f;
                Shard("MugWallLower"+i,CupWall(a,b,.035f,.035f,seamA,seamB));
                Shard("MugWallUpper"+i,CupWall(a,b,seamA,seamB,.42f,.42f));
            }
            for(int i=0;i<6;i++)Shard("MugBase"+i,DiskWedge(i*Mathf.PI/3,(i+1)*Mathf.PI/3,.17f,.035f));
            for(int i=0;i<3;i++)Shard("MugHandle"+i,Handle(-Mathf.PI/2+i*Mathf.PI/3,-Mathf.PI/2+(i+1)*Mathf.PI/3));
            Presentation(debris,Vector3.zero);
            PropFragments.SetLayer(debris);
            var broken=PrefabUtility.SaveAsPrefabAsset(debris,"Assets/Prefabs/GlassMugBroken.prefab");
            root=new GameObject("PickupGlassMug");SceneManager.MoveGameObjectToScene(root,stage);
            var complete=new Mesh();complete.CombineMeshes(combines.ToArray(),true,true);complete=Store("GlassMug",complete);
            var model=Part(root,"Cup and Handle",complete,glass,Vector3.zero,Quaternion.identity,Vector3.one);
            // Compound wall colliders leave the top and handle open while staying valid for a dynamic rigidbody.
            foreach(Transform shard in debris.transform)
            {
                var colliderObject=new GameObject(shard.name+" Collider");colliderObject.transform.SetParent(root.transform,false);
                colliderObject.transform.localPosition=shard.localPosition;
                var collider=colliderObject.AddComponent<MeshCollider>();collider.sharedMesh=shard.GetComponent<MeshFilter>().sharedMesh;collider.convex=true;
            }
            Pickup(root,broken,3.8f,.25f,new Vector3(0,.45f,0));
            var interactionAnchor=new GameObject("Interaction Anchor").transform;
            interactionAnchor.SetParent(root.transform,false);interactionAnchor.localPosition=new Vector3(0,.21f,0);
            root.GetComponent<PickupItem>().interactionAnchor=interactionAnchor;
            Presentation(root,Vector3.zero);
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally {if(root!=null)Object.DestroyImmediate(root);if(debris!=null)Object.DestroyImmediate(debris);EditorSceneManager.ClosePreviewScene(stage);}
    }

    sealed class MeshBuilder
    {
        readonly List<Vector3> points=new();readonly List<int> triangles=new();
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0){var swap=b;b=d;d=swap;}
            int n=points.Count;points.AddRange(new[]{a,b,c,d});triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        public Mesh Build(){var m=new Mesh();m.SetVertices(points);m.SetTriangles(triangles,0);m.RecalculateNormals();m.RecalculateBounds();return m;}
    }

    static Mesh Annular(float a,float b,float inner,float outer,float back,float front,int segments)
    {
        var m=new MeshBuilder();Vector3 P(float t,float r,float z)=>new Vector3(Mathf.Cos(t)*r,Mathf.Sin(t)*r,z);
        for(int i=0;i<segments;i++)
        {
            float t=Mathf.Lerp(a,b,(float)i/segments),u=Mathf.Lerp(a,b,(float)(i+1)/segments);var n=P((t+u)*.5f,1,0);
            m.Quad(P(t,outer,back),P(u,outer,back),P(u,outer,front),P(t,outer,front),n);
            m.Quad(P(t,inner,back),P(u,inner,back),P(u,inner,front),P(t,inner,front),-n);
            m.Quad(P(t,outer,front),P(u,outer,front),P(u,inner,front),P(t,inner,front),Vector3.forward);
            m.Quad(P(t,outer,back),P(u,outer,back),P(u,inner,back),P(t,inner,back),Vector3.back);
        }
        m.Quad(P(a,inner,back),P(a,outer,back),P(a,outer,front),P(a,inner,front),new Vector3(Mathf.Sin(a),-Mathf.Cos(a),0));
        m.Quad(P(b,inner,back),P(b,outer,back),P(b,outer,front),P(b,inner,front),new Vector3(-Mathf.Sin(b),Mathf.Cos(b),0));return m.Build();
    }

    static Mesh CupWall(float a,float b,float lowA,float lowB,float highA,float highB)
    {
        var m=new MeshBuilder();Vector3 P(float t,float r,float y)=>new Vector3(Mathf.Cos(t)*r,y,Mathf.Sin(t)*r);
        for(int i=0;i<4;i++)
        {
            float f=i/4f,g=(i+1)/4f,t=Mathf.Lerp(a,b,f),u=Mathf.Lerp(a,b,g);
            float lo=Mathf.Lerp(lowA,lowB,f),lu=Mathf.Lerp(lowA,lowB,g),hi=Mathf.Lerp(highA,highB,f),hu=Mathf.Lerp(highA,highB,g);
            var n=P((t+u)*.5f,1,0);
            m.Quad(P(t,.17f,lo),P(u,.17f,lu),P(u,.17f,hu),P(t,.17f,hi),n);
            m.Quad(P(t,.145f,lo),P(u,.145f,lu),P(u,.145f,hu),P(t,.145f,hi),-n);
            m.Quad(P(t,.145f,lo),P(u,.145f,lu),P(u,.17f,lu),P(t,.17f,lo),Vector3.down);
            m.Quad(P(t,.145f,hi),P(u,.145f,hu),P(u,.17f,hu),P(t,.17f,hi),Vector3.up);
        }
        m.Quad(P(a,.145f,lowA),P(a,.17f,lowA),P(a,.17f,highA),P(a,.145f,highA),new Vector3(Mathf.Sin(a),0,-Mathf.Cos(a)));
        m.Quad(P(b,.145f,lowB),P(b,.17f,lowB),P(b,.17f,highB),P(b,.145f,highB),new Vector3(-Mathf.Sin(b),0,Mathf.Cos(b)));return m.Build();
    }

    static Mesh DiskWedge(float a,float b,float radius,float height)
    {
        var m=new MeshBuilder();Vector3 P(float t,float y)=>new Vector3(Mathf.Cos(t)*radius,y,Mathf.Sin(t)*radius);
        for(int i=0;i<8;i++)
        {
            float t=Mathf.Lerp(a,b,i/8f),u=Mathf.Lerp(a,b,(i+1)/8f);
            m.Quad(Vector3.zero,P(t,0),P(u,0),Vector3.zero,Vector3.down);
            m.Quad(Vector3.up*height,P(t,height),P(u,height),Vector3.up*height,Vector3.up);
            m.Quad(P(t,0),P(u,0),P(u,height),P(t,height),P((t+u)*.5f,0).normalized);
        }
        m.Quad(Vector3.zero,P(a,0),P(a,height),Vector3.up*height,new Vector3(Mathf.Sin(a),0,-Mathf.Cos(a)));
        m.Quad(Vector3.zero,P(b,0),P(b,height),Vector3.up*height,new Vector3(-Mathf.Sin(b),0,Mathf.Cos(b)));return m.Build();
    }

    static Mesh Handle(float a,float b)
    {
        var m=new MeshBuilder();
        Vector3 Center(float t)=>new Vector3(.15f+Mathf.Cos(t)*.13f,.23f+Mathf.Sin(t)*.12f,0);
        Vector3 P(float t,float v)=>Center(t)+new Vector3(Mathf.Cos(t)*Mathf.Cos(v),Mathf.Sin(t)*Mathf.Cos(v),Mathf.Sin(v))*.025f;
        for(int i=0;i<8;i++)for(int j=0;j<8;j++)
        {
            float t=Mathf.Lerp(a,b,i/8f),u=Mathf.Lerp(a,b,(i+1)/8f),v=j*Mathf.PI/4,w=(j+1)*Mathf.PI/4;
            m.Quad(P(t,v),P(u,v),P(u,w),P(t,w),P((t+u)*.5f,(v+w)*.5f)-Center((t+u)*.5f));
        }
        for(int j=0;j<8;j++)
        {
            float v=j*Mathf.PI/4,w=(j+1)*Mathf.PI/4;
            m.Quad(Center(a),P(a,v),P(a,w),Center(a),new Vector3(Mathf.Sin(a),-Mathf.Cos(a),0));
            m.Quad(Center(b),P(b,v),P(b,w),Center(b),new Vector3(-Mathf.Sin(b),Mathf.Cos(b),0));
        }
        return m.Build();
    }

    public static object PlaceInTest()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/test.unity");
        if(!scene.IsValid()||!scene.isLoaded)throw new Exception("Open test scene first.");
        var layout=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>()).Single();
        var player=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PlayerController>()).Single();
        Directory.CreateDirectory("Logs/BreakableProps");
        if(!EditorSceneManager.SaveScene(scene,"Logs/BreakableProps/test-before-props.unity",true))throw new Exception("Snapshot failed.");
        var placed=new List<string>();
        foreach(var entry in new[]{("PickupAlarmClock",-.55f),("PickupGlassMug",.55f)})
        {
            var parent=LevelPropPlacement.PropsRoot(layout);
            if(parent!=null&&parent.Find(entry.Item1)!=null){placed.Add(entry.Item1+" already present");continue;}
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+entry.Item1+".prefab");
            var point=player.transform.position+player.transform.forward*1.25f+player.transform.right*entry.Item2;
            point.y=layout.transform.position.y+2;
            if(!LevelPropPlacement.TryPose(layout,prefab,new Ray(point,Vector3.down),PropSurface.Floor,.025f,180,out var pose))throw new Exception("No safe floor for "+entry.Item1);
            var instance=LevelPropPlacement.Place(layout,prefab,pose);placed.Add(instance.name+" at "+instance.transform.position);
        }
        EditorSceneManager.SaveScene(scene);return new {scene=scene.path,placed};
    }
}
