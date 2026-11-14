using UnityEditor;
using UnityEngine;

public static class ConfigureCoinPoints
{
    public static object Main()
    {
        if(Application.isPlaying)throw new System.InvalidOperationException("Exit Play mode first.");
        const string path="Assets/Prefabs/Coin.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var coin=root.GetComponent<CoinPickup>();
            coin.body.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            coin.body.enabled=false;
            coin.pointDensity=.16f;coin.waveTravelFraction=.65f;
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
        return new {prefab=path,scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
            sceneDirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty};
    }
}
