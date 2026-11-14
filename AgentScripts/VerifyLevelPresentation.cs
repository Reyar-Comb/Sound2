using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Object = UnityEngine.Object;

public static class VerifyLevelPresentation
{
    static readonly List<string> checks = new();
    static void Check(bool pass, string message)
    {
        checks.Add((pass ? "PASS: " : "FAIL: ") + message);
        if (!pass) throw new Exception(message);
    }
    static async Task Tick(int ms = 150)
    {
        int frame = Time.frameCount; await Task.Delay(ms);
        Check(Time.frameCount > frame, "Player loop advances");
    }

    public static object Edit()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode first.");
        checks.Clear();
        var original = SceneManager.GetActiveScene();
        const string exportPath = "Assets/Scenes/__LevelPresentationVerification.unity";
        const string exportFolder = "Assets/Scenes/__LevelPresentationVerification_Geometry";
        Check(!File.Exists(exportPath) && !Directory.Exists(exportFolder), "Temporary export destination is unused");
        var scenes = new List<Scene>();
        try
        {
            foreach (string name in new[] { "LevelEditorDemo", "LevelEditorWorkspace", "test" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity", OpenSceneMode.Additive); scenes.Add(scene);
                var layout = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelLayout>()).Single();
                var roof = layout.generatedRoot.transform.Find("Ceiling");
                Check(roof != null && !roof.gameObject.activeSelf, name + ": edit preview is open-top after reload");
                Check(AssetDatabase.Contains(roof.GetComponent<MeshFilter>().sharedMesh), name + ": hidden ceiling mesh is saved for builds");
                Check(layout.generatedRoot.GetComponentsInChildren<MeshRenderer>().All(r => r.sharedMaterial != layout.gameplayMaterial), name + ": preview uses original colored materials");
                Check(layout.gameplayMaterial == AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat"), name + ": gameplay uses the existing black material");
                layout.SetGameplayPresentation(true); Physics.SyncTransforms();
                if (layout.rooms.Count > 0 || layout.corridors.Count > 0)
                {
                    var point = layout.rooms.Count > 0 ? layout.rooms[0].center : layout.corridors[0].points[0];
                    Check(roof.GetComponent<MeshCollider>().Raycast(new Ray(layout.World(point, 1), layout.transform.up), out var hit, layout.wallHeight + 1), name + ": ceiling collides from underneath");
                    Check(Mathf.Abs(layout.transform.InverseTransformPoint(hit.point).y - layout.wallHeight) < .001f, name + ": ceiling underside matches wall height");
                }
                foreach (var pit in layout.pits)
                    Check(!layout.generatedRoot.GetComponentsInChildren<MeshCollider>().Any(c => c.Raycast(new Ray(layout.World(pit.center, 1), -layout.transform.up), out _, pit.depth + 5)), name + ": floor shaft remains uncapped");
                layout.SetGameplayPresentation(false);
                foreach (var player in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerController>()))
                {
                    var sonar = player.GetComponentInChildren<SphericalSonar>();
                    Check(sonar != null && !sonar.proximityRendering && sonar.sonarRendering && sonar.renderingSettings != null, name + ": player uses existing sonar with proximity off");
                }
                if (name != "LevelEditorDemo") continue;
                SceneManager.SetActiveScene(scene);
                CaptureEditor(layout);
                string sourceData = JsonUtility.ToJson(layout.rooms[0]);
                LevelBuilder.Export(layout, exportPath, true);
                Check(JsonUtility.ToJson(layout.rooms[0]) == sourceData && !layout.generatedRoot.transform.Find("Ceiling").gameObject.activeSelf, "Export preserves source layout and open preview");
                var exported = EditorSceneManager.OpenScene(exportPath, OpenSceneMode.Additive); scenes.Add(exported);
                var copy = exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelLayout>()).Single();
                Check(copy.geometryAsset != layout.geometryAsset && !copy.generatedRoot.transform.Find("Ceiling").gameObject.activeSelf, "Fresh export has independent meshes and reopens in edit presentation");
                Check(copy.generatedRoot.GetComponentsInChildren<MeshFilter>(true).All(f => AssetDatabase.Contains(f.sharedMesh)), "Fresh export persists every mesh including the inactive roof");
                Check(exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SphericalSonar>()).Count() == 1, "Fresh export has exactly one sonar player");
            }
            foreach (int index in new[] { 0, 1, 5 })
            {
                var plan = SeededFloorPlan.Generate(index);
                var mesh = SeededFloorGeometry.Build(plan, 8, true);
                var probe = new GameObject("Seeded roof probe", typeof(MeshCollider));
                probe.transform.position = new Vector3(10000, 0, 10000);
                var collider = probe.GetComponent<MeshCollider>(); collider.sharedMesh = mesh;
                try
                {
                    Physics.SyncTransforms();
                    bool Up(Vector2 p) => collider.Raycast(new Ray(probe.transform.TransformPoint(new Vector3(p.x, 1, p.y)), Vector3.up), out _, 4);
                    Check(Up(new Vector2(2.5f, .5f)), "Seeded F" + index + ": room roof exists");
                    Check(Up(new Vector2(.5f, .5f)) == (index == 0), "Seeded F" + index + ": incoming shaft aperture is correct");
                    Check(Up(plan.Exit), "Seeded F" + index + ": outgoing pit still has a ceiling overhead");
                }
                finally { Object.DestroyImmediate(probe); Object.DestroyImmediate(mesh); }
                var preview = SeededFloorGeometry.Build(plan, 8);
                try { Check(preview.bounds.max.y <= SeededFloorGeometry.WallHeight + .001f, "Seeded F" + index + ": preview omits roof slab"); }
                finally { Object.DestroyImmediate(preview); }
            }
            return new { passed = true, checks };
        }
        finally
        {
            foreach (var scene in scenes) if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (File.Exists(exportPath)) AssetDatabase.DeleteAsset(exportPath);
            if (Directory.Exists(exportFolder)) AssetDatabase.DeleteAsset(exportFolder);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
        }
    }

    static void CaptureEditor(LevelLayout layout)
    {
        var go = new GameObject("Edit presentation capture", typeof(Camera));
        var camera = go.GetComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 24;
        camera.transform.position = new Vector3(40, 32, -30);
        camera.transform.LookAt(new Vector3(9, -1, 10));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .085f, .11f);
        var rt = new RenderTexture(960, 720, 24); var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); image.Apply();
            Directory.CreateDirectory("Logs/LevelPresentation");
            File.WriteAllBytes("Logs/LevelPresentation/editor-open.png", image.EncodeToPNG());
            Check(image.GetPixels32().Count(c => c.r > 60 || c.g > 60 || c.b > 60) > 1000, "Editor capture contains brightly shaded geometry");
        }
        finally { RenderTexture.active = previous; rt.Release(); Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
    }

    public static async Task<object> Play()
    {
        checks.Clear();
        if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
        foreach (string name in new[] { "LevelEditorDemo", "test" })
        {
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + name + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
            await Tick(200);
            var layout = Object.FindAnyObjectByType<LevelLayout>();
            var ceiling = layout.generatedRoot.transform.Find("Ceiling");
            Check(ceiling.gameObject.activeInHierarchy, name + ": Play mode automatically enables ceiling and collision");
            Check(layout.generatedRoot.GetComponentsInChildren<MeshRenderer>().All(r => r.sharedMaterial == layout.gameplayMaterial), name + ": every map surface is black at runtime");
            var sonar = Object.FindAnyObjectByType<SphericalSonar>();
            Check(sonar != null && sonar.PulseField != null && !sonar.proximityRendering, name + ": point-cloud pulse system runs without near reveal");
            Check(sonar.GetComponentInParent<PlayerController>().GetComponent<FootstepEmitter>() != null, name + ": shared footsteps and landing feedback are retained");
            var camera = sonar.GetComponent<Camera>();
            var player = sonar.GetComponentInParent<PlayerController>(); player.SetInputCaptured(false);
            var motion = player.GetComponent<PlayerCameraMotion>(); motion.enabled = false;
            camera.transform.rotation = Quaternion.Euler(-90, 0, 0);
            camera.transform.position = layout.World(layout.rooms[0].center, 1.6f);
            var rt = new RenderTexture(640, 360, 24); var read = new Texture2D(640, 360, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture;
            var counts = new List<int>();
            try
            {
                rt.Create(); camera.targetTexture = rt; sonar.ClearPulses(); sonar.automatic = false;
                for (int i = 0; i < 2; i++)
                {
                    if (i == 1) sonar.PulseField.EmitFrom(camera.transform.position, 6, 10000, 2);
                    await Tick(200);
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); read.Apply(); }
                    finally { RenderTexture.active = previous; }
                    counts.Add(read.GetPixels32().Count(c => c.r > 8 || c.g > 8 || c.b > 8));
                    Directory.CreateDirectory("Logs/LevelPresentation");
                    File.WriteAllBytes("Logs/LevelPresentation/" + name + (i == 0 ? "-dark" : "-ceiling-pulse") + ".png", read.EncodeToPNG());
                }
                Check(counts[0] == 0 && counts[1] > 100, name + ": ceiling is black until a pulse reveals dots (" + string.Join(",", counts) + ")");
            }
            finally { camera.targetTexture = oldTarget; rt.Release(); Object.Destroy(rt); Object.Destroy(read); }
        }
        // Actual capsule descent validates roof aperture alignment across streamed floor rotations.
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SeededMapDemo.unity", new LoadSceneParameters(LoadSceneMode.Single));
        await Tick(250);
        var map = Object.FindAnyObjectByType<LevelMap>(); var controller = Object.FindAnyObjectByType<PlayerController>();
        var progression = controller.GetComponent<PlayerFloorProgression>();
        controller.SetInputCaptured(true);
        for (int f = 0; f < 3; f++)
        {
            var floor = map.FindFloor(f);
            controller.Teleport(floor.transform.TransformPoint(new Vector3(floor.Plan.Exit.x, 1.2f, floor.Plan.Exit.y)));
            float deadline = Time.realtimeSinceStartup + 6;
            while ((progression.CurrentFloor == f || progression.AwaitingLanding) && Time.realtimeSinceStartup < deadline) await Task.Delay(80);
            Check(progression.CurrentFloor == f + 1 && !progression.AwaitingLanding && controller.GetComponent<CharacterController>().isGrounded,
                "Ceilings preserve real shaft descent and landing F" + f + " -> F" + (f + 1));
        }
        return new { passed = true, checks };
    }
}
