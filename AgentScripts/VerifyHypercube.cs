using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class VerifyHypercube
{
    public static object Main()
    {
        var cube=Object.FindAnyObjectByType<HypercubePointCloud>();
        if(cube==null) throw new Exception("Missing hypercube.");
        var start=new Vector3[16]; var middle=new Vector3[16]; var end=new Vector3[16];
        cube.EvaluateVertices(0,start); cube.EvaluateVertices(cube.loopSeconds*.23f,middle); cube.EvaluateVertices(cube.loopSeconds,end);
        float seam=start.Select((p,i)=>Vector3.Distance(p,end[i])).Max();
        float movement=start.Select((p,i)=>Vector3.Distance(p,middle[i])).Max();
        if(seam>.00001f || movement<.25f) throw new Exception("Invalid rotation loop.");
        var bounds=new Bounds(start[0],Vector3.zero);
        for(int step=0;step<=320;step++)
        {
            cube.EvaluateVertices(step*cube.loopSeconds/320,middle);
            foreach(var p in middle)
            {
                if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)||!cube.WorldBounds.Contains(p))
                    throw new Exception("Invalid projection bounds.");
                bounds.Encapsulate(p);
            }
        }
        if(bounds.min.y<.01f||bounds.max.y>3.19f) throw new Exception("Hypercube crosses the floor or ceiling.");
        var cloud=new Vector4[HypercubePointCloud.PointCount]; int points=cube.Collect(cloud);
        if(points!=656 || points>MonsterVines.Capacity) throw new Exception("Invalid draw count.");
        if(cube.GetComponent<Animator>()!=null||cube.GetComponent<MonsterVines>()!=null)
            throw new Exception("Unexpected old behaviour on the replacement.");
        foreach(var go in cube.gameObject.scene.GetRootGameObjects())
            foreach(var t in go.GetComponentsInChildren<Transform>(true))
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)
                    throw new Exception("Missing script: "+t.name);
        var material=new Material(Shader.Find("Sound2/Screen Grid Face And Corner Points"));
        bool passes=material.GetPassName(10)=="ClearVineDots"&&material.GetPassName(11)=="SurfaceVineDots";
        Object.DestroyImmediate(material); if(!passes) throw new Exception("Missing original point passes.");
        return new {passed=true,vertices=16,edges=32,points,period=cube.loopSeconds,loopSeam=seam,
            movement,minY=bounds.min.y,maxY=bounds.max.y,registered=HypercubePointCloud.Instances.Contains(cube)};
    }
}
