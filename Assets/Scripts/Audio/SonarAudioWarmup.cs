using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Prepares passive sonar voices across frames without delaying an actual sound request.</summary>
[DefaultExecutionOrder(10000)]
public sealed class SonarAudioWarmup : MonoBehaviour
{
    // Two voices normally fit in this budget. A tighter budget serializes hundreds
    // of native allocations one per frame and unnecessarily prolongs elevator travel.
    const double BudgetSeconds = .006;
    const int MaxSourcesPerFrame = 2;
    static readonly ProfilerMarker marker = new("Sonar.AudioWarmup");
    static readonly Queue<SonarObjectEmitter> queue = new();
    static readonly HashSet<SonarObjectEmitter> pending = new();
    static SonarAudioWarmup instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        queue.Clear();
        pending.Clear();
        instance = null;
    }

    public static void Schedule(SonarObjectEmitter source)
    {
        if (!Application.isPlaying || source == null || source.AudioReady || !source.isActiveAndEnabled || !pending.Add(source)) return;
        if (instance == null)
        {
            var root = new GameObject("Sonar audio warmup") { hideFlags = HideFlags.HideInHierarchy };
            instance = root.AddComponent<SonarAudioWarmup>();
            DontDestroyOnLoad(root);
        }
        queue.Enqueue(source);
    }

    public static void Cancel(SonarObjectEmitter source) => pending.Remove(source);

    void Update()
    {
        if (queue.Count == 0) return;
        using (marker.Auto())
        {
            double start = Time.realtimeSinceStartupAsDouble;
            int prepared = 0, examined = 0;
            while (queue.Count > 0 && examined++ < 32)
            {
                var source = queue.Dequeue();
                if (!pending.Remove(source) || source == null || !source.isActiveAndEnabled || source.AudioReady) continue;
                source.PrepareAudio();
                // An individual native Steam Audio source creation cannot be interrupted.
                if (++prepared >= MaxSourcesPerFrame || Time.realtimeSinceStartupAsDouble - start >= BudgetSeconds) break;
            }
        }
    }

    void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        queue.Clear();
        pending.Clear();
    }
}
