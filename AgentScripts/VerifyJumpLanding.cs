using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Run in agent-owned SonarHall Play mode; all fixture changes disappear on exiting Play.
public static class VerifyJumpLanding
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Read<T>(object o, string f) => (T)o.GetType().GetField(f, Flags).GetValue(o);
    static async Task Tick(int ms = 150)
    {
        int frame = Time.frameCount;
        await Task.Delay(ms);
        if (!Application.isPlaying || Time.frameCount <= frame) throw new Exception("Player loop stopped.");
    }

    public static async Task<object> Main()
    {
        if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        var foot = player.GetComponent<FootstepEmitter>();
        var capsule = player.GetComponent<CharacterController>();
        var sonar = player.GetComponentInChildren<SphericalSonar>();
        var actions = Read<InputActionReference>(player, "moveAction").action.actionMap.asset;
        var oldDevices = actions.devices;
        var oldKeyboard = Keyboard.current;
        var oldBackground = InputSystem.settings.backgroundBehavior;
        var oldRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
        var keyboard = InputSystem.AddDevice<Keyboard>("JumpLandingVerification");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject ceiling = null;
        var noises = new List<GameplayNoiseEvent>();
        Action<GameplayNoiseEvent> heard = n => { if (n.source == foot) noises.Add(n); };
        var checks = new List<string>(); var failures = new List<string>();
        Action<bool, string> check = (pass, text) => { checks.Add((pass ? "PASS: " : "FAIL: ") + text); if (!pass) failures.Add(text); };
        Func<Key[], int, Task> keys = async (pressed, ms) => {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed)); await Tick(ms);
        };
        int impactFrame = -1, stepsAtLanding = -1;
        Action<Vector3> landed = _ => { impactFrame = Time.frameCount; stepsAtLanding = foot.Steps; };
        bool duplicateStep = false;
        Action<Vector3> stepped = _ => { if (Time.frameCount == impactFrame) duplicateStep = true; };
        AudioSource testSource = null; AudioClip testClip = null;
        try
        {
            check(player.UsesGravity && actions.FindAction("Player/Jump").enabled, "Shared Player enables gravity and uses the existing Jump input");
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            actions.devices = new InputDevice[] { keyboard };
            player.SetInputCaptured(true); sonar.automatic = false;
            foreach (var emitter in UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>()) emitter.automatic = false;
            floor.name = "Jump verification floor";
            floor.transform.position = new Vector3(1000, -.5f, 1000);
            floor.transform.localScale = new Vector3(60, 1, 60);
            Physics.SyncTransforms();
            player.Teleport(new Vector3(1000, 1.1f, 1000));
            foot.ResetStride();
            GameplayNoise.Emitted += heard; foot.Landed += landed; foot.Stepped += stepped;
            await keys(Array.Empty<Key>(), 400);
            check(capsule.isGrounded && foot.Landings == 0, "Initial ground settling is silent");
            int sources = player.GetComponents<AudioSource>().Length;
            int before = foot.Steps;
            await keys(new[] { Key.W }, 700);
            await keys(Array.Empty<Key>(), 80);
            check(foot.Steps > before && noises.Any(n => n.kind == GameplayNoiseKind.Footstep), "Grounded walking still emits footsteps and hearing events");
            check(foot.audioSource == null && player.GetComponents<AudioSource>().Length == sources,
                "Unassigned walking audio does not create a fallback AudioSource");
            float groundY = player.transform.position.y;
            before = foot.Steps;
            await keys(new[] { Key.Space, Key.W }, 180);
            check(!capsule.isGrounded && player.transform.position.y > groundY + .4f, "Space starts a grounded jump");
            await keys(new[] { Key.W }, 80);
            await keys(new[] { Key.Space, Key.W }, 220);
            check(Read<float>(player, "fallSpeed") < 0, "Pressing Space again in the air does not double jump");
            check(foot.Steps == before, "Horizontal airborne movement does not emit ordinary steps");
            await keys(new[] { Key.Space }, 650);
            check(capsule.isGrounded && foot.Landings == 1, "Holding Space lands once without automatic repeat jumping");
            check(!duplicateStep && stepsAtLanding == before, "Landing does not duplicate an ordinary step in the same frame");
            var impact = noises.Last(n => n.kind == GameplayNoiseKind.Impact);
            check(Mathf.Approximately(impact.range, foot.hearingRange * foot.landingMultiplier), "Landing uses a stronger Impact hearing event (2x footstep range)");
            var parameters = Read<Vector4[]>(sonar.PulseField, "parameters");
            var falloffs = Read<Vector4[]>(sonar.PulseField, "distanceFalloffs");
            check(Enumerable.Range(4, 4).Any(i => Mathf.Approximately(parameters[i].x, foot.range * foot.landingMultiplier)
                && Mathf.Approximately(falloffs[i].y, foot.distanceSettings.fadeEndDistance * foot.landingMultiplier)
                && falloffs[i].w > foot.distanceSettings.pulseStrength), "Landing expands and brightens the existing footstep pulse bank");
            check(foot.audioSource == null && player.GetComponents<AudioSource>().Length == sources,
                "Unassigned landing audio stays silent without generating audio");
            await keys(Array.Empty<Key>(), 80);
            player.SetInputCaptured(false);
            await keys(new[] { Key.Space }, 180);
            check(capsule.isGrounded && foot.Landings == 1, "Suspended input cannot jump");
            await keys(Array.Empty<Key>(), 80); player.SetInputCaptured(true);

            ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.transform.position = new Vector3(player.transform.position.x, 2.5f, player.transform.position.z);
            ceiling.transform.localScale = new Vector3(10, .2f, 10);
            Physics.SyncTransforms();
            await keys(new[] { Key.Space }, 170);
            check(Read<float>(player, "fallSpeed") <= 0 && player.transform.position.y < groundY + .5f,
                "Ceiling collision cancels upward velocity");
            await keys(Array.Empty<Key>(), 500);
            UnityEngine.Object.DestroyImmediate(ceiling); ceiling = null;
            int landingsBefore = foot.Landings;
            player.Teleport(new Vector3(1000, 1.05f, 1000)); foot.ResetStride();
            await Tick(250);
            check(foot.Landings == landingsBefore, "Teleporting onto the floor does not fabricate a landing impact");
            player.Teleport(new Vector3(1000, 5, 1000)); foot.ResetStride();
            await Tick(950);
            check(capsule.isGrounded && foot.Landings == landingsBefore + 1, "A real fall after teleport emits one landing");

            // Explicitly assigned test resources exercise the audio interface; these are not game fallback assets.
            testSource = player.gameObject.AddComponent<AudioSource>(); testSource.playOnAwake = false;
            testClip = AudioClip.Create("Assigned test waveform", 48000, 1, 48000, false);
            var data = Enumerable.Range(0, 48000).Select(i => .2f * Mathf.Sin(2 * Mathf.PI * 220 * i / 48000)).ToArray();
            testClip.SetData(data, 0);
            foot.audioSource = testSource; foot.clips = new[] { testClip }; foot.landingClip = testClip;
            var buffer = new float[1024];
            Func<Task<float>> measure = async () => {
                float peak = 0;
                for (int i = 0; i < 8; i++)
                {
                    testSource.GetOutputData(buffer, 0);
                    peak = Mathf.Max(peak, Mathf.Sqrt(buffer.Sum(v => v * v) / buffer.Length));
                    await Tick(40);
                }
                return peak;
            };
            testSource.GetOutputData(buffer, 0); // Prime Unity's output sample buffer before the first measurement.
            typeof(FootstepEmitter).GetMethod("PlayStepSound", Flags).Invoke(foot, null);
            float stepRms = await measure(); testSource.Stop(); await Tick(120);
            typeof(FootstepEmitter).GetMethod("EmitLanding", Flags).Invoke(foot, new object[] { 7f });
            float landingRms = await measure(); testSource.Stop();
            check(stepRms > .001f && landingRms > stepRms * 1.7f, "Explicit landing audio is louder than a step (RMS " + stepRms + " -> " + landingRms + ")");
            foot.landingClip = null;
            typeof(FootstepEmitter).GetMethod("EmitLanding", Flags).Invoke(foot, new object[] { 7f });
            check(!testSource.isPlaying, "Missing landing clip does not fall back to the footstep clip");
            foot.clips = Array.Empty<AudioClip>();
            typeof(FootstepEmitter).GetMethod("PlayStepSound", Flags).Invoke(foot, null);
            check(!testSource.isPlaying, "Missing footstep clips stay silent even with an assigned source");
        }
        catch (Exception e) { failures.Add(e.ToString()); }
        finally
        {
            GameplayNoise.Emitted -= heard; foot.Landed -= landed; foot.Stepped -= stepped;
            foot.audioSource = null; foot.clips = Array.Empty<AudioClip>(); foot.landingClip = null;
            if (testSource != null) UnityEngine.Object.Destroy(testSource);
            if (testClip != null) UnityEngine.Object.Destroy(testClip);
            if (ceiling != null) UnityEngine.Object.Destroy(ceiling);
            UnityEngine.Object.Destroy(floor);
            actions.devices = oldDevices;
            InputSystem.RemoveDevice(keyboard);
            if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldRouting;
        }
        return new { passed = failures.Count == 0, checks, failures };
    }
}
