using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public static class ConfigureWorldObjects
{
    static void Folder(string parent,string name) {if(!AssetDatabase.IsValidFolder(parent+"/"+name))AssetDatabase.CreateFolder(parent,name);}
    static GameObject CreateCoin(Material gold)
    {
        var root=new GameObject("Coin");
        var pivot=new GameObject("Floating visual");pivot.transform.SetParent(root.transform,false);pivot.transform.localPosition=new Vector3(0,.9f,0);
        var mesh=GameObject.CreatePrimitive(PrimitiveType.Cylinder);mesh.name="Coin body (pulse reveal)";mesh.transform.SetParent(pivot.transform,false);
        mesh.transform.localRotation=Quaternion.Euler(90,0,0);mesh.transform.localScale=new Vector3(.55f,.05f,.55f);
        Object.DestroyImmediate(mesh.GetComponent<Collider>());
        var renderer=mesh.GetComponent<Renderer>();renderer.sharedMaterial=gold;renderer.enabled=false;
        var pulse=root.AddComponent<SonarObjectEmitter>();pulse.toggleKey=Key.None;pulse.pulseKind=SonarPulseKind.Coin;pulse.emissionOrigin=pivot.transform;
        pulse.interval=3;pulse.firstDelay=.7f;pulse.range=2;pulse.waveSpeed=5;pulse.afterglow=.45f;pulse.playSound=false;
        pulse.distanceSettings=new SonarDistanceSettings {enabled=true,fadeStartDistance=.2f,fadeEndDistance=2,falloffPower=1,pulseStrength=.8f};
        pulse.densitySettings=new SonarDensitySettings {enabled=true,fadeStartDistance=.7f,fadeEndDistance=2,falloffPower=1};
        var coin=root.AddComponent<CoinPickup>();coin.visual=pivot.transform;coin.body=renderer;
        return root;
    }
    static GameObject CreateLightning(Material black)
    {
        var root=new GameObject("Ball Lightning");root.AddComponent<NavMeshAgent>().enabled=false;
        var body=GameObject.CreatePrimitive(PrimitiveType.Sphere);body.name="Lightning point surface";body.transform.SetParent(root.transform,false);
        body.transform.localPosition=new Vector3(0,1.35f,0);body.transform.localScale=Vector3.one*.9f;
        body.GetComponent<Collider>().isTrigger=true;body.GetComponent<Renderer>().sharedMaterial=black;
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
        if(scene.path!="Assets/Scenes/SonarHall.unity")throw new System.InvalidOperationException("Open SonarHall first.");
        var player=Object.FindAnyObjectByType<PlayerController>();
        var wallet=player.GetComponent<PlayerWallet>();if(wallet==null)wallet=player.gameObject.AddComponent<PlayerWallet>();
        var health=player.GetComponent<PlayerVitality>();if(health==null)health=player.gameObject.AddComponent<PlayerVitality>();
        if(player.GetComponent<PlayerStatusHUD>()==null)player.gameObject.AddComponent<PlayerStatusHUD>();
        Folder("Assets/Materials","Gameplay");
        var gold=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Gameplay/CoinGold.mat");
        if(gold==null)
        {
            gold=new Material(Shader.Find("Universal Render Pipeline/Unlit"));gold.SetColor("_BaseColor",new Color(1,.75f,.08f,1));
            AssetDatabase.CreateAsset(gold,"Assets/Materials/Gameplay/CoinGold.mat");
        }
        const string coinPath="Assets/Prefabs/Coin.prefab", lightningPath="Assets/Prefabs/BallLightning.prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(coinPath)==null)
        {var root=CreateCoin(gold);PrefabUtility.SaveAsPrefabAsset(root,coinPath);Object.DestroyImmediate(root);}
        if(AssetDatabase.LoadAssetAtPath<GameObject>(lightningPath)==null)
        {
            var root=CreateLightning(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat"));
            PrefabUtility.SaveAsPrefabAsset(root,lightningPath);Object.DestroyImmediate(root);
        }
        if(Object.FindObjectsByType<CoinPickup>().Length==0)
        {
            var group=new GameObject("Coins");
            Vector3[] positions={new Vector3(1.8f,0,2.4f),new Vector3(-2,0,3),new Vector3(-4,0,-1),
                new Vector3(3,0,-4),new Vector3(8,0,0),new Vector3(0,0,-8)};
            for(int i=0;i<positions.Length;i++)
            {
                var coin=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(coinPath),scene);
                coin.name="Coin "+(i+1);coin.transform.SetParent(group.transform);coin.transform.position=positions[i];
                var pickup=coin.GetComponent<CoinPickup>();pickup.player=player;pickup.phaseOffset=i*.17f;
                var pulse=coin.GetComponent<SonarObjectEmitter>();pulse.firstDelay=.7f+i*.35f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(coin.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
                PrefabUtility.RecordPrefabInstancePropertyModifications(pulse);
            }
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
        return new {coins=Object.FindObjectsByType<CoinPickup>().Length,lightning=hazard.name,coinPrefab=coinPath,lightningPrefab=lightningPath};
    }
}
