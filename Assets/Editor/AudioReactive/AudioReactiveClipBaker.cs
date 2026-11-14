using System;
using UnityEditor;
using UnityEngine;

public static class AudioReactiveClipBaker
{
    // Maximum absolute sample over all channels per interval; opposite-phase stereo cannot cancel.
    public static void Bake(AudioReactiveClip asset)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Bake in Edit mode.");
        var clip = asset.clip;
        int requestedRate = asset.analysisRate;
        int rate = clip != null && clip.length > 0 ? Math.Min(requestedRate, Mathf.FloorToInt((AudioReactiveClip.TextureWidth - 1) / clip.length)) : requestedRate;
        if (clip == null || rate < 10 || requestedRate < 10 || requestedRate > 120)
            throw new ArgumentException("Select a clip, 10–120 Hz analysis rate (clips up to about 6.8 minutes).");
        int frames = Mathf.CeilToInt(clip.length * rate) + 1;
        if (frames > AudioReactiveClip.TextureWidth) throw new ArgumentException("Clip exceeds 4095 analysis intervals; lower analysis rate or split the clip.");
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        if (clip.loadState != AudioDataLoadState.Loaded) throw new InvalidOperationException("Audio is still loading. Wait and bake again.");
        var pcm = new float[clip.samples * clip.channels];
        if (!clip.GetData(pcm, 0)) throw new InvalidOperationException("GetData failed. Use Decompress On Load; importer settings were not changed.");
        var bytes = new byte[frames];
        for (int f = 0; f < frames - 1; f++)
        {
            int first = Mathf.Min(clip.samples, Mathf.FloorToInt((float)f * clip.frequency / rate));
            int last = Mathf.Min(clip.samples, Mathf.FloorToInt((float)(f + 1) * clip.frequency / rate));
            float peak = 0;
            for (int s = first * clip.channels; s < last * clip.channels; s++)
                peak = Mathf.Max(peak, Mathf.Abs(pcm[s]));
            bytes[f] = AudioReactiveClip.Quantize(peak);
        }
        string path = AssetDatabase.GetAssetPath(clip);
        asset.SetAnalysis(bytes, frames, string.IsNullOrEmpty(path) ? "" : AssetDatabase.GetAssetDependencyHash(path).ToString(), rate);
        EditorUtility.SetDirty(asset);
    }
}

[CustomEditor(typeof(AudioReactiveClip))]
public sealed class AudioReactiveClipEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); var asset = (AudioReactiveClip)target;
        bool stale = asset.clip != null && asset.SourceHash != AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(asset.clip)).ToString();
        EditorGUILayout.HelpBox(asset.IsReady && !stale ? $"Ready: {asset.FrameCount} frames at {asset.FrameRate} Hz, {asset.ByteCount:N0} bytes, peak only, no FFT." : "Bake or re-bake the selected dry clip. No audio importer is changed automatically.", asset.IsReady && !stale ? MessageType.Info : MessageType.Warning);
        using (new EditorGUI.DisabledScope(Application.isPlaying || asset.clip == null))
            if (GUILayout.Button("Bake peak envelope"))
                try { Undo.RecordObject(asset, "Bake audio analysis"); AudioReactiveClipBaker.Bake(asset); AssetDatabase.SaveAssets(); }
                catch (Exception ex) { Debug.LogError(ex.Message, asset); }
    }
}


[CustomEditor(typeof(AudioReactiveSonarEmitter))]
public sealed class AudioReactiveSonarEmitterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); var emitter = (AudioReactiveSonarEmitter)target;
        EditorGUILayout.HelpBox("Use a dedicated ImpactSoundPool on this object. Its Settings own HRTF, reverb and distance curves. Visual settings only affect the point cloud. Peak brightness = pow(clamp01((peak * Gain - Peak Floor) / (Peak Ceiling - Peak Floor)), Response Exponent). Gain / Floor / Ceiling / Exponent update live; other visual settings apply on the next trigger.", MessageType.Info);
        if (emitter.analysis == null || !emitter.analysis.IsReady)
            EditorGUILayout.HelpBox("Assign a baked Audio Reactive Clip before triggering.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || emitter.analysis == null || !emitter.analysis.IsReady))
        {
            if (GUILayout.Button("Play + emit audio wave")) emitter.Trigger();
            if (GUILayout.Button("Stop audio (emitted waves keep travelling)")) emitter.AudioPool.StopAll();
        }
    }
}
