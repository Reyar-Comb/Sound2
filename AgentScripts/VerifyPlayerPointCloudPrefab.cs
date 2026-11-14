using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Exercise a direct Instantiate(Player.prefab), without LevelBuilder or scene repair helpers.
// Run in agent-owned Play mode; entering/exiting Play preserves the user's unsaved edit scene.
public static class VerifyPlayerPointCloudPrefab
{
    static async Task Tick(int ms)
    {
        int frame = Time.frameCount; await Task.Delay(ms);
        if (!Application.isPlaying || Time.frameCount <= frame) throw new Exception("Player loop stopped.");
    }
    public static async Task<object> Main()
    {
        if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
        var checks = new List<string>();
        Action<bool, string> check = (ok, label) => { checks.Add((ok ? "PASS: " : "FAIL: ") + label); if (!ok) throw new Exception(label); };
        var originalRoots = SceneManager.GetActiveScene().GetRootGameObjects();
        var active = originalRoots.Select(r => r.activeSelf).ToArray();
        var oldKeyboard = Keyboard.current;
        var bg = InputSystem.settings.backgroundBehavior;
        var routing = InputSystem.settings.editorInputBehaviorInPlayMode;
        GameObject root = null, wall = null, floor = null;
        Keyboard keyboard = null; InputActionAsset actions = null;
        UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? devices = null;
        RenderTexture target = null; Texture2D image = null;
        try
        {
            var currentPlayer = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            check(currentPlayer != null && currentPlayer.GetComponentInChildren<SphericalSonar>() != null
                && currentPlayer.GetComponentInChildren<SonarPulseField>() != null, "Current manually placed player inherits the configured prefab");
            foreach (var r in originalRoots) r.SetActive(false);
            root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"),
                new Vector3(3000, 1.1f, 3000), Quaternion.identity);
            var player = root.GetComponent<PlayerController>();
            var camera = root.GetComponentInChildren<Camera>();
            var sonar = camera.GetComponent<SphericalSonar>();
            var source = camera.GetComponent<BlackBoxProximitySource>();
            var field = camera.GetComponent<SonarPulseField>();
            var foot = root.GetComponent<FootstepEmitter>();
            check(sonar != null && source != null && field != null && foot.pulseField == field,
                "Direct prefab instance has explicit sonar, style, field and footstep field binding");
            check(root.GetComponentsInChildren<SphericalSonar>().Length == 1 && root.GetComponentsInChildren<BlackBoxProximitySource>().Length == 1
                && root.GetComponentsInChildren<SonarPulseField>().Length == 1, "Direct prefab instance has exactly one point-cloud system");
            check(sonar.renderingSettings != null && !sonar.proximityRendering && sonar.sonarRendering, "Rendering material is assigned; proximity stays off");
            var black = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(3000, -.5f, 3000);
            floor.transform.localScale = new Vector3(20, 1, 20); floor.GetComponent<Renderer>().sharedMaterial = black;
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(3000, 2, 3004);
            wall.transform.localScale = new Vector3(20, 6, .2f); wall.GetComponent<Renderer>().sharedMaterial = black;
            Physics.SyncTransforms();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            actions = ((InputActionReference)typeof(PlayerController).GetField("moveAction", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player)).action.actionMap.asset;
            devices = actions.devices;
            keyboard = InputSystem.AddDevice<Keyboard>("PrefabSonarVerification"); actions.devices = new InputDevice[] { keyboard }; keyboard.MakeCurrent();
            player.SetInputCaptured(true); await Tick(300);
            target = new RenderTexture(640, 360, 24); target.Create(); camera.targetTexture = target;
            image = new Texture2D(640, 360, TextureFormat.RGB24, false);
            Func<int> capture = () => {
                var previous = RenderTexture.active;
                try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); image.Apply(); }
                finally { RenderTexture.active = previous; }
                return image.GetPixels32().Count(c => c.r > 8 || c.g > 8 || c.b > 8);
            };
            field.ClearPulses(); await Tick(120); int dark = capture();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); await Tick(650);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); await Tick(60);
            int lit = capture();
            Directory.CreateDirectory("Logs/PlayerPrefab"); File.WriteAllBytes("Logs/PlayerPrefab/direct-drag-pulse.png", image.EncodeToPNG());
            check(field.Emissions > 0 && field.LastHitCount > 0 && lit > dark + 100,
                "Enter on direct prefab instance reveals actual point pixels (" + dark + " -> " + lit + ")");
            int steps = foot.Steps;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); await Tick(800);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); await Tick(60);
            check(foot.Steps > steps && field.FootstepEmissions > 0, "Direct prefab footsteps emit into the same field");
            check(foot.audioSource == null && foot.landingClip == null, "No footstep audio fallback was introduced");
            return new { passed = true, checks };
        }
        finally
        {
            if (actions != null) actions.devices = devices;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = bg; InputSystem.settings.editorInputBehaviorInPlayMode = routing;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            for (int i = 0; i < originalRoots.Length; i++) if (originalRoots[i] != null) originalRoots[i].SetActive(active[i]);
        }
    }
}
