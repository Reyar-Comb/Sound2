using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class CreateSonarHall
{
    public static object Main()
    {
        const string path = "Assets/Scenes/SonarHall.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) return "SonarHall already exists; not overwritten.";
        var current = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (current.isDirty) throw new System.Exception("Current scene has unsaved edits.");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/CircularHall.unity");
        if (!EditorSceneManager.SaveScene(scene, path, true)) throw new System.Exception("Scene copy failed.");
        scene = EditorSceneManager.OpenScene(path);
        var camera = Camera.main;
        var bounce = camera.GetComponent<BouncePlaytest>();
        if (bounce != null) bounce.enabled = false;
        foreach (var light in Object.FindObjectsByType<Light>()) light.enabled = false;
        var black = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>())
        {
            var materials = renderer.sharedMaterials;
            for (int i=0; i<materials.Length; i++) materials[i]=black;
            renderer.sharedMaterials=materials;
        }
        var hum = Object.FindAnyObjectByType<HallHumEmitter>();
        if (hum != null) { hum.humEnabled=false; hum.GetComponent<Renderer>().enabled=false; }
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        camera.cullingMask=-1;
        camera.nearClipPlane=.03f;
        camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing=false;
        var style = camera.gameObject.AddComponent<BlackBoxProximitySource>();
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Face, true);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Edge, true);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars, false);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Twinkle, false);
        var sonar=camera.gameObject.AddComponent<SphericalSonar>();
        sonar.renderingSettings=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/SonarDots.mat");
        camera.transform.position=new Vector3(0,1.6f,0);
        camera.transform.rotation=Quaternion.Euler(0,65,0);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Selection.activeGameObject=camera.gameObject;
        return new {scene=path, sonar.range, sonar.waveSpeed, sonar.afterglow};
    }
}
