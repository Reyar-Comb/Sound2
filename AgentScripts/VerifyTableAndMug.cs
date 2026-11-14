using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyTableAndMug
{
    static readonly List<string> checks = new();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
    public static object Edit()
    {
        checks.Clear();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("ResizableTable"), scene);
            var table = instance.GetComponent<ResizableTable>();
            Check(instance.GetComponentsInChildren<BoxCollider>().Length == 5 && instance.GetComponent<PickupItem>() == null, "Table has five solid parts and cannot be picked up");
            Check(table.GetComponent<LevelProp>().gameplayMaterial != null, "Table uses existing runtime black presentation");
            Undo.RecordObject(table, "Verify table size");
            table.width = 4; table.depth = 1.5f; table.height = 1.2f; table.Rebuild();
            Undo.FlushUndoRecordObjects();
            var top = instance.transform.Find("Tabletop");
            Check(top.localScale == new Vector3(4, .08f, 1.5f), "Resizing preserves tabletop thickness");
            foreach (Transform leg in instance.transform)
                if (leg != top)
                {
                    Check(Mathf.Abs(leg.localScale.x - .08f) < .0001f && Mathf.Abs(leg.localPosition.y - leg.localScale.y * .5f) < .0001f, "Leg thickness and floor contact preserved");
                }
            Physics.SyncTransforms();
            Check(Mathf.Abs(top.GetComponent<Collider>().bounds.size.x - 4) < .001f, "Collision shape follows table dimensions");
            Undo.PerformUndo(); table.Rebuild();
            Check(table.width == 2 && top.localScale.x == 2, "Undo restores dimensions and geometry");
            Undo.PerformRedo(); table.Rebuild();
            Check(table.width == 4 && top.localScale.x == 4, "Redo restores resized geometry");
            table.width = .001f; table.height = -.1f; table.Rebuild();
            Check(top.localScale.x > 0 && instance.transform.Find("Leg Front Left").localScale.y > 0, "Tiny or negative input cannot invert the geometry");
            var mug = Prefab("PickupGlassMug").GetComponent<PickupItem>();
            Check(mug.interactionAnchor != null && Mathf.Abs(mug.interactionAnchor.localPosition.y - .33f) < .001f, "Glass focus anchor is centred in its bowl above the floor");
        }
        finally { if (instance != null) Object.DestroyImmediate(instance); EditorSceneManager.ClosePreviewScene(scene); }
        Capture();
        return new { passed = true, checks };
    }

    static void Capture()
    {
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var rt = new RenderTexture(1100, 700, 24);
        var image = new Texture2D(1100, 700, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            SceneManager.SetActiveScene(scene);
            Vector3 origin = new Vector3(6000, 0, 6000);
            var table = Object.Instantiate(Prefab("ResizableTable"), origin, Quaternion.identity);
            var mug = Object.Instantiate(Prefab("PickupGlassMug"), origin + new Vector3(.5f, .8f, 0), Quaternion.identity);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = origin + Vector3.down * .05f; floor.transform.localScale = new Vector3(10, .1f, 10);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Floor.mat");
            var light = new GameObject("Preview light", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Quaternion.Euler(45, -30, 0);
            var camera = new GameObject("Preview camera", typeof(Camera)).GetComponent<Camera>(); camera.enabled = false;
            camera.transform.position = origin + new Vector3(3, 2.4f, -3.5f); camera.transform.LookAt(origin + Vector3.up * .4f);
            camera.fieldOfView = 38; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .075f, .09f);
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1100, 700), 0, 0); image.Apply();
            Directory.CreateDirectory("Logs/Table"); File.WriteAllBytes("Logs/Table/preview.png", image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(image); SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
    }

    public static async Task<object> Play()
    {
        checks.Clear(); Check(Application.isPlaying, "Play mode active");
        var fixture = new GameObject("Table and mug verification");
        try
        {
            Vector3 origin = new Vector3(4000, 0, 4000);
            GameObject Cube(string name, Vector3 position, Vector3 scale)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
                go.transform.SetParent(fixture.transform); go.transform.position = origin + position; go.transform.localScale = scale; return go;
            }
            Cube("Verification floor", new Vector3(0, -.5f, 0), new Vector3(20, 1, 20));
            var player = Object.Instantiate(Prefab("Player"), origin + Vector3.up * 1.1f, Quaternion.identity, fixture.transform);
            var controller = player.GetComponent<PlayerController>(); controller.InputSuspended = true;
            var interaction = player.GetComponent<PlayerInteraction>();
            var mug = Object.Instantiate(Prefab("PickupGlassMug"), origin + new Vector3(0, 0, 1.4f), Quaternion.identity, fixture.transform).GetComponent<PickupItem>();
            mug.GetComponent<Rigidbody>().isKinematic = true;
            int start = Time.frameCount; await Task.Delay(150);
            Check(Time.frameCount > start, "Player loop advances during verification");
            controller.InputSuspended = false;
            var camera = interaction.viewCamera.transform;
            camera.position = origin + new Vector3(0, 1.2f, 0);
            var anchor = mug.interactionAnchor;
            var los = typeof(PlayerInteraction).GetMethod("HasLineOfSight", BindingFlags.Instance | BindingFlags.NonPublic);
            int oldBlocked = 0;
            for (int i = 0; i < 60; i++)
            {
                mug.transform.position = origin + new Vector3(0, Mathf.Sin(i) * .002f, 1.4f);
                Physics.SyncTransforms(); camera.LookAt(anchor);
                mug.interactionAnchor = null;
                if (!(bool)los.Invoke(interaction, new object[] { mug })) oldBlocked++;
                mug.interactionAnchor = anchor;
                interaction.RefreshFocus();
                Check(interaction.FocusedTarget == mug, "Stable upright focus sample " + i);
            }
            Check(oldBlocked > 0, "Reproduced old floor-anchor occlusion under small physics offsets");
            mug.transform.rotation = Quaternion.Euler(0, 0, 90); mug.transform.position = origin + new Vector3(.2f, .17f, 1.4f);
            Physics.SyncTransforms(); camera.LookAt(anchor); interaction.RefreshFocus();
            Check(interaction.FocusedTarget == mug, "Sideways mug stays focusable");
            var wall = Cube("Verification occluder", new Vector3(0, 1, .7f), new Vector3(2, 2, .1f));
            Physics.SyncTransforms(); interaction.RefreshFocus();
            Check(interaction.FocusedTarget == null && !interaction.Pickup.TryPickUp(mug), "Wall hides the hint and blocks pickup");
            wall.SetActive(false); Physics.SyncTransforms(); interaction.RefreshFocus();
            Check(interaction.FocusedTarget == mug && interaction.Pickup.TryPickUp(mug), "Visible mug can still be picked up");
            controller.InputSuspended = true;
            var table = Object.Instantiate(Prefab("ResizableTable"), origin + Vector3.right * 4, Quaternion.identity, fixture.transform).GetComponent<ResizableTable>();
            table.width = 3; table.Rebuild(); Physics.SyncTransforms();
            Check(table.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterial == table.GetComponent<LevelProp>().gameplayMaterial), "All table parts switch to runtime black");
            Check(Physics.Raycast(table.transform.position + new Vector3(1.2f, 2, 0), Vector3.down, out var hit, 2) && hit.collider.transform.IsChildOf(table.transform), "Resized tabletop supports physical objects");
            return new { passed = true, oldBlocked, checks };
        }
        finally { Object.Destroy(fixture); }
    }
}
