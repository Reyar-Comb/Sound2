using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Run with script run in SonarHall Play mode after confirming the player loop is advancing.
// Virtual devices drive real Input System events and PlayerController.Update; no movement methods are invoked.
public static class VerifySharedPlayer
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static T Read<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);

    static void Write(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

    static async Task Tick(int milliseconds = 150)
    {
        int before = Time.frameCount;
        await Task.Delay(milliseconds);
        if (!Application.isPlaying || Time.frameCount <= before)
            throw new InvalidOperationException("Play mode must keep advancing while the integration test awaits input frames.");
    }

    static async Task Keys(Keyboard keyboard, params Key[] keys)
    {
        keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        await Tick();
    }

    static async Task MouseEvent(Mouse mouse, Vector2 position, Vector2 delta, bool down = false)
    {
        mouse.MakeCurrent();
        var state = new MouseState { position = position, delta = delta };
        if (down) state = state.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(mouse, state);
        await Tick();
    }

    public static async Task<object> Main()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Open SonarHall and enter Play mode before running this verification.");

        var sonar = UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        if (sonar == null) throw new InvalidOperationException("No active SphericalSonar found.");
        var player = sonar.GetComponentInParent<PlayerController>();
        if (player == null) throw new InvalidOperationException("Sonar camera has no shared PlayerController parent.");
        var controller = player.GetComponent<CharacterController>();
        if (controller == null) throw new InvalidOperationException("Player has no CharacterController.");
        var scene = player.gameObject.scene;
        var camera = sonar.GetComponent<Camera>();
        var emitters = UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>()
            .Where(item => item.gameObject.scene == scene).ToArray();
        var actions = new[] { "moveAction", "lookAction", "verticalAction" }
            .Select(name => Read<InputActionReference>(player, name)).ToArray();
        if (actions.Any(reference => reference == null || reference.action == null))
            throw new InvalidOperationException("Player input action references are missing.");
        var actionAsset = actions[0].action.actionMap.asset;
        if (actions.Any(reference => reference.action.actionMap.asset != actionAsset))
            throw new InvalidOperationException("Expected the shared project input action asset.");

        var checks = new List<string>();
        var failures = new List<string>();
        Action<bool, string> check = (passed, message) =>
        {
            checks.Add((passed ? "PASS: " : "FAIL: ") + message);
            if (!passed) failures.Add(message);
        };
        Action<string, Action> cleanup = (label, action) =>
        {
            try { action(); }
            catch (Exception error) { failures.Add("Cleanup " + label + ": " + error.Message); }
        };

        var initialPosition = player.transform.position;
        var initialRotation = player.transform.rotation;
        var initialCameraPosition = camera.transform.localPosition;
        var initialCameraRotation = camera.transform.localRotation;
        float initialYaw = Read<float>(player, "yaw");
        float initialPitch = Read<float>(player, "pitch");
        bool initialSuspended = player.InputSuspended;
        bool initialGravity = player.UsesGravity;
        var cameraMotion = player.GetComponent<PlayerCameraMotion>();
        bool initialCameraMotion = cameraMotion != null && cameraMotion.enabled;
        bool initialControllerEnabled = controller.enabled;
        bool initialAutoSonar = sonar.automatic;
        var initialAutoEmitters = emitters.Select(emitter => emitter.automatic).ToArray();
        var initialDevices = actionAsset.devices;
        var initialKeyboard = Keyboard.current;
        var initialMouse = Mouse.current;
        var initialCursorLock = Cursor.lockState;
        bool initialCursorVisible = Cursor.visible;
        var initialInputSettings = InputSystem.settings;
        var initialBackgroundBehavior = initialInputSettings.backgroundBehavior;
        var initialEditorInputBehavior = initialInputSettings.editorInputBehaviorInPlayMode;
        int initialFrame = Time.frameCount;
        Keyboard keyboard = null;
        Mouse mouse = null;

        try
        {
            Write(player, "useGravity", false); // This regression suite exercises the optional free-flight mode.
            if (cameraMotion != null) cameraMotion.enabled = false;
            camera.transform.localPosition = initialCameraPosition; // Isolate controller offsets from visual head bob.
            controller.enabled = false;
            player.transform.position = new Vector3(1000, 10, 1000); // Keep movement probes clear of scene walls/ceilings.
            controller.enabled = initialControllerEnabled;
            Physics.SyncTransforms();
            // Play-mode objects can lose their editor prefab handle. Verify the actual saved instance link instead.
            string prefabGuid = AssetDatabase.AssetPathToGUID("Assets/Prefabs/Player.prefab");
            string sceneText = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", scene.path)));
            check(!string.IsNullOrEmpty(prefabGuid) && sceneText.Contains(
                "m_SourcePrefab: {fileID: 100100000, guid: " + prefabGuid + ", type: 3}"),
                "Saved scene instantiates the same Player.prefab used by BlackBox");
            check(UnityEngine.Object.FindObjectsByType<PlayerController>().Count(item => item.gameObject.scene == scene && item.isActiveAndEnabled) == 1
                && UnityEngine.Object.FindObjectsByType<CharacterController>().Count(item => item.gameObject.scene == scene && item.enabled) == 1
                && sonar.GetComponent<CharacterController>() == null && controller.gameObject == player.gameObject,
                "Exactly one player and root CharacterController; sonar camera has no duplicate movement capsule");
            check(UnityEngine.Object.FindObjectsByType<Camera>().Count(item => item.gameObject.scene == scene && item.isActiveAndEnabled) == 1
                && UnityEngine.Object.FindObjectsByType<AudioListener>().Count(item => item.gameObject.scene == scene && item.isActiveAndEnabled) == 1
                && UnityEngine.Object.FindObjectsByType<SteamAudio.SteamAudioListener>().Count(item => item.gameObject.scene == scene && item.isActiveAndEnabled) == 1,
                "Exactly one active camera, AudioListener, and SteamAudioListener");
            check(!UnityEngine.Object.FindObjectsByType<BouncePlaytest>().Any(item => item.gameObject.scene == scene && item.isActiveAndEnabled),
                "Legacy audio settings adapter does not run its old movement or input code");
            check(emitters.Length > 0 && emitters.All(emitter => emitter.pulseField == sonar.PulseField
                && (emitter.sonar == null || emitter.sonar == sonar)),
                "Object sound sources use the shared pulse field; optional legacy sonar references remain valid");
            check(player.isActiveAndEnabled && controller.enabled && actions.All(reference => reference.action.enabled),
                "Shared player controller and project-wide Move, Look, Vertical actions are enabled");

            sonar.automatic = false;
            foreach (var emitter in emitters) emitter.automatic = false;
            // Automation often runs with an unfocused Game view. Temporarily route queued virtual events
            // into Play mode; restore both properties below without saving an asset or replacing settings.
            initialInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            initialInputSettings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("SonarPlayerVerificationKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("SonarPlayerVerificationMouse");
            actionAsset.devices = new InputDevice[] { keyboard, mouse };
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
            player.SetInputCaptured(true);
            var pointer = new Vector2(Mathf.Max(1, Screen.width - 24), 24);
            await Keys(keyboard);
            await MouseEvent(mouse, pointer, Vector2.zero);

            var beforeMove = player.transform.position;
            var forward = player.transform.forward;
            await Keys(keyboard, Key.W);
            await Keys(keyboard);
            var displacement = player.transform.position - beforeMove;
            check(Vector3.Dot(displacement, forward) > .05f
                && Vector3.Distance(camera.transform.localPosition, initialCameraPosition) < .0001f,
                "W moves the shared player root forward while preserving the camera's local offset");

            float yawBefore = player.transform.eulerAngles.y;
            float pitchBefore = camera.transform.localEulerAngles.x;
            await MouseEvent(mouse, pointer, new Vector2(30, -20));
            await MouseEvent(mouse, pointer, Vector2.zero);
            check(Mathf.Abs(Mathf.DeltaAngle(yawBefore, player.transform.eulerAngles.y)) > .5f
                && Mathf.Abs(Mathf.DeltaAngle(pitchBefore, camera.transform.localEulerAngles.x)) > .5f
                && Mathf.Abs(Mathf.DeltaAngle(0, player.transform.eulerAngles.x)) < .001f
                && Mathf.Abs(Mathf.DeltaAngle(0, camera.transform.localEulerAngles.y)) < .001f,
                "Mouse look rotates root yaw and camera pitch without pitching the collider or duplicating camera yaw");

            float nextEmissionBeforeSpace = Read<float>(sonar, "nextEmission");
            float heightBefore = player.transform.position.y;
            await Keys(keyboard, Key.Space);
            await Keys(keyboard);
            check(player.transform.position.y > heightBefore + .05f && Read<float>(sonar, "nextEmission") == nextEmissionBeforeSpace,
                "Space ascends through the shared Vertical action without firing sonar");
            heightBefore = player.transform.position.y;
            await Keys(keyboard, Key.LeftShift);
            await Keys(keyboard);
            check(player.transform.position.y < heightBefore - .05f,
                "Left Shift descends through the shared Vertical action");

            var cooldownDeadline = DateTime.UtcNow.AddSeconds(3);
            while (Time.time < Read<float>(sonar, "nextEmission"))
            {
                if (DateTime.UtcNow > cooldownDeadline) throw new TimeoutException("Sonar emission cooldown did not expire.");
                await Tick(100);
            }
            float nextEmissionBeforeEnter = Read<float>(sonar, "nextEmission");
            await Keys(keyboard, Key.Enter);
            await Keys(keyboard);
            // Field totals and LastOrigin also include unrelated object/footstep pulses.
            var mainOrigins = Read<Vector4[]>(sonar.PulseField, "origins").Take(SonarPulseField.MainPulseCapacity);
            check(Read<float>(sonar, "nextEmission") > nextEmissionBeforeEnter
                && mainOrigins.Any(p => Vector3.Distance(new Vector3(p.x, p.y, p.z), camera.transform.position) < .001f),
                "Enter fires a main sonar pulse from the player's camera position");

            await Keys(keyboard, Key.Escape);
            await Keys(keyboard);
            check(player.InputSuspended, "Escape releases player input");
            beforeMove = player.transform.position;
            await Keys(keyboard, Key.W);
            await Keys(keyboard);
            check(Vector3.Distance(beforeMove, player.transform.position) < .001f,
                "W cannot move the player while input is released");
            await MouseEvent(mouse, pointer, Vector2.zero, true);
            await MouseEvent(mouse, pointer, Vector2.zero);
            check(!player.InputSuspended, "Clicking outside the HUD recaptures player input after Escape");
            await Keys(keyboard, Key.Tab);
            await Keys(keyboard);
            check(player.InputSuspended, "Tab also releases player input");
            await MouseEvent(mouse, pointer, Vector2.zero, true);
            await MouseEvent(mouse, pointer, Vector2.zero);
            check(!player.InputSuspended, "Clicking outside the HUD recaptures player input after Tab");
        }
        catch (Exception error)
        {
            failures.Add(error.ToString());
        }
        finally
        {
            cleanup("suspend test movement", () => player.SetInputCaptured(false));
            cleanup("restore action devices", () => actionAsset.devices = initialDevices);
            cleanup("remove virtual keyboard", () => { if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); });
            cleanup("remove virtual mouse", () => { if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); });
            cleanup("restore input background behavior", () => initialInputSettings.backgroundBehavior = initialBackgroundBehavior);
            cleanup("restore editor input routing", () => initialInputSettings.editorInputBehaviorInPlayMode = initialEditorInputBehavior);
            cleanup("restore current keyboard", () => { if (initialKeyboard != null && initialKeyboard.added) initialKeyboard.MakeCurrent(); });
            cleanup("restore current mouse", () => { if (initialMouse != null && initialMouse.added) initialMouse.MakeCurrent(); });
            cleanup("restore pose", () =>
            {
                controller.enabled = false;
                player.transform.SetPositionAndRotation(initialPosition, initialRotation);
                camera.transform.SetLocalPositionAndRotation(initialCameraPosition, initialCameraRotation);
                Write(player, "yaw", initialYaw);
                Write(player, "pitch", initialPitch);
                Physics.SyncTransforms();
            });
            cleanup("restore collider", () => controller.enabled = initialControllerEnabled);
            cleanup("restore gravity", () => Write(player, "useGravity", initialGravity));
            cleanup("restore camera motion", () => { if (cameraMotion != null) cameraMotion.enabled = initialCameraMotion; });
            cleanup("restore automatic emissions", () =>
            {
                sonar.automatic = initialAutoSonar;
                for (int i = 0; i < emitters.Length; i++) emitters[i].automatic = initialAutoEmitters[i];
            });
            cleanup("clear verification pulses", () => sonar.ClearPulses());
            cleanup("restore capture state", () =>
            {
                player.SetInputCaptured(!initialSuspended);
                Cursor.lockState = initialCursorLock;
                Cursor.visible = initialCursorVisible;
            });
        }

        return new { passed = failures.Count == 0, checks, failures, framesAdvanced = Time.frameCount - initialFrame };
    }
}
