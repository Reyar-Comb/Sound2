using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Run via Unity CLI script eval in Play Mode; no scene changes are saved.
if (!Application.isPlaying) throw new InvalidOperationException("Requires Play Mode");
const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
var checks = new List<string>();
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
var original = SceneManager.GetActiveScene();
var roots = original.GetRootGameObjects().Where(g => g.activeSelf).ToArray();
var settings = InputSystem.settings;
var background = settings.backgroundBehavior;
var routing = settings.editorInputBehaviorInPlayMode;
var oldKeyboard = Keyboard.current;
Scene test = default;
Keyboard keyboard = null;
InputActionAsset actions = null;
UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? devices = null;
try {
    foreach (var root in roots) root.SetActive(false);
    test = EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/audiotest.unity", new LoadSceneParameters(LoadSceneMode.Additive));
    for (int i = 0; i < 30 && !test.isLoaded; i++) await Task.Delay(100);
    SceneManager.SetActiveScene(test);
    if (SonicSpaceStartMenu.BlocksGameplay) Object.FindAnyObjectByType<SonicSpaceStartMenu>().BeginGame();
    var player = test.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerController>()).Single();
    var foot = player.GetComponent<FootstepEmitter>();
    var field = foot.pulseField;
    var source = foot.audioSource;
    Check(foot.emissionEnabled && source != null && field != null, "audiotest inherits enabled feet and an explicit audio source");
    Check(foot.clips.Select(c => c.name).SequenceEqual(new[] { "step1", "step2", "step3", "step4" }), "All four recordings are linked in list order");
    Check(foot.clips.All(c => c.loadState == AudioDataLoadState.Loaded), "The four samples are loaded before the first step");
    Check(!source.playOnAwake && !source.loop && foot.landingClip == null, "No autoplay, loop or unintended landing recording");
    foreach(var e in Object.FindObjectsByType<AudioReactiveSonarEmitter>()) e.Stop();
    foreach(var e in Object.FindObjectsByType<SonarObjectEmitter>()) e.automatic = false;
    player.GetComponentInChildren<SphericalSonar>().automatic = false;
    player.FaceDirection(Vector3.forward);
    player.SetInputCaptured(true);
    foot.stepDistance = .55f;
    settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
    settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
    actions = ((InputActionReference)typeof(PlayerController).GetField("moveAction", flags).GetValue(player)).action.actionMap.asset;
    devices = actions.devices;
    keyboard = InputSystem.AddDevice<Keyboard>("FootstepAudioVerification");
    actions.devices = new InputDevice[] {keyboard}; keyboard.MakeCurrent();
    foot.ResetStride(); await Task.Delay(350);
    Check(!source.isPlaying, "Idle player does not play footstep audio");
    int start = foot.Steps, pulses = field.FootstepEmissions;
    var played = new List<int>();
    var atStep = new List<bool>();
    int noise = 0;
    var clipIndex = typeof(FootstepEmitter).GetField("clipIndex", flags);
    void Hear(GameplayNoiseEvent evt) { if (evt.source == foot && evt.kind == GameplayNoiseKind.Footstep) noise++; }
    void Step(Vector3 origin) {
        played.Add((int)clipIndex.GetValue(foot));
        atStep.Add(source.isPlaying && field.FootstepEmissions == pulses + played.Count && field.LastOrigin == origin);
    }
    GameplayNoise.Emitted += Hear; foot.Stepped += Step;
    try {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        for(int i=0;i<80&&played.Count<5;i++) await Task.Delay(20);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); await Task.Delay(100);
        Check(played.Take(5).SequenceEqual(new[]{1,2,3,0,1}), "Actual W movement plays 1, 2, 3, 4, then 1 again");
        Check(atStep.All(x=>x) && foot.Steps-start==played.Count && noise==played.Count,
            "Each step starts playback, publishes one wave at the same origin, and emits one AI event");
        var samples = new float[1024]; float peak = 0;
        for(int i=0;i<6;i++){ source.GetOutputData(samples,0); peak=Mathf.Max(peak,samples.Max(x=>Mathf.Abs(x))); await Task.Delay(20); }
        Check(peak > .0005f, "The configured source outputs a non-silent waveform");
        int before = foot.Steps;
        await Task.Delay(250);
        Check(foot.Steps==before, "No repeat triggers after releasing movement");
        foot.soundEnabled=false; source.Stop();
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.S));
        for(int i=0;i<70&&foot.Steps==before;i++) await Task.Delay(20);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); await Task.Delay(90);
        Check(foot.Steps>before && !source.isPlaying && field.FootstepEmissions>pulses,
            "Sound Enabled can mute recordings without suppressing step waves");
    } finally { GameplayNoise.Emitted -= Hear; foot.Stepped -= Step; }
    System.IO.Directory.CreateDirectory("Logs/FootstepAudio");
    System.IO.File.WriteAllLines("Logs/FootstepAudio/play-checks.txt", checks);
    return new { passed=true, checks };
} finally {
    if (actions!=null) actions.devices=devices;
    if (keyboard!=null) InputSystem.RemoveDevice(keyboard);
    if (oldKeyboard!=null && oldKeyboard.added) oldKeyboard.MakeCurrent();
    settings.backgroundBehavior=background; settings.editorInputBehaviorInPlayMode=routing;
    SceneManager.SetActiveScene(original);
    if (test.IsValid() && test.isLoaded) await SceneManager.UnloadSceneAsync(test);
    foreach(var root in roots) if(root!=null)root.SetActive(true);
}
