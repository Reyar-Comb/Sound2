using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Sound2.LevelEditing;
using Object=UnityEngine.Object;

public static class VerifyLevelAudioReset
{
    public static async Task<object> Run()
    {
        var world=Object.FindAnyObjectByType<LevelWorld>();var previous=world.CurrentLevel;
        if(!previous.name.Contains("超市")||world.Travelling)throw new Exception("Requires settled grocery level");
        var lamp=previous.GetComponentsInChildren<PickupItem>().First(i=>i.name.StartsWith("PickupDeskLamp"));
        string removed=lamp.name;Object.Destroy(lamp.gameObject);await Task.Delay(100);
        world.StartCoroutine(world.ResetCurrentLevel());double end=EditorApplication.timeSinceStartup+90;
        while(world.CurrentLevel==previous||world.CurrentLevel.IsPreparing||!world.CurrentLevel.GetComponent<RuntimePatrolNavigation>().Ready)
        {if(EditorApplication.timeSinceStartup>end)throw new Exception("Reset timed out");await Task.Delay(20);}
        var level=world.CurrentLevel;int items=level.GetComponentsInChildren<PickupItem>().Length;
        int ready=level.GetComponentsInChildren<SonarObjectEmitter>().Count(s=>s.AudioReady);
        if(items!=309||ready!=309||!level.GetComponentsInChildren<PickupItem>().Any(i=>i.name==removed)
            ||world.ArrivalCheckpoint.Level!=level)throw new Exception("Reset did not restore state/audio/checkpoint");
        var result=new{passed=true,items,readyAudio=ready,restored=removed,arrivalRemapped=true};
        File.WriteAllText("Logs/CollisionOptimization/reset-checks.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
    }
}
