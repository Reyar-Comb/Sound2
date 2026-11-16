using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEditor;
using UnityEditorInternal;
using Newtonsoft.Json;
using Sound2.LevelEditing;
using Object = UnityEngine.Object;

// Runs outside Assets: no import/domain reload and no saved gameplay changes.
// Compilation, fixture setup and JSON serialization happen outside the capture.
public static class ProfileHomeGrocery
{
    const string Dir = "Logs/HomeGroceryProfile/";
    static async Task Until(Func<bool> condition)
    {
        double end = EditorApplication.timeSinceStartup + 90;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > end) throw new Exception("Transition timed out");
            await Task.Delay(20);
        }
    }
    public static async Task<object> Run(string label, bool held = false, bool lean = false)
    {
        if (!Application.isPlaying) throw new Exception("Requires Play Mode");
        Directory.CreateDirectory(Dir);
        var world = Object.FindAnyObjectByType<LevelWorld>();
        var menu = Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if (menu != null && !menu.HasStarted) menu.BeginGame();
        await Until(() => !SonicSpaceStartMenu.BlocksGameplay);
        var home = world.levels.First(l => l.name.Contains("家"));
        var connection = world.connections.First(c => c.source.Level == home);
        var market = connection.destination.Level;
        typeof(LevelWorld).GetMethod("Activate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world, new object[] { home });
        connection.source.Use(world.player.GetComponent<PlayerInteraction>(), false);
        await Until(() => connection.source.DoorOpen > .999f);
        world.player.Teleport(connection.source.transform.TransformPoint(new Vector3(0, 1.1f, 0)));
        if (held)
        {
            var picker = world.player.GetComponent<PlayerPickup>();
            if (picker.HeldItem == null)
            {
                var item = home.GetComponentsInChildren<PickupAlarm>().First().GetComponent<PickupItem>();
                if (!item.TryPickUp(picker, picker.holdPoint)) throw new Exception("Pickup failed");
                typeof(PlayerPickup).GetField("heldItem", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(picker, item);
            }
        }
        var items = market.GetComponentsInChildren<PickupItem>(true);
        int breaks = 0;
        Action onBreak = () => breaks++;
        foreach (var item in items) item.Broken += onBreak;
        await Task.Delay(1500);
        var events = new List<object>();
        var rows = new List<Frame>(3000);
        int lastFrame = -1; string lastPhase = "";
        Action<string> stamp = name => events.Add(new { name, frame = Time.frameCount, profilerFrame = ProfilerDriver.lastFrameIndex, time = Time.realtimeSinceStartupAsDouble });
        EditorApplication.CallbackFunction tick = () =>
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            string phase = market.IsPreparing ? "activating" : world.CurrentLevel == market ? (world.Travelling ? "arrival-door" : "settled") : world.Travelling ? "snapshot-or-navigation" : "home";
            if (phase != lastPhase) { stamp(phase); lastPhase = phase; }
            rows.Add(new Frame { game = Time.frameCount, profiler = ProfilerDriver.lastFrameIndex, deltaMs = Time.unscaledDeltaTime * 1000, phase = phase });
        };
        bool priorEnabled = ProfilerDriver.enabled, priorEditor = ProfilerDriver.profileEditor;
        bool cpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU), physics = ProfilerDriver.IsAreaEnabled(ProfilerArea.Physics);
        var areas = Enum.GetValues(typeof(ProfilerArea)).Cast<ProfilerArea>().ToDictionary(a => a, a => ProfilerDriver.IsAreaEnabled(a));
        try
        {
            ProfilerDriver.enabled = false;
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = true;
            if (lean) foreach (var area in areas.Keys) ProfilerDriver.SetAreaEnabled(area, area == ProfilerArea.CPU || area == ProfilerArea.Physics || area == ProfilerArea.GPU);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.Physics, true);
            ProfilerDriver.enabled = true;
            EditorApplication.update += tick;
            await Task.Delay(700);
            stamp("travel-request");
            if (!world.Travel(connection.source, world.player)) throw new Exception("Travel refused");
            await Until(() => !world.Travelling);
            if (world.CurrentLevel != market) throw new Exception("Travel did not reach the destination");
            if (market.GetComponentsInChildren<SonarObjectEmitter>().Any(e => e.isActiveAndEnabled && !e.AudioReady))
                throw new Exception("Arrival before audio warmup completed");
            stamp("travel-completed");
            await Task.Delay(4000);
            ProfilerDriver.enabled = false;
            EditorApplication.update -= tick;
            ProfilerDriver.SaveProfile(Dir + label + ".data");
            var result = new { label, held, lean, from = home.name, to = market.name, breaks,
                items = market.GetComponentsInChildren<PickupItem>().Length,
                bodies = market.GetComponentsInChildren<Rigidbody>().Length,
                collisionEmissions = market.GetComponentsInChildren<CollisionSonarEmitter>().Sum(e => e.Emissions),
                readyAudio = market.GetComponentsInChildren<SonarObjectEmitter>().Count(e => e.AudioReady),
                first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex, events, rows };
            File.WriteAllText(Dir + label + "-events.json", JsonConvert.SerializeObject(result, Formatting.Indented));
            return new { label, breaks, samples = rows.Count, first = result.first, last = result.last, events };
        }
        finally
        {
            EditorApplication.update -= tick;
            foreach (var item in items) if (item != null) item.Broken -= onBreak;
            ProfilerDriver.enabled = priorEnabled;
            ProfilerDriver.profileEditor = priorEditor;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, cpu);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.Physics, physics);
            foreach (var area in areas) ProfilerDriver.SetAreaEnabled(area.Key, area.Value);
        }
    }
    struct Frame { public int game, profiler; public float deltaMs; public string phase; }
}
