using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor;
using Sound2.LevelEditing;
using Newtonsoft.Json;

public static class VerifyHospitalGrounding
{
    static string Geometry(LevelLayout l)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        foreach(var f in l.generatedRoot.GetComponentsInChildren<MeshFilter>(true).OrderBy(f=>f.name))
        {
            w.Write(f.name);var m=f.sharedMesh;if(m==null){w.Write(-1);continue;}
            w.Write(m.vertexCount);foreach(var v in m.vertices){w.Write(v.x);w.Write(v.y);w.Write(v.z);}foreach(int i in m.triangles)w.Write(i);
        }
        return Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(stream.ToArray()));
    }
    public static object Run()
    {
        var current=SceneManager.GetSceneByPath("Assets/Levels/LevelHospital.unity");
        const string temporary="Assets/__HospitalGroundingVerification.unity";
        if(File.Exists(temporary))throw new Exception("Temporary path already exists");
        File.Copy("Logs/HospitalJourney/Backup/LevelHospital.unsaved-before.unity",temporary);
        AssetDatabase.ImportAsset(temporary);
        var before=EditorSceneManager.OpenScene(temporary,OpenSceneMode.Additive);
        try
        {
            LevelLayout Layout(Scene s)=>s.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>()).Single();
            var a=Layout(before);var b=Layout(current);var changes=new List<string>();
            var original=a.transform.Find("Props");var final=b.transform.Find("Props");
            if(original.childCount!=final.childCount)throw new Exception("Prop count changed");
            foreach(Transform x in original)
            {
                var y=final.Find(x.name);if(y==null)throw new Exception("Missing "+x.name);
                if(Mathf.Abs(x.localPosition.x-y.localPosition.x)>.00001f||Mathf.Abs(x.localPosition.z-y.localPosition.z)>.00001f
                    ||Quaternion.Angle(x.localRotation,y.localRotation)>.001f||Vector3.Distance(x.localScale,y.localScale)>.00001f)throw new Exception("Non-height change "+x.name);
                if(Mathf.Abs(x.localPosition.y-y.localPosition.y)>.00001f)changes.Add(x.name);
            }
            bool sameGeometry=Geometry(a)==Geometry(b);
            if(!sameGeometry)throw new Exception("Architectural geometry changed");
            var result=new{passed=true,props=final.childCount,heightChanges=changes.Count,sameGeometry,changes};
            File.WriteAllText("Logs/HospitalJourney/grounding-verification.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
        }
        finally{EditorSceneManager.CloseScene(before,true);AssetDatabase.DeleteAsset(temporary);SceneManager.SetActiveScene(current);}
    }
}
