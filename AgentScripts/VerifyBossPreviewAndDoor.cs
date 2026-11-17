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

public static class VerifyBossPreviewAndDoor
{
    const string ScenePath = "Assets/Scenes/__BossPreviewDoorTest.unity";
    static readonly List<string> checks = new();
    static void Check(bool pass, string label)
    {
        checks.Add((pass ? "PASS: " : "FAIL: ") + label);
        if (!pass) throw new Exception(label);
    }

    public static object Prepare()
    {
        if (Application.isPlaying || File.Exists(ScenePath)) throw new Exception("Requires edit mode and an unused test scene path");
        var original = SceneManager.GetActiveScene();
        var previous = EditorSceneManager.playModeStartScene;
        SessionState.SetString("BossPreviewDoor.PreviousPlayScene", previous == null ? "" : AssetDatabase.GetAssetPath(previous));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var table = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BossMemoryTable.prefab"), scene);
            table.transform.position = new Vector3(10, 0, 0);
            var door = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WoodenDoor.prefab"), scene);
            door.transform.position = Vector3.zero;
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"), scene);
            player.transform.position = new Vector3(10, 1, 2);
            var serialized = new SerializedObject(player.GetComponent<PlayerController>());
            serialized.FindProperty("useGravity").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var motion = player.GetComponentInChildren<PlayerCameraMotion>();
            if (motion != null) motion.enabled = false;
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        return new { ScenePath, original = original.path, original.isDirty };
    }

    static async Task Tick(int milliseconds = 160)
    {
        int before = Time.frameCount;
        await Task.Delay(milliseconds);
        Check(Application.isPlaying && Time.frameCount > before, "Player loop advances");
    }

    static void Aim(PlayerInteraction actor, Vector3 point)
    {
        var controller = actor.GetComponent<PlayerController>();
        Vector3 delta = point - actor.viewCamera.transform.position;
        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        controller.RotateTransport(Mathf.DeltaAngle(controller.transform.eulerAngles.y, yaw));
        float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
        typeof(PlayerController).GetField("pitch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, pitch);
    }

    static async Task Focus(PlayerInteraction actor, InteractableObject target, Vector3 point)
    {
        Aim(actor, point);
        await Tick(450);
        actor.RefreshFocus();
        Check(actor.FocusedTarget == target, "Focus " + target.name);
    }

    public static async Task<object> Run()
    {
        checks.Clear();
        Check(Application.isPlaying && SceneManager.GetActiveScene().path == ScenePath, "Isolated scene is playing");
        try
        {
            await Tick();
            var actor = Object.FindAnyObjectByType<PlayerInteraction>();
            var controller = actor.GetComponent<PlayerController>();
            controller.SetInputCaptured(true);
            var gate = Object.FindAnyObjectByType<BossMemoryGate>();
            var slots = gate.displayRoot.GetComponentsInChildren<BossMemorySlot>();
            Check(slots.Length == 6 && gate.PlacedCount == 0 && !gate.Ready, "Six empty previews, Boss gated");
            Check(slots.All(s => s.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterial.shader.name == "Sound2/Boss Memory Preview")), "All six previews use red wireframe material");
            Check(!gate.TryInteract(actor), "Table body cannot place memories");
            var progressType = typeof(StoryCollectible).Assembly.GetType("StoryRunProgress");
            var progress = (ICollectibleProgress)progressType.GetField("Current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            progress.TryCollect(gate.memories[1].id);
            controller.Teleport(slots[0].InteractionPosition + new Vector3(0, .2f, 1.4f));
            await Focus(actor, slots[0], slots[0].InteractionPosition);
            Check(actor.TryInteractFocused(), "Click missing memory preview");
            Check(gate.PlacedCount == 0 && actor.GetComponent<PlayerSubtitles>().CurrentText == "没有可摆放的信物。请先在关卡中收集，再回到长桌。", "Missing clicked item retains old message and does not place another owned item");
            Check(slots[0].GetComponentInChildren<Renderer>().sharedMaterial.shader.name == "Sound2/Boss Memory Preview", "Missing item stays red");
            controller.Teleport(slots[1].InteractionPosition + new Vector3(0, .2f, 1.4f));
            await Focus(actor, slots[1], slots[1].InteractionPosition);
            Check(actor.TryInteractFocused() && gate.PlacedCount == 1, "Click owned memory places exactly that slot");
            var expected = (Material)typeof(BossMemoryGate).GetField("displayGameplayMaterial", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gate);
            Check(slots[1].GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m == expected)), "Placed slot uses original white gameplay material");
            Check(actor.TryInteractFocused() && gate.PlacedCount == 1 && slots[1].InteractionHint.StartsWith("调查："), "Repeat click investigates without duplicating placement");
            Check(!gate.startButton.CanInteract(actor), "Partial placement cannot start Boss");
            foreach (int index in new[] { 5, 3, 0, 4, 2 })
            {
                progress.TryCollect(gate.memories[index].id);
                controller.Teleport(slots[index].InteractionPosition + new Vector3(0, .2f, 1.4f));
                await Focus(actor, slots[index], slots[index].InteractionPosition);
                Check(actor.TryInteractFocused(), "Place selected slot " + index);
            }
            Check(gate.Ready && gate.PlacedCount == 6 && gate.displayRoot.childCount == 6 && gate.startButton.CanInteract(actor), "Six placements unlock start without duplicate displays");
            gate.gameObject.SetActive(false); gate.gameObject.SetActive(true);
            await Tick();
            Check(gate.displayRoot.childCount == 6 && slots.All(s => s.GetComponentInChildren<Renderer>().sharedMaterial == expected), "Reactivation preserves placed appearance");
            var clone = Object.Instantiate(gate.gameObject, new Vector3(30, 0, 0), Quaternion.identity);
            await Tick();
            var cloneGate = clone.GetComponent<BossMemoryGate>();
            Check(cloneGate.Ready && cloneGate.displayRoot.childCount == 6, "Runtime clone rebuilds six placed displays");
            Object.Destroy(clone);

            var door = Object.FindAnyObjectByType<DoorInteraction>();
            var leaf = door.leafCollider;
            foreach (float side in new[] { 1f, -1f })
            {
                foreach (Vector2 sample in new[] { new Vector2(-.4f, -.4f), new Vector2(.4f, .4f), Vector2.zero })
                {
                    var point = leaf.transform.TransformPoint(leaf.center + new Vector3(sample.x * leaf.size.x, sample.y * leaf.size.y, side * leaf.size.z * .5f));
                    controller.Teleport(leaf.bounds.center + new Vector3(0, 0, side * 1.5f) - (actor.viewCamera.transform.position - controller.transform.position));
                    await Focus(actor, door, point);
                    Check(Vector3.Distance(door.GetInteractionPosition(actor), point) < .03f, "Door focus follows aimed surface on side " + side);
                }
            }
            // Open using a corner far from the handle, then focus and close the rotated leaf.
            var corner = leaf.transform.TransformPoint(leaf.center + new Vector3(-.4f * leaf.size.x, .4f * leaf.size.y, -.5f * leaf.size.z));
            await Focus(actor, door, corner);
            Check(actor.TryInteractFocused() && door.TargetOpen, "Door corner click opens");
            await Tick(1000);
            Check(door.IsOpen, "Door reaches open pose");
            var openPoint = leaf.transform.TransformPoint(leaf.center + new Vector3(.35f * leaf.size.x, .35f * leaf.size.y, .5f * leaf.size.z));
            controller.Teleport(openPoint + leaf.transform.forward * 1.5f - (actor.viewCamera.transform.position - controller.transform.position));
            await Focus(actor, door, openPoint);
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.position = Vector3.Lerp(actor.viewCamera.transform.position, openPoint, .5f);
            obstacle.transform.localScale = Vector3.one * .4f;
            Physics.SyncTransforms(); actor.RefreshFocus();
            Check(actor.FocusedTarget != door, "Occluder blocks door surface interaction");
            Object.Destroy(obstacle); await Tick();
            await Focus(actor, door, openPoint);
            Check(actor.TryInteractFocused() && !door.TargetOpen, "Open door corner click closes");
            controller.Teleport(new Vector3(0, 1, 10)); Aim(actor, leaf.bounds.center); await Tick(); actor.RefreshFocus();
            Check(actor.FocusedTarget != door, "Door still obeys interaction distance");
            return new { passed = true, checks };
        }
        finally
        {
            Directory.CreateDirectory("Logs/BossPreviewDoor");
            File.WriteAllLines("Logs/BossPreviewDoor/runtime.txt", checks);
        }
    }

    public static object Cleanup()
    {
        if (Application.isPlaying) throw new Exception("Stop Play Mode first");
        string previous = SessionState.GetString("BossPreviewDoor.PreviousPlayScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        AssetDatabase.DeleteAsset(ScenePath);
        return "Removed isolated test scene and restored play scene";
    }
}
