using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

public static class VerifySonarWarmup
{
    public static async Task<object> Run()
    {
        if(!Application.isPlaying)throw new Exception("Requires Play Mode");
        var root=new GameObject("Audio warmup regression");root.SetActive(false);
        var field=Object.FindAnyObjectByType<SonarPulseField>();
        var player=Object.FindAnyObjectByType<PlayerController>();root.transform.position=player.transform.position;
        var checks=new List<string>();var sources=new List<SonarObjectEmitter>();
        void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
        async Task Until(Func<bool> condition){double end=EditorApplication.timeSinceStartup+10;while(!condition()){if(EditorApplication.timeSinceStartup>end)throw new Exception("Warmup timed out");await Task.Delay(10);}}
        SonarObjectEmitter Add(string name)
        {
            var go=new GameObject(name);go.transform.SetParent(root.transform,false);
            var s=go.AddComponent<SonarObjectEmitter>();s.automatic=false;s.toggleKey=UnityEngine.InputSystem.Key.None;s.pulseField=field;
            sources.Add(s);return s;
        }
        var clip=AudioClip.Create("Silent playback test",44100,1,44100,false);
        try
        {
            for(int i=0;i<16;i++)Add("Queued "+i);
            var immediate=Add("Immediate sound");
            var cancelled=Add("Cancelled");var destroyed=Add("Destroyed");
            root.SetActive(true);
            Check(sources.All(s=>!s.AudioReady),"Enabling a batch does not eagerly create voices");
            Check(immediate.EmitWithClip(4,clip,.25f)&&immediate.AudioReady&&immediate.Emissions==1,"Actual emission initializes immediately and is not lost");
            Check(immediate.GetComponent<AudioSource>().isPlaying,"Immediate emission starts audio playback");
            SonarAudioWarmup.Schedule(cancelled);cancelled.gameObject.SetActive(false);
            SonarAudioWarmup.Schedule(destroyed);Object.Destroy(destroyed.gameObject);
            await Until(()=>sources.Take(16).All(s=>s.AudioReady));
            Check(!cancelled.AudioReady,"Disabled pending sources are skipped");
            Check(sources.Take(16).All(s=>s.Emissions==0),"Prewarming does not emit a sound or sonar pulse");
            Check(sources.Take(16).All(s=>s.GetComponents<SteamAudio.SteamAudioSource>().Length==1&&s.GetComponents<SpatialAudioEmitter>().Length==1),"Exactly one spatial voice is created per source");
            cancelled.gameObject.SetActive(true);await Until(()=>cancelled.AudioReady);
            Check(cancelled.AudioReady,"Re-enabled source resumes warmup");
            var voice=immediate.GetComponent<SpatialAudioEmitter>();
            Check(voice.settings.hrtf&&!voice.settings.reflections&&voice.settings.distanceMode==SoundDistanceMode.Linear
                &&voice.settings.minDistance==1&&Mathf.Approximately(voice.settings.maxDistance,immediate.soundRange*4/immediate.range),"Emission spatialization and range scaling are unchanged");
            int voices=immediate.GetComponents<SteamAudio.SteamAudioSource>().Length;
            immediate.gameObject.SetActive(false);immediate.gameObject.SetActive(true);await Task.Delay(150);
            Check(immediate.AudioReady&&immediate.GetComponents<SteamAudio.SteamAudioSource>().Length==voices,"Ready voice is reused after reactivation");
            var result=new{passed=true,checks};File.WriteAllText("Logs/CollisionOptimization/audio-checks.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
        }
        finally{Object.Destroy(root);Object.Destroy(clip);}
    }
}
