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
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Run Edit, then VerifyDoorInteraction.Prepare / Play, then Play, and finally the existing Cleanup.
public static class VerifyDoorFeatures
{
    static readonly List<string> checks = new();
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string label)
    {
        checks.Add((value ? "PASS: " : "FAIL: ") + label);
        if (!value) throw new Exception(label);
    }
    static object Get(object owner, string name) => owner.GetType().GetField(name, Flags).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
    static void Report(string name)
    {
        Directory.CreateDirectory("Logs/DoorFeatures");
        File.WriteAllLines("Logs/DoorFeatures/" + name + ".txt", checks);
    }
    static async Task Tick(int ms)
    {
        int frame = Time.frameCount;
        await Task.Delay(ms);
        Check(Application.isPlaying && Time.frameCount > frame, "Player loop advances");
    }

    public static object Edit()
    {
        checks.Clear();
        Check(!Application.isPlaying, "Edit mode");
        var original = SceneManager.GetActiveScene();
        var selection = Selection.objects;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        string path = "Assets/Scenes/__DoorFeatures_" + Guid.NewGuid().ToString("N") + ".prefab";
        string scenePath = path.Replace(".prefab", ".unity");
        string sourcePath = path.Replace(".prefab", "_Source.unity");
        GameObject reopened = null;
        LevelDesignerWindow window = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Door feature verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.rooms.Add(new LevelRoom { size = new Vector2(40, 10) });
            layout.partitions.Add(new RoomPartition { start = new Vector2(-2, 0), end = new Vector2(2, 0), height = 3 });
            layout.partitions.Add(new RoomPartition { start = new Vector2(8, 0), end = new Vector2(12, 0), height = 3 });
            LevelBuilder.Rebuild(layout);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WoodenDoor.prefab");
            var a = LevelPropPlacement.Place(layout, prefab, new Pose(Vector3.zero, Quaternion.identity)).GetComponent<DoorInteraction>();
            var b = LevelPropPlacement.Place(layout, prefab, new Pose(Vector3.right * 10, Quaternion.identity)).GetComponent<DoorInteraction>();
            Check(a.openingSide == DoorOpeningSide.Both && a.teleportDestination == null && a.autoCloseDelay == 0, "Existing prefab defaults retain ordinary two-way manual door");
            var serialized = new SerializedObject(a);
            Undo.IncrementCurrentGroup();
            serialized.FindProperty("teleportDestination").objectReferenceValue = b;
            serialized.FindProperty("openingSide").enumValueIndex = (int)DoorOpeningSide.FrontOnly;
            serialized.FindProperty("autoCloseDelay").floatValue = 2.25f;
            serialized.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Check(a.teleportDestination == null && a.autoCloseDelay == 0, "Door settings support Undo");
            Undo.PerformRedo();
            Check(a.teleportDestination == b && a.openingSide == DoorOpeningSide.FrontOnly && a.autoCloseDelay == 2.25f, "Door settings support Redo");
            b.teleportDestination = a;
            window = ScriptableObject.CreateInstance<LevelDesignerWindow>();
            window.ShowUtility(); window.CreateGUI();
            Set(window, "layout", layout); Set(window, "selectedProp", a.gameObject);
            typeof(LevelDesignerWindow).GetMethod("Refresh", Flags).Invoke(window, null);
            var labels = window.rootVisualElement.Query<UnityEditor.UIElements.PropertyField>().ToList().Select(x => x.label).ToList();
            Check(labels.Contains("开门传送到") && labels.Contains("允许开门的方向") && labels.Contains("自动关门等待（秒，0 关闭）"), "Level editor exposes all three behaviour controls");
            LevelBuilder.ExportPrefab(layout, path);
            reopened = PrefabUtility.LoadPrefabContents(path);
            var doors = reopened.GetComponentsInChildren<DoorInteraction>();
            var saved = doors.Single(x => x.openingSide == DoorOpeningSide.FrontOnly);
            Check(saved.autoCloseDelay == 2.25f && doors.Contains(saved.teleportDestination) && saved.teleportDestination.teleportDestination == saved,
                "Prefab export remaps reciprocal door references and preserves settings");
            LevelBuilder.Persist(layout, sourcePath);
            EditorSceneManager.SaveScene(scene, sourcePath);
            LevelBuilder.Export(layout, scenePath, false);
            var exported = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var exportedDoors = exported.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<DoorInteraction>()).ToArray();
                var source = exportedDoors.Single(x => x.openingSide == DoorOpeningSide.FrontOnly);
                Check(exportedDoors.Contains(source.teleportDestination) && source.autoCloseDelay == 2.25f, "Scene export and reopen preserve internal target and delay");
            }
            finally { EditorSceneManager.CloseScene(exported, true); }
            return new { passed = true, checks };
        }
        finally
        {
            if (window != null) window.Close();
            if (reopened != null) PrefabUtility.UnloadPrefabContents(reopened);
            SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.DeleteAsset(path); AssetDatabase.DeleteAsset(path.Replace(".prefab", "_Geometry"));
            AssetDatabase.DeleteAsset(scenePath);
            AssetDatabase.DeleteAsset(sourcePath); AssetDatabase.DeleteAsset(sourcePath.Replace(".unity", "_Geometry"));
            Selection.objects = selection; Report("edit");
        }
    }

    public static async Task<object> Play()
    {
        checks.Clear();
        Check(Application.isPlaying && SceneManager.GetActiveScene().path == "Assets/Scenes/__DoorInteractionTest.unity", "Isolated play scene");
        var source = Object.FindAnyObjectByType<DoorInteraction>();
        var player = Object.FindAnyObjectByType<PlayerInteraction>();
        var controller = player.GetComponent<PlayerController>();
        var capsule = player.GetComponent<CharacterController>();
        var pickup = player.GetComponent<PlayerPickup>();
        var footsteps = player.GetComponent<FootstepEmitter>();
        var motion = player.GetComponent<PlayerCameraMotion>();
        var destination = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WoodenDoor.prefab"), new Vector3(20, 0, 0), Quaternion.Euler(0, 90, 0)).GetComponent<DoorInteraction>();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(20, -.25f, 0); floor.transform.localScale = new Vector3(12, .5f, 12);
        var oldBackground = InputSystem.settings.backgroundBehavior;
        var oldInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        GameObject obstacle = null, itemObject = null;
        try
        {
            controller.SetInputCaptured(true);
            controller.Teleport(new Vector3(0, 1.05f, 1.6f)); controller.FaceDirection(Vector3.back);
            if (source.TargetOpen) { source.TryInteract(player); await Tick(900); }
            source.teleportDestination = destination;
            destination.teleportDestination = source; // Must not recursively teleport.
            destination.degreesPerSecond = 30; destination.openAngle = 60;
            motion.enabled = true;
            await Tick(120);
            Set(controller, "pitch", 13f); Set(controller, "fallSpeed", -7f); Set(controller, "airborneTime", .4f);
            Set(motion, "stridePhase", 1.7f); Set(footsteps, "distanceSinceStep", .7f);
            itemObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));
            var item = itemObject.GetComponent<PickupItem>();
            itemObject.transform.position = pickup.holdPoint.position;
            Physics.SyncTransforms();
            Check(pickup.TryPickUp(item), "Hold an actual physical prop before transport");
            var cameraPosition = controller.ViewTransform.position;
            var cameraRotation = controller.ViewTransform.rotation;
            var itemPosition = item.transform.position;
            var field = controller.GetComponentInChildren<SonarPulseField>();
            field.ClearPulses();
            field.EmitFrom(cameraPosition, 12, 30, 8);
            field.EmitFootstepFrom(controller.transform.position, 3, 8, 2, null);
            field.EmitDangerFrom(cameraPosition + Vector3.forward, 8, 8, 2, null);
            var pulseOrigins = (Vector4[])Get(field, "origins");
            var pulseBefore = pulseOrigins[0];
            var dangerBefore = pulseOrigins[SonarPulseField.DangerPulseOffset];
            int emissions = field.Emissions;
            var from = controller.transform.position;
            var q = Quaternion.Euler(0, 90, 0);
            var expected = destination.transform.position + q * from;
            Check(source.TryInteract(player) && !source.TeleportBlocked, "Opening accepts configured target");
            Check(Vector3.Distance(controller.transform.position, expected) < .001f, "Teleport occurs synchronously with exact relative position");
            Check(Vector3.Distance(controller.ViewTransform.position, destination.transform.position + q * cameraPosition) < .001f &&
                Quaternion.Angle(controller.ViewTransform.rotation, q * cameraRotation) < .01f, "Camera pose and pitch preserved under rotated doorway mapping");
            Check((float)Get(controller, "fallSpeed") == -7 && (float)Get(controller, "airborneTime") == .4f &&
                !controller.InputSuspended && !controller.TransportLocked && capsule.enabled, "Velocity and airborne state preserved without input lock");
            Check((float)Get(motion, "stridePhase") == 1.7f && (float)Get(footsteps, "distanceSinceStep") == .7f, "Camera stride and footstep accumulation preserved");
            Check(pickup.HeldItem == item && Vector3.Distance(item.transform.position, destination.transform.position + q * itemPosition) < .001f, "Held prop follows in the same frame");
            Check(destination.TargetOpen && destination.CurrentAngle == source.CurrentAngle, "Destination receives exact door animation phase");
            Check(Vector3.Distance((Vector3)pulseOrigins[0], destination.transform.position + q * (Vector3)pulseBefore) < .001f &&
                pulseOrigins[0].w == pulseBefore.w && field.Emissions == emissions, "White reveal maps without restarting age or emitting another sound pulse");
            var basis = ((Vector4[])Get(field, "blockerRotations"))[0];
            Check(Mathf.Abs(basis.x) < .001f && Mathf.Abs(basis.y - 1) < .001f && pulseOrigins[SonarPulseField.DangerPulseOffset] == dangerBefore,
                "Occlusion basis rotates while danger cue remains in its real world position");
            Set(controller, "pitch", 0f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            await Tick(350);
            Check(Vector3.Distance(controller.transform.position, expected) > .1f, "Held forward input continues movement after transport");
            Check(Mathf.Abs(destination.CurrentAngle - source.CurrentAngle) < 3.1f && destination.CurrentAngle > 5,
                "Both doors continue at source animation speed despite different destination defaults");
            await Tick(550);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Tick(100);
            Check(destination.transform.InverseTransformPoint(controller.transform.position).z < -.1f, "Actual CharacterController traverses arriving doorway without bounce-back");
            destination.teleportDestination = null;
            // Separate the player from the door sweep; a held prop remains in the player's hierarchy.
            controller.Teleport(destination.transform.TransformPoint(new Vector3(0, 1.05f, 2)));
            if (destination.TargetOpen) destination.TryInteract(player);
            await Tick(3300); // This manual close uses the destination's own 30 degrees/sec setting.
            Check(Mathf.Abs(destination.CurrentAngle) < .1f, "Manual close remains available after arrival");
            destination.degreesPerSecond = 150; destination.openAngle = 90;
            destination.openingSide = DoorOpeningSide.FrontOnly;
            controller.Teleport(destination.transform.TransformPoint(new Vector3(0, 1.05f, -2)));
            destination.TryInteract(player);
            Check(!destination.TargetOpen && destination.LockedFeedback && destination.InteractionHint == "已上锁", "Wrong side triggers locked feedback without opening");
            bool moved = false;
            for (int i = 0; i < 5; i++)
            {
                await Tick(35);
                float angle = Mathf.Abs(Mathf.DeltaAngle(0, destination.hinge.localEulerAngles.y));
                moved |= angle > .05f;
                Check(angle <= destination.lockedShakeAngle + .1f && !destination.TargetOpen, "Lock oscillation stays small and door stays closed");
            }
            Check(moved, "Locked door visibly shakes");
            await Tick(350);
            Check(!destination.LockedFeedback && Quaternion.Angle(destination.hinge.localRotation, Quaternion.identity) < .1f, "Shake returns exactly to closed pose");
            destination.openingSide = DoorOpeningSide.BackOnly;
            destination.TryInteract(player); await Tick(800);
            Check(destination.IsOpen && destination.CurrentAngle < -89, "Back-only door opens away from allowed back side");
            destination.TryInteract(player); await Tick(800);
            destination.openingSide = DoorOpeningSide.FrontOnly;
            controller.Teleport(destination.transform.TransformPoint(new Vector3(0, 1.05f, 2)));
            destination.autoCloseDelay = .3f; destination.TryInteract(player);
            await Tick(350);
            Check(destination.TargetOpen && !destination.IsOpen, "Auto-close does not count opening animation time");
            obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.SetPositionAndRotation(destination.transform.TransformPoint(new Vector3(.1f, 1, -.55f)), destination.transform.rotation);
            obstacle.transform.localScale = new Vector3(.3f, 1.4f, .3f);
            // Let the opening finish before inserting an obstruction into the return sweep.
            obstacle.SetActive(false); await Tick(350); obstacle.SetActive(true); Physics.SyncTransforms();
            await Tick(1000);
            Check(!destination.TargetOpen && destination.Blocked && destination.CurrentAngle > 10, "Delayed automatic close stops against real obstacle");
            Object.Destroy(obstacle); obstacle = null; await Tick(800);
            Check(Mathf.Abs(destination.CurrentAngle) < .1f && !destination.Blocked, "Automatic close finishes after obstacle removal");
            destination.autoCloseDelay = 0; destination.TryInteract(player); await Tick(1500);
            Check(destination.IsOpen && destination.TargetOpen, "Zero delay disables automatic closing");
            destination.TryInteract(player); await Tick(800);

            // Invalid links and occupied arrivals never move the player or mutate the destination.
            source.teleportDestination = null;
            controller.Teleport(new Vector3(0, 1.05f, 2));
            if (source.TargetOpen) { source.TryInteract(player); await Tick(800); }
            source.teleportDestination = source;
            from = controller.transform.position; source.TryInteract(player);
            Check(source.TeleportBlocked && controller.transform.position == from, "Self link fails safely");
            source.TryInteract(player); await Tick(800);
            source.teleportDestination = destination;
            destination.gameObject.SetActive(false); source.TryInteract(player);
            Check(source.TeleportBlocked && controller.transform.position == from, "Inactive destination fails safely");
            source.TryInteract(player); await Tick(800); destination.gameObject.SetActive(true);
            obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.position = destination.transform.TransformPoint(new Vector3(0, 1.05f, 2));
            Physics.SyncTransforms(); source.TryInteract(player);
            Check(source.TeleportBlocked && Vector3.Distance(controller.transform.position, from) < .001f && !destination.TargetOpen && Mathf.Abs(destination.CurrentAngle) < .1f,
                $"Occupied arrival rejects teleport and rolls destination pose back (blocked={source.TeleportBlocked}, moved={Vector3.Distance(controller.transform.position, from)}, target={destination.TargetOpen}, angle={destination.CurrentAngle})");
            source.TryInteract(player); await Tick(800);
            Object.Destroy(obstacle); obstacle = null;
            source.openingSide = DoorOpeningSide.BackOnly;
            source.TryInteract(player);
            Check(source.LockedFeedback && controller.transform.position == from, "Wrong-side source never teleports");
            return new { passed = true, checks };
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldInput;
            source.teleportDestination = null;
            if (itemObject != null) Object.Destroy(itemObject);
            if (obstacle != null) Object.Destroy(obstacle);
            Object.Destroy(destination.gameObject); Object.Destroy(floor);
            Report("play");
        }
    }

    public static async Task<object> VisualContinuity()
    {
        checks.Clear();
        Check(Application.isPlaying && SceneManager.GetActiveScene().path == "Assets/Scenes/__DoorInteractionTest.unity", "Isolated visual test scene");
        var source = Object.FindAnyObjectByType<DoorInteraction>();
        var layout = source.GetComponentInParent<LevelLayout>();
        var player = Object.FindAnyObjectByType<PlayerController>();
        var interaction = player.GetComponent<PlayerInteraction>();
        var camera = interaction.viewCamera;
        var sonar = camera.GetComponent<SphericalSonar>();
        player.Teleport(new Vector3(0, 1.05f, 1.8f)); player.FaceDirection(Vector3.back); player.SetInputCaptured(true);
        source.openingSide = DoorOpeningSide.Both; source.teleportDestination = null;
        if (source.TargetOpen) { source.TryInteract(interaction); await Tick(800); }
        var copy = Object.Instantiate(layout.gameObject, new Vector3(30, 0, 0), Quaternion.Euler(0, 73, 0));
        var destination = copy.GetComponentInChildren<DoorInteraction>();
        source.teleportDestination = destination;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = new RenderTexture(640, 360, 24); target.Create();
        var readback = new Texture2D(640, 360, TextureFormat.RGB24, false);
        Color32[] Capture(string name)
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); readback.Apply();
            File.WriteAllBytes("Logs/DoorFeatures/" + name + ".png", readback.EncodeToPNG());
            return readback.GetPixels32();
        }
        try
        {
            Physics.SyncTransforms(); sonar.PulseField.ClearPulses();
            sonar.EmitFrom(camera.transform.position, 12, 30, 8);
            await Tick(500);
            Directory.CreateDirectory("Logs/DoorFeatures");
            // Same frame, identical geometry and no new wave at the destination.
            var before = Capture("before-transfer");
            source.TryInteract(interaction);
            Check(!source.TeleportBlocked, "Visual test transfers through arbitrary 73-degree rotation");
            var after = Capture("after-transfer");
            int a = before.Count(x => x.r > 30), b = after.Count(x => x.r > 30);
            Check(a > 100 && b > a * .6f && b < a * 1.5f, $"Point reveal stays visible in the transfer frame (before={a}, after={b})");
            Check(!ShaderUtil.ShaderHasError(sonar.renderingSettings.shader), "Point shader compiles without errors");
            return new { passed = true, checks };
        }
        finally
        {
            source.teleportDestination = null;
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            Object.Destroy(readback); target.Release(); Object.Destroy(target); Object.Destroy(copy);
            Report("visual");
        }
    }
}
