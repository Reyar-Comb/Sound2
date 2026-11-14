using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class VerifyRadioPower
{
    public static async Task<object> Main()
    {
        if (!Application.isPlaying || SonicSpaceStartMenu.BlocksGameplay)
            throw new Exception("Run test in Play mode with the start menu closed.");
        var radio = Object.FindObjectsByType<AudioReactiveSonarEmitter>().Single(e => e.name == "Radio");
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        Check(!radio.IsLoopPlaying && !radio.playOnStart, "Existing test Radio starts off");
        Check(radio.analysis.IsReady, "Original recording has baked peak data");
        Check(radio.GetComponents<AudioReactiveSonarEmitter>().Length == 1
            && radio.GetComponents<ImpactSoundPool>().Length == 1, "One emitter and audio pool");
        var players = Object.FindObjectsByType<PlayerController>();
        var previousMouse = Mouse.current;
        var mouse = InputSystem.AddDevice<Mouse>();
        GameObject fixture = null;
        try
        {
            foreach (var p in players) p.gameObject.SetActive(false);
            fixture = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            fixture.name = "Radio power verification player";
            var controller = fixture.GetComponent<PlayerController>();
            var so = new SerializedObject(controller);
            so.FindProperty("useGravity").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            var interaction = fixture.GetComponent<PlayerInteraction>();
            var action = radio.GetComponent<InteractableObject>();
            var camera = interaction.viewCamera;
            typeof(PlayerController).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(controller, 60f);
            camera.transform.localRotation = Quaternion.Euler(60, 0, 0);
            fixture.transform.position += action.InteractionPosition - camera.transform.forward * 1.8f - camera.transform.position;
            Physics.SyncTransforms();
            await Task.Delay(500);
            Check(interaction.FocusedTarget == action && interaction.RingProgress > .05f,
                "Actual scene Radio receives focus and ring (target=" + interaction.FocusedTarget + ", camera=" + camera.transform.position + ")");
            async Task Click()
            {
                mouse.MakeCurrent();
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
                await Task.Delay(100);
                InputSystem.QueueStateEvent(mouse, new MouseState());
                await Task.Delay(250);
            }
            int initialTriggers = radio.Triggers;
            await Click();
            Check(radio.IsLoopPlaying && radio.Triggers == initialTriggers + 1, "First mouse click starts loop once");
            var playback = (ImpactSoundPool.Playback)typeof(AudioReactiveSonarEmitter)
                .GetField("playback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(radio);
            Check(playback != null && playback.Source.isPlaying && playback.Source.clip == radio.analysis.clip,
                "Spatial AudioSource plays the original recording");
            Check(radio.field != null && radio.field.ActiveSourceCount > 0 && radio.field.OcclusionCaptures > 0
                && Shader.GetGlobalFloat("_AudioSonarActive") == 1, "Peak waves and occlusion data reach shader");
            Directory.CreateDirectory("Logs/RadioPower");
            ScreenCapture.CaptureScreenshot("Logs/RadioPower/on.png");
            await Task.Delay(300);
            await Click();
            Check(!radio.IsLoopPlaying && radio.Triggers == initialTriggers + 1, "Second mouse click stops loop without replay");
            await Task.Delay(3000);
            Check(radio.field.ActiveSourceCount == 0 && Shader.GetGlobalFloat("_AudioSonarActive") == 0,
                "Previously emitted waves fade after power off");
            Check(!playback.Source.isPlaying, "Audio stops after power off");
            await Click();
            Check(radio.IsLoopPlaying && radio.Triggers == initialTriggers + 2, "Third mouse click restarts loop once");
            File.WriteAllLines("Logs/RadioPower/verification.txt", checks);
            return new { passed = true, checks, screenshot = "Logs/RadioPower/on.png" };
        }
        finally
        {
            radio.Stop();
            if (fixture != null) Object.Destroy(fixture);
            InputSystem.RemoveDevice(mouse);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            foreach (var p in players) if (p != null) p.gameObject.SetActive(true);
        }
    }
}
