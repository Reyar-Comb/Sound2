using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public static class ConfigureWorldObjects
{
    static void Folder(string parent,string name) {if(!AssetDatabase.IsValidFolder(parent+"/"+name))AssetDatabase.CreateFolder(parent,name);}
    static GameObject CreateLightning(Material black)
    {
        var root=new GameObject("Ball Lightning");root.AddComponent<NavMeshAgent>().enabled=false;
        var body=GameObject.CreatePrimitive(PrimitiveType.Sphere);body.name="Lightning point surface";body.transform.SetParent(root.transform,false);
        body.transform.localPosition=new Vector3(0,1.35f,0);body.transform.localScale=Vector3.one*.9f;
        body.GetComponent<Collider>().isTrigger=true;Object.DestroyImmediate(body.GetComponent<MeshRenderer>());Object.DestroyImmediate(body.GetComponent<MeshFilter>());
        var pulse=root.AddComponent<SonarObjectEmitter>();pulse.toggleKey=Key.None;pulse.pulseKind=SonarPulseKind.Lightning;
        pulse.emissionOrigin=body.transform;pulse.playSound=false;pulse.interval=6;pulse.range=2;pulse.afterglow=.25f;
        pulse.distanceSettings=new SonarDistanceSettings {enabled=true,fadeStartDistance=.3f,fadeEndDistance=2,falloffPower=1,pulseStrength=.25f};
        var lightning=root.AddComponent<BallLightning>();lightning.visual=body.transform;
        return root;
    }
    public static object Main()
    {
        if(Application.isPlaying)throw new System.InvalidOperationException("Exit Play mode first.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/TeammateSonarHall.unity")throw new System.InvalidOperationException("Open SonarHall first.");
        var player=Object.FindAnyObjectByType<PlayerController>();
        var health=player.GetComponent<PlayerVitality>();if(health==null)health=player.gameObject.AddComponent<PlayerVitality>();
        if(player.GetComponent<PlayerStatusHUD>()==null)player.gameObject.AddComponent<PlayerStatusHUD>();
        Folder("Assets/Materials","Gameplay");
        var black=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        const string lightningPath="Assets/Prefabs/BallLightning.prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(lightningPath)==null)
        {
            var root=CreateLightning(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat"));
            PrefabUtility.SaveAsPrefabAsset(root,lightningPath);Object.DestroyImmediate(root);
        }
        var nav=Object.FindAnyObjectByType<RuntimePatrolNavigation>();
        var hazard=Object.FindAnyObjectByType<BallLightning>();
        if(hazard==null)
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(lightningPath),scene);
            root.transform.position=new Vector3(-3,0,2.5f);hazard=root.GetComponent<BallLightning>();
            hazard.navigation=nav;hazard.player=health;
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hazard);
        }
        foreach(var scan in Object.FindObjectsByType<MonsterScanEmitter>())
            scan.densitySettings=new SonarDensitySettings {enabled=true,fadeStartDistance=6,fadeEndDistance=10,falloffPower=1};
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Selection.activeGameObject=hazard.gameObject;
        return new {lightning=hazard.name,lightningPrefab=lightningPath};
    }
}
