using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Object = UnityEngine.Object;

public static class ConfigureSoundShell
{
    public const string ScenePath = "Assets/Scenes/SoundAndShell.unity";
    static Material paper, ink;
    static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material ?? paper;
        return go;
    }
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        if (Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("An open scene contains unsaved edits.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)!=null) throw new InvalidOperationException("New scene already exists; do not overwrite edits.");
        if (!AssetDatabase.CopyAsset("Assets/Scenes/BaiYu-Test.unity", ScenePath)) throw new Exception("Cannot copy base scene.");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = Object.FindAnyObjectByType<PlayerController>();
        var menu = Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        Object.DestroyImmediate(Object.FindAnyObjectByType<LevelWorld>().gameObject);
        var world = new GameObject("Sound & Shell - Level World").AddComponent<LevelWorld>();
        world.player = player;
        menu.name = "SOUND & SHELL Entry"; menu.usePlayerView = true; menu.transitionSeconds = .55f;

        paper = new Material(Shader.Find("Sound2/Starting Room Paper")); paper.name = "Starting Room Paper";
        AssetDatabase.CreateAsset(paper,"Assets/LevelEditor/Materials/Adventure/StartingRoomPaper.mat");
        ink = AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Adventure/ElevatorInk.mat");
        var room = new GameObject("00 - Starting White Room").AddComponent<LevelLayout>();
        room.manualGeometry=true;
        room.transform.SetParent(world.transform,false);
        room.rooms.Add(new LevelRoom { name="起始白房",size=new Vector2(10,10) });
        world.levels.Add(room); world.startingLevel=room;
        var shell = new GameObject("White Box").transform; shell.SetParent(room.transform,false);
        // Real wall segments leave an unobstructed, jumpable window opposite the lift.
        Box(shell,"Floor",new Vector3(0,-.15f,0),new Vector3(10.4f,.3f,10.4f));
        Box(shell,"Ceiling",new Vector3(0,6.15f,0),new Vector3(10.4f,.3f,10.4f));
        Box(shell,"West Wall",new Vector3(-5.15f,3,0),new Vector3(.3f,6,10.4f));
        Box(shell,"East Wall",new Vector3(5.15f,3,0),new Vector3(.3f,6,10.4f));
        Box(shell,"Lift Wall Left",new Vector3(-3.5f,3,5.15f),new Vector3(3,6,.3f));
        Box(shell,"Lift Wall Right",new Vector3(3.5f,3,5.15f),new Vector3(3,6,.3f));
        Box(shell,"Lift Wall Header",new Vector3(0,4.6f,5.15f),new Vector3(4,2.8f,.3f));
        Box(shell,"Window Wall Left",new Vector3(-3.5f,3,-5.15f),new Vector3(3,6,.3f));
        Box(shell,"Window Wall Right",new Vector3(3.5f,3,-5.15f),new Vector3(3,6,.3f));
        Box(shell,"Window Sill",new Vector3(0,.375f,-5.15f),new Vector3(4,.75f,.3f));
        Box(shell,"Window Header",new Vector3(0,5.05f,-5.15f),new Vector3(4,1.9f,.3f));
        Box(shell,"Window Frame Left",new Vector3(-2,2.42f,-4.97f),new Vector3(.075f,3.45f,.08f),ink);
        Box(shell,"Window Frame Right",new Vector3(2,2.42f,-4.97f),new Vector3(.075f,3.45f,.08f),ink);
        Box(shell,"Window Frame Top",new Vector3(0,4.1f,-4.97f),new Vector3(4.075f,.075f,.08f),ink);
        Box(shell,"Window Frame Sill",new Vector3(0,.75f,-4.97f),new Vector3(4.075f,.075f,.08f),ink);
        var props = new GameObject("Props").transform; props.SetParent(room.transform,false);
        var cabin = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LevelElevator.prefab"),scene);
        cabin.name="Elevator - To School"; cabin.transform.SetParent(props,false);
        cabin.transform.localPosition=new Vector3(0,0,7.5f); cabin.transform.localRotation=Quaternion.Euler(0,180,0);
        var firstLift=cabin.GetComponent<LevelElevator>();
        var spawn = new GameObject("Starting Room Spawn").transform; spawn.SetParent(room.transform,false);
        var capsule=player.GetComponent<CharacterController>();
        spawn.localPosition=new Vector3(0,capsule.height*.5f-capsule.center.y+capsule.skinWidth+.02f,0);
        world.startingPoint=spawn; player.transform.position=spawn.position; player.transform.rotation=spawn.rotation;
        player.ViewTransform.localRotation=Quaternion.identity;
        var camera=player.GetComponentInChildren<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        camera.farClipPlane=300;
        var opening=camera.gameObject.AddComponent<SoundShellOpening>();
        opening.player=player;opening.world=world;opening.startingRoom=room;opening.spawn=spawn;
        opening.foolCard=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Cards/0-the-fool.png");

        string[] paths={"School","Library","Home","Grocery","Wedding","Hospital","Boss"};
        string[] labels={"学校","图书馆","家","超市","婚礼","医院","Boss"};
        var departure=firstLift;
        for(int i=0;i<paths.Length;i++)
        {
            var imported=EditorSceneManager.OpenScene("Assets/Levels/Level"+paths[i]+".unity",OpenSceneMode.Additive);
            var original=imported.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>(true)).Single();
            // Keep loose authored geometry too. Reparent only this temporary loaded copy; never save the source.
            foreach(var root in imported.GetRootGameObjects())
            {
                if(root==original.gameObject || root.GetComponent<PlayerController>()!=null || root.GetComponent<Light>()!=null)continue;
                root.transform.SetParent(original.generatedRoot!=null?original.generatedRoot.transform:original.transform,true);
            }
            var level=Object.Instantiate(original.gameObject).GetComponent<LevelLayout>();
            level.manualGeometry=true;
            level.transform.SetParent(null,true); SceneManager.MoveGameObjectToScene(level.gameObject,scene);
            EditorSceneManager.CloseScene(imported,true);
            level.name=(i+1).ToString("00")+" - "+labels[i]; level.transform.SetParent(world.transform,false);
            level.transform.localPosition=new Vector3((i+1)*1000,0,0);
            world.levels.Add(level);
            var elevators=level.GetComponentsInChildren<LevelElevator>(true).OrderBy(e=>e.transform.localPosition.z).ToArray();
            // Library's hierarchy lists its departure first; world-space Z resolves the authored endpoints.
            elevators= elevators.OrderBy(e=>level.transform.InverseTransformPoint(e.transform.position).z).ToArray();
            if(elevators.Length==0)throw new Exception("No elevator in "+paths[i]);
            var arrival=elevators[0];
            world.connections.Add(new ElevatorConnection {source=departure,destination=arrival,travelSeconds=3,bidirectional=false});
            departure=elevators[elevators.Length-1];
            level.gameObject.SetActive(false);
        }
        SceneManager.SetActiveScene(scene);
        var profile=ScriptableObject.CreateInstance<FloorProgressionProfile>(); profile.applySonarDifficulty=false;
        profile.defaultEntryText="";
        profile.overrides.Add(new FloorRule {floor=1});
        for(int i=0;i<labels.Length;i++)profile.overrides.Add(new FloorRule {floor=i+2,difficulty=1+.15f*i,
            entrySubtitles=new List<SubtitleCue>{new SubtitleCue(labels[i],4)}});
        AssetDatabase.CreateAsset(profile,"Assets/Resources/SoundShellFloorProgression.asset");
        world.profile=profile; player.GetComponent<PlayerFloorProgression>().profile=profile;
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)}.Concat(EditorBuildSettings.scenes.Where(s=>s.path!=ScenePath)).ToArray();
        Selection.activeGameObject=room.gameObject;
        return new {scene=ScenePath,levels=world.levels.Count,connections=world.connections.Count};
    }

    public static object PreserveAuthoredGeometry()
    {
        if(Application.isPlaying)throw new Exception("Exit Play mode first.");
        var world=Object.FindAnyObjectByType<LevelWorld>();
        if(world.gameObject.scene.path!=ScenePath)throw new Exception("Wrong scene.");
        var room=world.startingLevel;room.manualGeometry=true;
        if(room.generatedRoot!=null)Object.DestroyImmediate(room.generatedRoot);
        room.generatedRoot=null;
        string[] paths={"School","Library","Home","Grocery","Wedding","Hospital","Boss"};
        for(int i=0;i<paths.Length;i++)
        {
            var old=world.levels[i+1];
            var oldLifts=old.GetComponentsInChildren<LevelElevator>(true).OrderBy(e=>old.transform.InverseTransformPoint(e.transform.position).z).ToArray();
            var imported=EditorSceneManager.OpenScene("Assets/Levels/Level"+paths[i]+".unity",OpenSceneMode.Additive);
            var original=imported.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>(true)).Single();
            foreach(var root in imported.GetRootGameObjects())
            {
                if(root==original.gameObject || root.GetComponent<PlayerController>()!=null || root.GetComponent<Light>()!=null)continue;
                root.transform.SetParent(original.generatedRoot!=null?original.generatedRoot.transform:original.transform,true);
            }
            var level=Object.Instantiate(original.gameObject).GetComponent<LevelLayout>();level.manualGeometry=true;
            level.transform.SetParent(null,true);SceneManager.MoveGameObjectToScene(level.gameObject,world.gameObject.scene);
            EditorSceneManager.CloseScene(imported,true);
            level.name=old.name;level.transform.SetParent(world.transform,false);level.transform.localPosition=old.transform.localPosition;
            var lifts=level.GetComponentsInChildren<LevelElevator>(true).OrderBy(e=>level.transform.InverseTransformPoint(e.transform.position).z).ToArray();
            foreach(var c in world.connections)
            {
                int source=Array.IndexOf(oldLifts,c.source),destination=Array.IndexOf(oldLifts,c.destination);
                if(source>=0)c.source=lifts[source];if(destination>=0)c.destination=lifts[destination];
            }
            world.levels[i+1]=level;Object.DestroyImmediate(old.gameObject);level.gameObject.SetActive(false);
        }
        Selection.activeGameObject=world.player.gameObject;
        SceneManager.SetActiveScene(world.gameObject.scene);
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);EditorSceneManager.SaveScene(world.gameObject.scene);
        return "Preserved authored geometry in all eight levels";
    }
}
