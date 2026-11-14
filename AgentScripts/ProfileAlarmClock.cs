using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

public static class ProfileAlarmClock
{
    public static string Inspect() => JsonConvert.SerializeObject(new {
        scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
        playing=Application.isPlaying, menu=SonicSpaceStartMenu.BlocksGameplay,
        alarms=Object.FindObjectsByType<PickupAlarm>().Select(a=>new {
            name=a.name, held=a.GetComponent<PickupItem>().IsHeld,
            armed=a.Armed, ringing=a.IsRinging, rings=a.Rings, delay=a.delayAfterPickup,
            interval=a.ringInterval, range=a.ringRange,
            position=a.transform.position.ToString(),
            reflections=a.GetComponent<SpatialAudioEmitter>().settings.reflections,
            clip=a.alarmClip!=null?a.alarmClip.name:null }).ToArray(),
        player=Object.FindAnyObjectByType<PlayerPickup>()?.transform.position.ToString(),
        resolution=new[]{Screen.width,Screen.height}, vsync=QualitySettings.vSyncCount,
        targetFps=Application.targetFrameRate
    });

    public static string Hold()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(SonicSpaceStartMenu.BlocksGameplay)menu.BeginGame();
        var picker=Object.FindAnyObjectByType<PlayerPickup>();
        var alarm=Object.FindObjectsByType<PickupAlarm>().FirstOrDefault(a=>a.name=="PickupAlarmClock (1)")
            ?? Object.FindObjectsByType<PickupAlarm>().FirstOrDefault();
        if(alarm==null)throw new Exception("No test scene alarm found");
        alarm.delayAfterPickup=600;
        if(picker.HeldItem==null)
        {
            var item=alarm.GetComponent<PickupItem>();
            // Use the actual pickup path after moving the existing prop into pickup reach in Play mode only.
            var body=item.GetComponent<Rigidbody>();
            body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
            body.position=picker.viewTransform.position+picker.viewTransform.forward*.65f;
            item.transform.position=body.position;Physics.SyncTransforms();
            if(!picker.TryPickUp(item))throw new Exception("Actual pickup failed");
        }
        Object.FindAnyObjectByType<SonarPulseField>().ClearPulses();
        return Inspect();
    }

    public static string Sampling(bool batch)
    {
        foreach(var field in Object.FindObjectsByType<SonarPulseField>())field.batchRaycasts=batch;
        return "batchRaycasts="+batch;
    }

    public static string Phase(string phase)
    {
        var alarm=Object.FindAnyObjectByType<PlayerPickup>().HeldItem.GetComponent<PickupAlarm>();
        var emitter=alarm.GetComponent<SonarObjectEmitter>();
        var field=Object.FindAnyObjectByType<SonarPulseField>();
        var features=Resources.FindObjectsOfTypeAll<BlackBoxPointCloudFeature>();
        foreach(var feature in features)feature.SetActive(phase!="render-off");
        emitter.playSound=phase!="audio-off";
        if(phase=="alarm-only" || phase=="render-off") foreach(var clock in Object.FindObjectsByType<TickingClock>()) clock.running=false;
        if(phase=="baseline") { alarm.enabled=false; alarm.GetComponent<TickingClock>().running=false; }
        else {
            alarm.enabled=true;
            typeof(PickupAlarm).GetProperty("Remaining").GetSetMethod(true).Invoke(alarm,new object[]{0f});
        }
        if(phase=="audio-off")alarm.GetComponent<AudioSource>().Stop();
        field.ClearPulses();
        return Inspect();
    }
}




