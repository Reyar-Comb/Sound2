using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Run in agent-owned Play mode. Loads every playable scene; changes are runtime-only.
public static class VerifyScenePlayerDefaults
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Write(object o, string field, object value) => o.GetType().GetField(field, Flags).SetValue(o, value);
    static async Task Tick(int ms)
    {
        int frame = Time.frameCount;
        await Task.Delay(ms);
        if (!Application.isPlaying || Time.frameCount <= frame) throw new Exception("Player loop stopped advancing.");
    }

    // Pixel check on an isolated nearby wall; rendered by the normal URP camera loop.
    public static async Task<object> VerifyProximityPixels()
    {
        var sonar = UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        var camera = sonar.GetComponent<Camera>();
        var source = sonar.GetComponent<BlackBoxProximitySource>();
        var player = sonar.GetComponentInParent<PlayerController>();
        var motion = player.GetComponent<PlayerCameraMotion>();
        var oldTarget = camera.targetTexture;
        var oldPosition = camera.transform.position;
        var oldRotation = camera.transform.rotation;
        bool oldPlayer = player.enabled, oldMotion = motion.enabled;
        bool oldNear = sonar.proximityRendering, oldScan = sonar.sonarRendering, oldAuto = sonar.automatic;
        bool oldStars = source.IsStyleEnabled(BlackBoxProximitySource.PointStyle.Stars);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var target = new RenderTexture(640, 360, 24);
        var read = new Texture2D(640, 360, TextureFormat.RGB24, false);
        var frames = new List<object>();
        try
        {
            player.enabled = motion.enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(100, 100, 100), Quaternion.identity);
            wall.transform.position = new Vector3(100, 100, 101.5f);
            wall.transform.localScale = new Vector3(10, 10, .1f);
            wall.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            Physics.SyncTransforms();
            sonar.automatic = false;
            source.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars, false);
            target.Create(); camera.targetTexture = target;
            int[] lit = new int[3];
            System.IO.Directory.CreateDirectory("Logs/ProximityDefault");
            for (int i = 0; i < 3; i++)
            {
                sonar.ClearPulses(); sonar.SetRevealModes(i == 1, true);
                await Tick(200);
                var oldActive = RenderTexture.active;
                try { RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); read.Apply(); }
                finally { RenderTexture.active = oldActive; }
                lit[i] = read.GetPixels32().Count(c => c.r > 8 || c.g > 8 || c.b > 8);
                string path = "Logs/ProximityDefault/" + (i == 1 ? "enabled" : "disabled-" + i) + ".png";
                System.IO.File.WriteAllBytes(path, read.EncodeToPNG());
                frames.Add(new { enabled = i == 1, litPixels = lit[i], path });
            }
            return new { passed = lit[1] > lit[0] + 100 && lit[2] == lit[0], frames };
        }
        finally
        {
            camera.targetTexture = oldTarget;
            camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
            player.enabled = oldPlayer; motion.enabled = oldMotion;
            sonar.automatic = oldAuto; sonar.SetRevealModes(oldNear, oldScan);
            source.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars, oldStars);
            UnityEngine.Object.Destroy(wall); UnityEngine.Object.Destroy(read);
            target.Release(); UnityEngine.Object.Destroy(target);
        }
    }

    public static async Task<object> Main()
    {
        if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
        var checks = new List<string>();
        var failures = new List<string>();
        var reports = new List<object>();
        Action<bool, string> check = (pass, label) => { checks.Add((pass ? "PASS: " : "FAIL: ") + label); if (!pass) failures.Add(label); };
        var settings = InputSystem.settings;
        var oldBackground = settings.backgroundBehavior;
        var oldRouting = settings.editorInputBehaviorInPlayMode;
        var oldKeyboard = Keyboard.current;
        var keyboard = InputSystem.AddDevice<Keyboard>("SceneFootstepsVerification");
        Func<Key[], int, Task> keys = async (pressed, ms) => {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed)); await Tick(ms);
        };
        string[] names = { "SampleScene", "LevelEditorDemo", "test", "BlackBoxPointCloud", "SeededMapDemo", "BaiYu-Test",
            "TeammateSonarHall", "CubeRoom", "MazeRoom", "CircularHall", "SonarHall" };
        try
        {
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (var name in names)
            {
                await keys(Array.Empty<Key>(), 60);
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + name + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
                await Tick(350);
                foreach (var menu in UnityEngine.Object.FindObjectsByType<SonicSpaceStartMenu>()) menu.enabled = false;
                await Tick(80);
                var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
                var legacy = UnityEngine.Object.FindObjectsByType<BouncePlaytest>().FirstOrDefault(b => b.isActiveAndEnabled);
                var actor = player != null ? player.gameObject : legacy.gameObject;
                var foot = actor.GetComponent<FootstepEmitter>();
                check(actor.GetComponents<FootstepEmitter>().Length == 1 && foot.isActiveAndEnabled && foot.emissionEnabled && foot.soundEnabled,
                    name + ": exactly one enabled footstep emitter");
                var style = actor.GetComponentsInChildren<BlackBoxProximitySource>().FirstOrDefault(c => c.isActiveAndEnabled);
                var sonar = actor.GetComponentsInChildren<SphericalSonar>().FirstOrDefault(c => c.isActiveAndEnabled);
                if (style != null)
                {
                    check(!style.ProximityRendering && (sonar == null || !sonar.proximityRendering)
                        && Shader.GetGlobalVector("_BlackBoxRevealModes").x == 0, name + ": proximity disabled on entry");
                    if (sonar != null) sonar.SetRevealModes(true, true); else style.SetRevealModes(true, true);
                    await Tick(60);
                    check(Shader.GetGlobalVector("_BlackBoxRevealModes").x == 1, name + ": explicit opt-in still reaches Shader");
                    if (sonar != null) sonar.SetRevealModes(false, true); else style.SetRevealModes(false, true);
                }

                var controller = actor.GetComponent<CharacterController>();
                if (controller != null) controller.enabled = false;
                actor.transform.SetPositionAndRotation(new Vector3(1000, 10, 1000), Quaternion.identity);
                if (controller != null) controller.enabled = true;
                if (player != null)
                {
                    Write(player, "useGravity", false); Write(player, "yaw", 0f); player.SetInputCaptured(true);
                }
                else
                {
                    legacy.movementMin = new Vector2(900, 900); legacy.movementMax = new Vector2(1100, 1100);
                    Write(legacy, "yaw", 0f); Write(legacy, "captured", true);
                }
                Physics.SyncTransforms(); foot.ResetStride();
                int audioStarts = 0;
                Action<Vector3> onStep = _ => { if (foot.audioSource != null && foot.audioSource.isPlaying) audioStarts++; };
                foot.Stepped += onStep;
                int before = foot.Steps;
                await keys(new[] { Key.W }, 1200);
                await keys(Array.Empty<Key>(), 80);
                int walked = foot.Steps - before;
                check(walked >= 2, name + ": real W movement triggers steps (" + walked + ")");
                if (style != null) check(foot.pulseField != null && foot.pulseField.FootstepEmissions >= 2,
                    name + ": footsteps reveal through the shared pulse field");
                check(foot.audioSource == null && audioStarts == 0,
                    name + ": unassigned audio stays silent without creating a fallback source");
                before = foot.Steps;
                await Tick(250);
                check(foot.Steps == before, name + ": idle does not emit footsteps");
                foot.soundEnabled = false;
                audioStarts = 0;
                await keys(new[] { Key.W }, 650);
                await keys(Array.Empty<Key>(), 80);
                check(foot.Steps > before && audioStarts == 0, name + ": audio switch is independent of step events");
                foot.soundEnabled = true;
                foot.Stepped -= onStep;
                reports.Add(new { scene = name, steps = walked, legacy = player == null });
            }
        }
        catch (Exception e) { failures.Add(e.ToString()); }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
            settings.backgroundBehavior = oldBackground;
            settings.editorInputBehaviorInPlayMode = oldRouting;
        }
        return new { passed = failures.Count == 0, scenes = reports, checks, failures };
    }
}
