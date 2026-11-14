using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyDoorInteraction
{
    const string Path = "Assets/Scenes/__DoorInteractionTest.unity";
    static readonly List<string> checks = new();
    static void Check(bool pass, string label) { checks.Add((pass ? "PASS: " : "FAIL: ") + label); if (!pass) throw new Exception(label); }
    public static object Prepare()
    {
        if (Application.isPlaying || File.Exists(Path)) throw new Exception("Test requires edit mode and unused temporary scene path.");
        var original = SceneManager.GetActiveScene(); var previous = EditorSceneManager.playModeStartScene;
        SessionState.SetString("WoodenDoor.PreviousPlayScene", previous == null ? "" : AssetDatabase.GetAssetPath(previous));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Independent partition doorway", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.rooms.Add(new LevelRoom { id = "A", center = new Vector2(0, -3), size = Vector2.one * 6 });
            layout.rooms.Add(new LevelRoom { id = "B", center = new Vector2(0, 3), size = Vector2.one * 6 });
            layout.partitions.Add(new RoomPartition { start = new Vector2(-3, 0), end = new Vector2(3, 0), height = 3.2f });
            LevelBuilder.Rebuild(layout);
            LevelPropPlacement.Place(layout, AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WoodenDoor.prefab"), new Pose(Vector3.zero, Quaternion.identity));
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"), scene);
            player.transform.position = new Vector3(0, 1, 2);
            var serialized = new SerializedObject(player.GetComponent<PlayerController>());
            serialized.FindProperty("useGravity").boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
            var motion = player.GetComponentInChildren<PlayerCameraMotion>(); if (motion != null) motion.enabled = false;
            LevelBuilder.AddLighting(); LevelBuilder.Persist(layout, Path); EditorSceneManager.SaveScene(scene, Path);
        }
        finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Path);
        return new { testScene = Path, originalScene = original.path, originalStillDirty = original.isDirty };
    }

    static async Task Tick(int ms = 160)
    {
        int frame = Time.frameCount; await Task.Delay(ms);
        Check(Application.isPlaying && Time.frameCount > frame, "Player loop advances");
    }
    static void Aim(PlayerController controller, PlayerInteraction player, Vector3 target)
    {
        Vector3 delta = target - player.viewCamera.transform.position;
        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        controller.RotateTransport(Mathf.DeltaAngle(controller.transform.eulerAngles.y, yaw));
        float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
        typeof(PlayerController).GetField("pitch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, pitch);
    }
    static bool ThroughLeaf(DoorInteraction door) => door.leafCollider.Raycast(new Ray(new Vector3(0, 1, 1), Vector3.back), out _, 2);
    static async Task Click(Mouse mouse)
    {
        mouse.MakeCurrent(); InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left)); await Tick(100);
        InputSystem.QueueStateEvent(mouse, new MouseState()); await Tick(100);
    }
    public static async Task<object> Play()
    {
        checks.Clear(); Check(Application.isPlaying && SceneManager.GetActiveScene().path == Path, "Isolated saved test scene is playing");
        var door = Object.FindAnyObjectByType<DoorInteraction>(); var player = Object.FindAnyObjectByType<PlayerInteraction>();
        var controller = player.GetComponent<PlayerController>(); var capsule = player.GetComponent<CharacterController>();
        var previousBackground = InputSystem.settings.backgroundBehavior;
        var previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var previousMouse = Mouse.current; var mouse = InputSystem.AddDevice<Mouse>();
        bool mouseWasEnabled = previousMouse != null && previousMouse.enabled;
        if (mouseWasEnabled) InputSystem.DisableDevice(previousMouse);
        var inputTrace = new List<string>();
        Action trace = () => { if (mouse.leftButton.wasPressedThisFrame)
            inputTrace.Add($"frame={Time.frameCount} current={Mouse.current == mouse} focus={player.FocusedTarget?.name} ring={player.RingProgress} suspended={controller.InputSuspended} locked={controller.TransportLocked} angle={Vector3.Angle(player.viewCamera.transform.forward, door.InteractionPosition-player.viewCamera.transform.position)}"); };
        InputSystem.onAfterUpdate += trace;
        GameObject obstacle = null;
        try
        {
            await Tick();
            controller.Teleport(new Vector3(0, 1.05f, 1.6f)); controller.SetInputCaptured(true);
            Aim(controller, player, door.InteractionPosition); await Tick(450);
            Check(player.FocusedTarget == door && door.InteractionHint == "开门", "Player focus advertises opening from front");
            Check(ThroughLeaf(door), "Closed leaf blocks passage");
            await Click(mouse); await Tick(800);
            Check(door.IsOpen && door.CurrentAngle > 89 && door.InteractionHint == "关门", $"Real left click opens away from player to 90 degrees (angle={door.CurrentAngle}, target={door.TargetOpen}, blocked={door.Blocked})");
            Check(!ThroughLeaf(door), "Open leaf collider leaves corridor center clear");
            Check(Mathf.Abs(Mathf.DeltaAngle(door.hinge.localEulerAngles.y, door.CurrentAngle)) < .2f, "Physical hinge and model angle agree");
            // Traverse using the actual player capsule, not just a ray through the door.
            controller.Teleport(new Vector3(.05f, 1, 1.3f));
            for (int i = 0; i < 18; i++) capsule.Move(Vector3.back * .15f);
            Check(player.transform.position.z < -.8f, "Player capsule passes through open doorway");
            controller.Teleport(new Vector3(0, 1, -1.7f)); Aim(controller, player, door.InteractionPosition); await Tick(450);
            Check(player.FocusedTarget == door, "Open door can be focused from opposite side");
            await Click(mouse); await Tick(800);
            Check(Mathf.Abs(door.CurrentAngle) < .1f && ThroughLeaf(door), "Real left click closes and restores collision");
            controller.Teleport(new Vector3(0, 1, -1.3f));
            for (int i = 0; i < 15; i++) capsule.Move(Vector3.forward * .15f);
            Check(player.transform.position.z < -.2f, "Closed door stops actual player capsule");
            controller.Teleport(new Vector3(0, 1, -1.7f)); Aim(controller, player, door.InteractionPosition); await Tick(450);
            await Click(mouse); await Tick(800);
            Check(door.IsOpen && door.CurrentAngle < -89, "Opening from rear swings away in opposite direction");
            controller.Teleport(new Vector3(0, 1, -2.2f));
            obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube); obstacle.name = "Door sweep obstruction";
            obstacle.transform.position = new Vector3(.1f, 1, .55f); obstacle.transform.localScale = new Vector3(.3f, 1.4f, .3f);
            Physics.SyncTransforms(); Check(door.TryInteract(player), "Close request accepted while sweep is obstructed"); await Tick(900);
            Check(door.Blocked && Mathf.Abs(door.CurrentAngle) > 10 && door.InteractionHint.Contains("受阻"), "Closing pauses before hitting obstacle");
            Object.Destroy(obstacle); obstacle = null; await Tick(900);
            Check(Mathf.Abs(door.CurrentAngle) < .1f && !door.Blocked, "Cleared obstruction lets closing finish");
            controller.InputSuspended = true;
            Check(!door.TryInteract(player), "Suspended player cannot toggle door"); controller.InputSuspended = false;
            door.interactionEnabled = false; Check(!door.TryInteract(player), "Disabled interaction rejects toggle"); door.interactionEnabled = true;
            Check(door.AllowWhileHolding, "Door can be used while carrying an item");
            return new { passed = true, checks };
        }
        finally
        {
            if (obstacle != null) Object.Destroy(obstacle);
            InputSystem.onAfterUpdate -= trace;
            InputSystem.RemoveDevice(mouse); if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (mouseWasEnabled && previousMouse != null && previousMouse.added) InputSystem.EnableDevice(previousMouse);
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            Directory.CreateDirectory("Logs/WoodenDoor"); File.WriteAllLines("Logs/WoodenDoor/play.txt", checks);
            File.WriteAllLines("Logs/WoodenDoor/input-trace.txt", inputTrace);
        }
    }
    public static object Cleanup()
    {
        if (Application.isPlaying) throw new Exception("Stop Play Mode first.");
        string previous = SessionState.GetString("WoodenDoor.PreviousPlayScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        AssetDatabase.DeleteAsset(Path); AssetDatabase.DeleteAsset("Assets/Scenes/__DoorInteractionTest_Geometry");
        return "Temporary test assets removed; prior play scene restored.";
    }
    public static async Task<object> Visual(bool open)
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().path != Path) throw new Exception("Run only in the isolated play scene.");
        var door = Object.FindAnyObjectByType<DoorInteraction>(); var player = Object.FindAnyObjectByType<PlayerInteraction>();
        var controller = player.GetComponent<PlayerController>();
        controller.Teleport(new Vector3(0, 1.05f, 1.8f)); controller.SetInputCaptured(true);
        Aim(controller, player, new Vector3(0, 1.2f, 0));
        if (door.TargetOpen != open) door.TryInteract(player);
        await Task.Delay(1000);
        player.GetComponentInChildren<SphericalSonar>().EmitFrom(player.viewCamera.transform.position, 10, 30, 30);
        await Task.Delay(400);
        return new { open = door.IsOpen, angle = door.CurrentAngle, blocked = door.Blocked };
    }
}
