using System;
using System.Collections;
using UnityEngine;
public static class VerifyObjectSonar
{
    public static object Main()
    {
        var e=UnityEngine.Object.FindAnyObjectByType<SonarObjectEmitter>();
        var s=e.sonar;
        e.automatic=false;s.automatic=false;s.ClearPulses();
        int before=e.Emissions;
        e.EmitNow();
        var p=Shader.GetGlobalVectorArray("_SonarOrigins");
        var settings=Shader.GetGlobalVectorArray("_SonarParameters");
        int slot=Array.FindIndex(settings,v=>v.w>.5f);
        var origin=new Vector3(p[slot].x,p[slot].y,p[slot].z);
        bool correctOrigin=Vector3.Distance(origin,e.transform.position)<.001f;
        var cameraPosition=s.transform.position;
        var controller=s.GetComponent<CharacterController>();
        controller.enabled=false;s.transform.position+=Vector3.right;
        var afterMove=Shader.GetGlobalVectorArray("_SonarOrigins");
        bool fixedOrigin=afterMove[slot]==p[slot];
        s.transform.position=cameraPosition;controller.enabled=true;
        bool playerStillWorks=s.Emit();
        var audio=e.GetComponent<AudioSource>();
        bool spatial=audio.spatialBlend==1&&audio.spatialize;
        bool passed=correctOrigin&&fixedOrigin&&playerStillWorks&&spatial&&e.Emissions==before+1;
        if(!passed)throw new Exception("Object sonar integration verification failed.");
        s.StartCoroutine(CheckAudio(e));
        return new {passed,correctOrigin,fixedOrigin,playerStillWorks,spatial,
            cube=origin.ToString(),player=cameraPosition.ToString()};
    }
    static IEnumerator CheckAudio(SonarObjectEmitter emitter)
    {
        // Isolate the object cue from the player's cue before recording listener output.
        yield return new WaitForSeconds(.3f);
        emitter.EmitNow();
        float peakRms=0;
        float end=Time.time+.3f;
        var data=new float[512];
        while(Time.time<end)
        {
            yield return null;
            AudioListener.GetOutputData(data,0);
            float sum=0;foreach(float sample in data)sum+=sample*sample;
            peakRms=Mathf.Max(peakRms,Mathf.Sqrt(sum/data.Length));
        }
        System.IO.File.WriteAllText("/tmp/Sound2-ObjectSonar-Audio.txt","listenerPeakRms="+peakRms+"; audibleSignal="+(peakRms>.00001f));
    }
}
