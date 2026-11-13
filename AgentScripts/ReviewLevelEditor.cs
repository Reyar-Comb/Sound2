using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Object = UnityEngine.Object;

public static class ReviewLevelEditor
{
    public static string Main()
    {
        var original = SceneManager.GetActiveScene();
        var demo = EditorSceneManager.OpenScene("Assets/Scenes/LevelEditorDemo.unity", OpenSceneMode.Additive);
        GameObject cameraObject = null;
        RenderTexture texture = null;
        Texture2D image = null;
        var previous = RenderTexture.active;
        try
        {
            var layout = demo.GetRootGameObjects().Select(g => g.GetComponent<LevelLayout>()).First(l => l != null && l.rooms.Count > 0);
            foreach (var filter in layout.generatedRoot.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh == null || !AssetDatabase.Contains(filter.sharedMesh)) throw new Exception("Missing persisted mesh after reload");
            var sourcePlayer = demo.GetRootGameObjects().First(g => g.GetComponent<PlayerController>() != null);
            sourcePlayer.SetActive(false);
            cameraObject = new GameObject("Review camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, demo);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 21;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .085f, .11f);
            camera.transform.position = new Vector3(10, 60, 11);
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            texture = new RenderTexture(1280, 1080, 24);
            camera.targetTexture = texture;
            image = new Texture2D(1280, 1080, TextureFormat.RGB24, false);
            void Capture(string path)
            {
                camera.Render(); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1280, 1080), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            Capture("Logs/LevelEditorTop.png");
            camera.transform.position = new Vector3(40, 32, -30);
            camera.transform.LookAt(new Vector3(9, -1, 10)); camera.orthographicSize = 24;
            Capture("Logs/LevelEditor3D.png");
            // Exercise the actual PlayerController gravity branch without entering Play mode
            // or touching the user's current editing scene and input bindings.
            layout.transform.position = new Vector3(5000, 0, 5000);
            sourcePlayer.SetActive(true);
            var controller = sourcePlayer.GetComponent<PlayerController>();
            controller.enabled = false;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(PlayerController).GetMethod("Awake", flags).Invoke(controller, null);
            var update = typeof(PlayerController).GetMethod("Update", flags);
            var cc = sourcePlayer.GetComponent<CharacterController>();
            cc.enabled = false; sourcePlayer.transform.position = layout.World(Vector2.zero, 1.1f); cc.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 300; i++) update.Invoke(controller, null);
            float grounded = sourcePlayer.transform.position.y;
            if (grounded < .9f || grounded > 1.2f) throw new Exception("Player failed to stand on floor: " + grounded);
            cc.enabled = false; sourcePlayer.transform.position = layout.World(layout.pits[0].center, 1.1f); cc.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 600; i++) update.Invoke(controller, null);
            float fallen = sourcePlayer.transform.position.y;
            if (fallen > -layout.pits[0].depth) throw new Exception("Gravity failed to carry player through pit: " + fallen + ", dt=" + Time.deltaTime);
            return "Saved top and 3D previews. Actual PlayerController.Update verified in edit-mode harness: stood at y=" + grounded.ToString("0.###") + ", fell below shaft to y=" + fallen.ToString("0.###") + ". Current workspace preserved.";
        }
        finally
        {
            RenderTexture.active = previous;
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
            if (image != null) Object.DestroyImmediate(image);
            EditorSceneManager.CloseScene(demo, true);
            SceneManager.SetActiveScene(original);
        }
    }
}
