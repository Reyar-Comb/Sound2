using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Newtonsoft.Json;

public static class AuditHospitalContacts
{
    public static object Run()
    {
        if (Application.isPlaying) throw new Exception("Inspect authored positions in Edit Mode");
        var scene = SceneManager.GetSceneByPath("Assets/Levels/LevelHospital.unity");
        var layout = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LevelLayout>()).Single();
        Physics.SyncTransforms();
        var props = layout.GetComponentsInChildren<PushableProp>();
        var shapes = props.Select(p => p.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray()).ToArray();
        var bounds = shapes.Select(cs => { var b = cs[0].bounds; foreach(var c in cs) b.Encapsulate(c.bounds); return b; }).ToArray();
        var pairs = new List<object>(); int exact = 0;
        for (int i = 0; i < props.Length; i++)
            for (int j = i+1; j < props.Length; j++)
            {
                if (!bounds[i].Intersects(bounds[j])) continue;
                float depth = 0; int contacts = 0;
                foreach (var a in shapes[i]) foreach (var b in shapes[j])
                    if (a.bounds.Intersects(b.bounds) && Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out float d) && d > .001f)
                    { contacts++; depth = Mathf.Max(depth,d); }
                if (contacts == 0) continue;
                var x=props[i].transform;var y=props[j].transform;
                bool duplicate = Vector3.Distance(x.position,y.position)<.001f && Quaternion.Angle(x.rotation,y.rotation)<.1f
                    && Vector3.Distance(x.lossyScale,y.lossyScale)<.001f
                    && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(x.gameObject)==PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(y.gameObject);
                if(duplicate)exact++;
                pairs.Add(new { a=props[i].name,b=props[j].name,contacts,depth,duplicate });
            }
        var result=new{props=props.Length,pairCount=pairs.Count,exactDuplicatePairs=exact,pairs};
        File.WriteAllText("Logs/HospitalJourney/authored-contacts.json",JsonConvert.SerializeObject(result,Formatting.Indented));
        return new{result.props,result.pairCount,result.exactDuplicatePairs};
    }
}
