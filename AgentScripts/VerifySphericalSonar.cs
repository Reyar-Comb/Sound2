using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
public static class VerifySphericalSonar
{
    public static object Main()
    {
        var s=UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        s.automatic=false;s.ClearPulses();
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name="Temporary sonar blocking test";
        blocker.transform.position=s.transform.position+Vector3.right*2;
        blocker.transform.localScale=new Vector3(.25f,2,2);
        var origin=s.transform.position;
        try
        {
            int before=UnityEngine.Object.FindObjectsByType<MeshRenderer>().Length;
            if(!s.Emit()) throw new Exception("Wait until the emission cooldown ends.");
            int after=UnityEngine.Object.FindObjectsByType<MeshRenderer>().Length;
            var parameters=Shader.GetGlobalVectorArray("_SonarParameters");
            var origins=Shader.GetGlobalVectorArray("_SonarOrigins");
            int slot=Array.FindIndex(parameters,v=>v.w>.5f);
            var texture=(Texture2DArray)Shader.GetGlobalTexture("_SonarBlockingPlanes");
            var plane=texture.GetPixels(slot*6)[24*48+24];
            var normal=new Vector3(plane.r,plane.g,plane.b);
            bool frontAllowed=Vector3.Dot(normal,Vector3.right*1.875f)>=plane.a-.06f;
            bool behindBlocked=Vector3.Dot(normal,Vector3.right*4)<plane.a-.06f;
            s.transform.position+=Vector3.forward*.3f;
            s.GetComponent<BlackBoxProximitySource>().PublishPosition();
            bool originFixed=origins.SequenceEqual(Shader.GetGlobalVectorArray("_SonarOrigins"));
            s.transform.position=origin;
            var style=s.GetComponent<BlackBoxProximitySource>();
            bool stylesCorrect=style.IsStyleEnabled(BlackBoxProximitySource.PointStyle.Face)==s.facePoints
                &&style.IsStyleEnabled(BlackBoxProximitySource.PointStyle.Edge)==s.edgePoints
                &&!style.IsStyleEnabled(BlackBoxProximitySource.PointStyle.Stars);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var feature=renderer.rendererFeatures.OfType<BlackBoxPointCloudFeature>().Single();
            bool originalRenderer=feature.isActive&&feature.settings.shader.name=="Sound2/Screen Grid Face And Corner Points";
            bool noShaderErrors=ShaderUtil.GetShaderMessages(feature.settings.shader).Length==0;
            bool passed=before==after&&frontAllowed&&behindBlocked&&originFixed&&stylesCorrect&&originalRenderer&&noShaderErrors;
            if(!passed)throw new Exception("Sonar integration verification failed.");
            return new {passed,noGeneratedRenderers=before==after,frontAllowed,behindBlocked,originFixed,
                stylesCorrect,originalRenderer,noShaderErrors,s.LastSamplingMilliseconds};
        }
        finally
        {
            s.transform.position=origin;
            UnityEngine.Object.Destroy(blocker);s.ClearPulses();
        }
    }
}
