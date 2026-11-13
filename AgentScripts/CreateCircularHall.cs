using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using SteamAudio;
using Vector3 = UnityEngine.Vector3;
using Material = UnityEngine.Material;

public static class CreateCircularHall
{
    const float Radius = 6;
    const float Height = 4;
    const float End = 12;
    public static string Main()
    {
        const string path = "Assets/Scenes/CircularHall.unity";
        var scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MazeRoom.unity") throw new InvalidOperationException("Open MazeRoom in Edit mode.");
        if (System.IO.File.Exists(path)) throw new InvalidOperationException("CircularHall exists.");
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Save failed.");
        if (!EditorSceneManager.SaveScene(scene,path,true)) throw new InvalidOperationException("Copy failed.");
        scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        UnityEngine.Object.DestroyImmediate(GameObject.Find("Maze Room (10m x 10m)"));
        UnityEngine.Object.DestroyImmediate(GameObject.Find("Navigation Markers"));
        const string folder="Assets/Materials/CircularHall";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials","CircularHall");
        var wall=MakeMaterial(folder+"/HallWalls.mat",new Color(0.47f,0.53f,0.58f));
        var floor=MakeMaterial(folder+"/Floor.mat",new Color(0.22f,0.27f,0.3f));
        var ceiling=MakeMaterial(folder+"/Ceiling.mat",new Color(0.72f,0.74f,0.76f));
        var east=MakeMaterial(folder+"/EastPassage.mat",new Color(0.28f,0.48f,0.58f));
        var south=MakeMaterial(folder+"/SouthPassage.mat",new Color(0.60f,0.43f,0.28f));
        var root=new GameObject("Circular Hall and Passages");
        var roof=new GameObject("Ceilings"); roof.transform.SetParent(root.transform);
        // Regular 72-sided prism: each wall has an inner apothem of six metres.
        float outerRadius=Radius/Mathf.Cos(2.5f*Mathf.Deg2Rad);
        float halfWidth=outerRadius*Mathf.Sin(15*Mathf.Deg2Rad);
        float join=outerRadius*Mathf.Cos(15*Mathf.Deg2Rad);
        float segment=2*(Radius+0.125f)*Mathf.Tan(2.5f*Mathf.Deg2Rad);
        for(int i=0;i<72;i++)
        {
            float angle=i*5+2.5f;
            if (Mathf.Abs(Mathf.DeltaAngle(angle,0))<15 || Mathf.Abs(Mathf.DeltaAngle(angle,270))<15) continue;
            float radians=angle*Mathf.Deg2Rad;
            var obj=Cube("Arc Wall "+i,new Vector3(Mathf.Cos(radians)*6.125f,2,Mathf.Sin(radians)*6.125f),new Vector3(0.25f,Height,segment+0.015f),wall,root.transform);
            obj.transform.rotation=Quaternion.Euler(0,-angle,0);
        }
        var mesh=Disc(outerRadius);
        AssetDatabase.CreateAsset(mesh,folder+"/HallSlab.asset");
        Slab("Hall Floor",new Vector3(0,-0.2f,0),mesh,floor,root.transform);
        Slab("Hall Ceiling",new Vector3(0,Height,0),mesh,ceiling,roof.transform);
        Passage("East (+X)",false,join,halfWidth,east,floor,ceiling,root.transform,roof.transform);
        Passage("South (-Z)",true,join,halfWidth,south,floor,ceiling,root.transform,roof.transform);
        var geometry=root.AddComponent<SteamAudioGeometry>();
        geometry.exportAllChildren=true;
        geometry.material=AssetDatabase.LoadAssetAtPath<SteamAudioMaterial>("Assets/Audio/RoomAcoustics/RoomSurfaces.asset");
        var camera=Camera.main;
        var walker=camera.GetComponent<CharacterController>(); walker.enabled=false;
        camera.transform.position=new Vector3(0,1.6f,0);
        camera.transform.rotation=Quaternion.Euler(0,90,0); walker.enabled=true;
        var play=camera.GetComponent<BouncePlaytest>();
        play.movementMin=new Vector2(-6,-12); play.movementMax=new Vector2(12,6);
        play.simulateOcclusion=true; EditorUtility.SetDirty(play);
        var lighting=GameObject.Find("Room Lighting");
        foreach(var lamp in lighting.GetComponentsInChildren<Light>()) UnityEngine.Object.DestroyImmediate(lamp.gameObject);
        Lamp("Hall Light",new Vector3(0,3.5f,0),lighting.transform,14,2.8f);
        Lamp("East Passage Light",new Vector3(9,3.4f,0),lighting.transform,7,1.8f);
        Lamp("South Passage Light",new Vector3(0,3.4f,-9),lighting.transform,7,1.8f);
        var data=ScriptableObject.CreateInstance<SerializedData>();
        AssetDatabase.CreateAsset(data,"Assets/Audio/RoomAcoustics/CircularHallGeometry.asset");
        var staticMesh=UnityEngine.Object.FindAnyObjectByType<SteamAudioStaticMesh>();
        staticMesh.asset=data; staticMesh.sceneNameWhenExported=scene.name;
        SteamAudioManager.ExportScene(scene,false);
        EditorUtility.SetDirty(data); EditorUtility.SetDirty(staticMesh);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=root;
        if(SceneView.lastActiveSceneView!=null) SceneView.lastActiveSceneView.LookAt(new Vector3(2,0,-2),Quaternion.Euler(65,0,0),15);
        return "Saved "+path+"; hall diameter 12m, height 4m; east and south passages width "+(halfWidth*2)+"m, length "+(End-join)+"m; exported "+data.data.Length+" bytes; reverb mix "+play.reverbLevel;
    }
    static void Passage(string name,bool south,float join,float halfWidth,Material wall,Material floor,Material ceiling,Transform root,Transform roof)
    {
        float begin=join-0.04f, length=End-begin, mid=(begin+End)/2;
        Func<Vector3,Vector3> position=p=>south?new Vector3(p.z,p.y,-p.x):p;
        Func<Vector3,Vector3> scale=p=>south?new Vector3(p.z,p.y,p.x):p;
        foreach(int sign in new[]{-1,1}) Cube(name+" Side "+sign,position(new Vector3(mid,2,sign*(halfWidth+0.125f))),scale(new Vector3(length,Height,0.25f)),wall,root);
        Cube(name+" End",position(new Vector3(End+0.125f,2,0)),scale(new Vector3(0.25f,Height,halfWidth*2+0.5f)),wall,root);
        Cube(name+" Floor",position(new Vector3(mid,-0.1f,0)),scale(new Vector3(length,0.2f,halfWidth*2)),floor,root);
        Cube(name+" Ceiling",position(new Vector3(mid,Height+0.1f,0)),scale(new Vector3(length,0.2f,halfWidth*2)),ceiling,roof);
    }
    static Mesh Disc(float radius)
    {
        var vertices=new System.Collections.Generic.List<Vector3>();
        var triangles=new System.Collections.Generic.List<int>();
        for(int i=0;i<72;i++)
        {
            float a=i*5*Mathf.Deg2Rad,b=(i+1)*5*Mathf.Deg2Rad;
            Vector3 p=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius),q=new Vector3(Mathf.Cos(b)*radius,0,Mathf.Sin(b)*radius);
            int n=vertices.Count;
            vertices.AddRange(new[]{Vector3.up*0.2f,p+Vector3.up*0.2f,q+Vector3.up*0.2f,Vector3.zero,p,q});
            triangles.AddRange(new[]{n,n+2,n+1,n+3,n+4,n+5,n+1,n+2,n+5,n+1,n+5,n+4});
        }
        var mesh=new Mesh {name="Circular Hall Slab"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static void Slab(string name,Vector3 position,Mesh mesh,Material material,Transform parent)
    {
        var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));obj.transform.SetParent(parent);obj.transform.position=position;
        obj.GetComponent<MeshFilter>().sharedMesh=mesh;obj.GetComponent<MeshRenderer>().sharedMaterial=material;obj.GetComponent<MeshCollider>().sharedMesh=mesh;obj.isStatic=true;
    }
    static Material MakeMaterial(string path,Color color)
    {
        var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",0.15f);AssetDatabase.CreateAsset(m,path);return m;
    }
    static GameObject Cube(string name,Vector3 position,Vector3 scale,Material material,Transform parent)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent);obj.transform.position=position;obj.transform.localScale=scale;obj.GetComponent<Renderer>().sharedMaterial=material;obj.isStatic=true;return obj;
    }
    static void Lamp(string name,Vector3 position,Transform parent,float range,float intensity)
    {
        var obj=new GameObject(name,typeof(Light));obj.transform.SetParent(parent);obj.transform.position=position;var light=obj.GetComponent<Light>();light.type=LightType.Point;light.range=range;light.intensity=intensity;light.shadows=LightShadows.None;
    }
}
