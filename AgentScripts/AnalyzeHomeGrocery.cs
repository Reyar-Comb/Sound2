using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditorInternal;
using Newtonsoft.Json;

// Inspect the capture already loaded with diagnostics profiler_session.
public static class AnalyzeHomeGrocery
{
    public static object Run(string label)
    {
        var frames = new List<object>();
        for (int f = Math.Max(1, ProfilerDriver.firstFrameIndex); f <= ProfilerDriver.lastFrameIndex; f++)
        {
            using (var raw = ProfilerDriver.GetRawFrameDataView(f, 0))
            {
                if (!raw.valid) continue;
                var values = new Dictionary<string, double>();
                var calls = new Dictionary<string, int>();
                for (int s = 1; s < raw.sampleCount; s++)
                {
                    var name = raw.GetSampleName(s);
                    string key = name == "PlayerLoop" ? "playerLoop" :
                        name == "FixedUpdate.PhysicsFixedUpdate" ? "physics" :
                        name == "Physics.Simulate" ? "simulate" :
                        name == "AsyncInstantiateOperation.IntegrateInMainThread" ? "integrate" :
                        name == "InstantiateMultiple.Awake" ? "cloneAwake" :
                        name.Contains("LevelWorld.CaptureInitialLevelAsync()") ? "snapshotContinuation" :
                        name.Contains("LevelWorld.Transport()") ? "transport" :
                        name.Contains("SonarObjectEmitter.Start()") ? "sonarStart" :
                        name.Contains("SteamAudioSource.Awake()") ? "steamAwake" :
                        name == "Sonar.AudioWarmup" ? "audioWarmup" :
                        name.Contains("RuntimePatrolNavigation.BuildAsync()") ? "navigation" :
                        name == "Profiler.FlushMemoryCounters" ? "profilerMemory" :
                        name == "HierarchyView.RefreshItems" ? "hierarchy" :
                        name.Contains("GC.Collect") ? "gcCollect" : null;
                    if (key == null) continue;
                    if (!values.ContainsKey(key)) { values[key] = 0; calls[key] = 0; }
                    values[key] += raw.GetSampleTimeNs(s) / 1000000.0;
                    calls[key]++;
                }
                frames.Add(new { frame = f, cpuMs = raw.frameTimeMs, values, calls });
            }
        }
        var path = "Logs/HomeGroceryProfile/" + label + "-metrics.json";
        File.WriteAllText(path, JsonConvert.SerializeObject(frames));
        return new { path, count = frames.Count };
    }
}
