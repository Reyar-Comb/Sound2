using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Exercises actual PlayerController input and displacement, plus pulse-bank isolation.
public static class VerifyFootsteps
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Read<T>(object o, string n) => (T)o.GetType().GetField(n, Flags).GetValue(o);
    static void Write(object o, string n, object v) => o.GetType().GetField(n, Flags).SetValue(o, v);
    static async Task Tick(int ms = 180) {
        int frame = Time.frameCount;
        await Task.Delay(ms);
        if (!Application.isPlaying || Time.frameCount <= frame) throw new Exception("Player loop did not advance");
    }
    public static async Task<object> Main()
    {
        if (!Application.isPlaying) throw new Exception("Enter Play mode first");
        var sonar = UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        var player = sonar.GetComponentInParent<PlayerController>();
        var foot = player.GetComponent<FootstepEmitter>();
        var controller = player.GetComponent<CharacterController>();
        var field = sonar.PulseField;
        var actions = Read<InputActionReference>(player, "moveAction").action.actionMap.asset;
        var devices = actions.devices;
        var keyboardBefore = Keyboard.current;
        var settings = InputSystem.settings;
        var bg = settings.backgroundBehavior;
        var routing = settings.editorInputBehaviorInPlayMode;
        var position = player.transform.position;
        var rotation = player.transform.rotation;
        float yaw = Read<float>(player, "yaw");
        bool suspended = player.InputSuspended, auto = sonar.automatic, emission = foot.emissionEnabled;
        bool grounded = foot.requireGrounded;
        float stepDistance = foot.stepDistance, minimumInterval = foot.minimumInterval, range = foot.range;
        var distanceSettings = foot.distanceSettings;
        var objects = UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>();
        var automatic = Array.ConvertAll(objects, o => o.automatic);
        Keyboard keyboard = null;
        GameObject wall = null;
        int events = 0;
        Action<Vector3> handler = _ => events++;
        var checks = new List<string>();
        var failures = new List<string>();
        Action<bool, string> check = (pass, label) => {
            checks.Add((pass ? "PASS: " : "FAIL: ") + label);
            if (!pass) failures.Add(label);
        };
        Func<Key[], Task> keys = async pressed => {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));
            await Tick();
        };
        try {
            sonar.automatic = false;
            foreach (var o in objects) o.automatic = false;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("FootstepVerification");
            actions.devices = new InputDevice[] {keyboard};
            player.SetInputCaptured(true);
            foot.emissionEnabled = true;
            foot.requireGrounded = false;
            // Exercise known boundary values independently of the user's live Inspector tuning.
            foot.stepDistance = 1.5f; foot.minimumInterval = .18f; foot.range = 3;
            foot.distanceSettings = new SonarDistanceSettings { enabled = true,
                fadeStartDistance = .4f, fadeEndDistance = 3, falloffPower = 2, pulseStrength = .6f };
            foot.Stepped += handler;
            controller.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(1000, 10, 1000), Quaternion.identity);
            Write(player, "yaw", 0f);
            controller.enabled = true;
            Physics.SyncTransforms();
            foot.ResetStride();
            await keys(new Key[0]);
            field.ClearPulses();
            int before = foot.Steps;
            await Tick(350);
            check(foot.Steps == before, "Standing still emits no footsteps");
            await keys(new[] {Key.W});
            await Tick(550);
            await keys(new Key[0]);
            check(foot.Steps >= before + 2, "Actual W travel emits repeated footsteps");
            check(events == foot.Steps - before, "Each footstep invokes the reusable event exactly once");
            check(field.FootstepEmissions >= 2 && field.LastRayCount == 3456,
                "Footsteps emit through the shared field with 3456 ray samples");
            var basePosition = player.transform.TransformPoint(controller.center) - Vector3.up * controller.height * .5f;
            check(Mathf.Abs(foot.LastOrigin.y - basePosition.y - foot.originHeight) < .001f,
                "Pulse origin is near the capsule base rather than camera height");
            before = foot.Steps;
            var fixedOrigin = field.LastOrigin;
            await Tick(400);
            check(foot.Steps == before && field.LastOrigin == fixedOrigin, "Stopping ends emissions and keeps the last wave origin fixed");
            await keys(new[] {Key.Space});
            await Tick(250);
            await keys(new Key[0]);
            check(foot.Steps == before, "Pure vertical movement emits no footsteps");
            player.transform.rotation = Quaternion.Euler(0, 45, 0);
            Write(player, "yaw", 45f);
            await Tick();
            check(foot.Steps == before, "Turning without translation emits no footsteps");
            await keys(new[] {Key.Digit6});
            await keys(new[] {Key.W});
            await Tick(350);
            await keys(new Key[0]);
            check(!foot.emissionEnabled && foot.Steps == before, "Key 6 disables footstep pulses while movement continues");
            await keys(new[] {Key.Digit6});
            await keys(new Key[0]);
            check(foot.emissionEnabled, "Key 6 re-enables footsteps");
            player.SetInputCaptured(false);
            await keys(new[] {Key.W});
            await Tick(350);
            await keys(new Key[0]);
            check(foot.Steps == before, "Released player input emits no footsteps");
            player.SetInputCaptured(true);
            controller.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(1000, 10, 1000), Quaternion.identity);
            Write(player, "yaw", 0f);
            controller.enabled = true;
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Temporary footstep blocking wall";
            wall.transform.position = player.transform.position + Vector3.forward * .8f;
            wall.transform.localScale = new Vector3(10, 10, .5f);
            Physics.SyncTransforms();
            foot.ResetStride();
            await keys(new[] {Key.W});
            await Tick(650);
            await keys(new Key[0]);
            check(foot.Steps == before, "Holding W against a wall does not generate steps from input alone");
            UnityEngine.Object.DestroyImmediate(wall); wall = null;
            player.transform.position += Vector3.right * 20;
            await Tick();
            check(foot.Steps == before, "Teleporting resets stride without emitting a step");
            foot.requireGrounded = true;
            await keys(new[] {Key.W});
            await Tick(350);
            await keys(new Key[0]);
            check(foot.Steps == before, "Optional grounded mode suppresses airborne footsteps");
            foot.requireGrounded = false;
            field.ClearPulses();
            field.EmitFrom(new Vector3(900, 10, 900), 12, 8, 1);
            var origins = Read<Vector4[]>(field, "origins");
            var parameters = Read<Vector4[]>(field, "parameters");
            var falloffs = Read<Vector4[]>(field, "distanceFalloffs");
            var savedOrigins = (Vector4[])origins.Clone();
            var savedParameters = (Vector4[])parameters.Clone();
            for(int i = 0; i < 7; i++)
                field.EmitFootstepFrom(new Vector3(1000 + i, 10, 1000), foot.range, foot.waveSpeed, foot.afterglow, foot.distanceSettings);
            bool mainUnchanged = true, footConfigured = true;
            for(int i = 0; i < 4; i++) mainUnchanged &= origins[i] == savedOrigins[i] && parameters[i] == savedParameters[i];
            for(int i = 4; i < 8; i++) footConfigured &= parameters[i].w == 1 && falloffs[i].y == 3 && falloffs[i].z == 2 && falloffs[i].w == .6f;
            check(mainUnchanged, "More than four footsteps never overwrite the four main pulse slots");
            check(footConfigured, "All four footstep slots retain their own 3m falloff and strength");
        } catch(Exception e) { failures.Add(e.ToString()); }
        finally {
            player.SetInputCaptured(false);
            foot.Stepped -= handler;
            if(wall != null) UnityEngine.Object.DestroyImmediate(wall);
            actions.devices = devices;
            if(keyboard != null) InputSystem.RemoveDevice(keyboard);
            if(keyboardBefore != null && keyboardBefore.added) keyboardBefore.MakeCurrent();
            settings.backgroundBehavior = bg;
            settings.editorInputBehaviorInPlayMode = routing;
            controller.enabled = false;
            player.transform.SetPositionAndRotation(position, rotation);
            Write(player, "yaw", yaw);
            controller.enabled = true;
            Physics.SyncTransforms();
            foot.emissionEnabled = emission;
            foot.requireGrounded = grounded;
            foot.stepDistance = stepDistance; foot.minimumInterval = minimumInterval; foot.range = range;
            foot.distanceSettings = distanceSettings;
            foot.ResetStride();
            sonar.automatic = auto;
            for(int i = 0; i < objects.Length; i++) objects[i].automatic = automatic[i];
            field.ClearPulses();
            player.SetInputCaptured(!suspended);
        }
        return new { passed = failures.Count == 0, checks, failures };
    }
}
