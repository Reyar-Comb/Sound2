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
        var style = camera.GetComponent<BlackBoxProximitySource>();
        if (style == null) style = camera.gameObject.AddComponent<BlackBoxProximitySource>();
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Face, true);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Edge, true);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars, false);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Twinkle, false);
        var sonar=camera.GetComponent<SphericalSonar>();
        if (sonar == null) sonar=camera.gameObject.AddComponent<SphericalSonar>();
        sonar.renderingSettings=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/SonarDots.mat");
        camera.transform.position=new Vector3(0,1.6f,0);
        camera.transform.rotation=Quaternion.Euler(0,65,0);
        sonar = UsePlayerPrefab(camera);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Selection.activeGameObject=sonar.GetComponentInParent<PlayerController>().gameObject;
        return new {scene=path, sonar.range, sonar.waveSpeed, sonar.afterglow};
    }

    // Run with --entry CreateSonarHall.ReusePlayer to migrate an existing hall without regenerating it.
    public static object ReusePlayer()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play mode before migrating.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isDirty) throw new System.InvalidOperationException("Save the current scene before migrating.");
        if (scene.path != "Assets/Scenes/SonarHall.unity")
            scene = EditorSceneManager.OpenScene("Assets/Scenes/SonarHall.unity");
        var sonar = Object.FindAnyObjectByType<SphericalSonar>();
        if (sonar == null) throw new System.InvalidOperationException("SonarHall has no sonar camera.");
        if (sonar.GetComponentInParent<PlayerController>() != null) return "SonarHall already uses Player.";
        sonar = UsePlayerPrefab(sonar.GetComponent<Camera>());
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("Could not save SonarHall.");
        Selection.activeGameObject = sonar.GetComponentInParent<PlayerController>().gameObject;
        return new { scene = scene.path, player = Selection.activeGameObject.name,
            sonar.range, sonar.waveSpeed, sonar.afterglow, obstacleLayers = sonar.obstacleLayers.value };
    }

    static SphericalSonar UsePlayerPrefab(Camera previousCamera)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var black = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        if (prefab == null || black == null) throw new System.Exception("Player prefab or PureBlack material missing.");
        var previousSonar = previousCamera.GetComponent<SphericalSonar>();
        var previousBounce = previousCamera.GetComponent<BouncePlaytest>();
        var scene = previousCamera.gameObject.scene;
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(player, "Reuse BlackBox player in SonarHall");
        var camera = player.GetComponentInChildren<Camera>();
        var controller = player.GetComponent<CharacterController>();
        var oldPosition = previousCamera.transform.position;
        var oldAngles = previousCamera.transform.eulerAngles;
        player.transform.rotation = Quaternion.Euler(0, oldAngles.y, 0);
        camera.transform.localRotation = Quaternion.Euler(Mathf.DeltaAngle(0, oldAngles.x), 0, 0);
        player.transform.position = oldPosition - player.transform.rotation * camera.transform.localPosition;
        // The shared capsule is taller than the old camera's capsule. Keep its base above the floor.
        if (Physics.Raycast(oldPosition, Vector3.down, out var floor, controller.height + 1,
            previousSonar.obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            var position = player.transform.position;
            position.y = Mathf.Max(position.y, floor.point.y + controller.height * .5f
                - controller.center.y + controller.skinWidth);
            player.transform.position = position;
        }
        foreach (var child in player.GetComponentsInChildren<Transform>())
        {
            // Match the old sonar camera: the player must not block object-origin pulses.
            child.gameObject.layer = previousCamera.gameObject.layer;
            PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(child);
        }
        foreach (var renderer in player.GetComponentsInChildren<MeshRenderer>())
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = black;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        EditorUtility.CopySerialized(previousCamera, camera);
        var previousData = previousCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var cameraData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        if (previousData != null) EditorUtility.CopySerialized(previousData, cameraData);
        PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        PrefabUtility.RecordPrefabInstancePropertyModifications(cameraData);
        var previousListener = previousCamera.GetComponent<SteamAudio.SteamAudioListener>();
        if (previousListener != null)
            EditorUtility.CopySerialized(previousListener, camera.gameObject.AddComponent<SteamAudio.SteamAudioListener>());

        var sonar = camera.GetComponent<SphericalSonar>();
        if (sonar == null) sonar = camera.gameObject.AddComponent<SphericalSonar>();
        EditorUtility.CopySerialized(previousSonar, sonar);
        if (player.GetComponent<FootstepEmitter>() == null) player.AddComponent<FootstepEmitter>();
        var style = camera.GetComponent<BlackBoxProximitySource>();
        EditorUtility.CopySerialized(previousCamera.GetComponent<BlackBoxProximitySource>(), style);
        style.TrackingPoint = camera.transform;
        foreach (var emitter in Object.FindObjectsByType<SonarObjectEmitter>())
            if (emitter.sonar == previousSonar) { Undo.RecordObject(emitter, "Rebind sonar"); emitter.sonar = sonar; }

        // HallHum still reads its audio switches from the disabled legacy adapter.
        // Preserve that configuration without enabling its old movement or projectile controls.
        BouncePlaytest audioSettings = null;
        foreach (var hum in Object.FindObjectsByType<HallHumEmitter>())
            if (previousBounce != null && hum.controls == previousBounce)
            {
                if (audioSettings == null)
                {
                    audioSettings = camera.gameObject.AddComponent<BouncePlaytest>();
                    EditorUtility.CopySerialized(previousBounce, audioSettings);
                    audioSettings.enabled = false;
                }
                Undo.RecordObject(hum, "Rebind hall audio listener");
                hum.controls = audioSettings;
            }
        Undo.DestroyObjectImmediate(previousCamera.gameObject);
        style.PublishPosition();
        return sonar;
    }
}
