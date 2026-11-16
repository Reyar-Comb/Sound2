using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

public static class FixGroceryPerformance
{
    static string Kind(PickupItem item) => Regex.Replace(item.name, @" \(\d+\)$", "");
    public static object Apply()
    {
        if (Application.isPlaying) throw new Exception("Stop Play Mode first");
        var report = new List<object>();
        foreach (string path in new[]{"Assets/Scenes/SoundAndShell.unity", "Assets/Levels/LevelGrocery.unity"})
        {
            var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (!opened && scene.isDirty)
                {
                    Directory.CreateDirectory("Logs/GroceryFix");
                    string backup="Logs/GroceryFix/"+scene.name+"-before-cleanup.unity";
                    if(!File.Exists(backup) && !EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Cannot preserve current scene edits");
                }
                var levels = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelLayout>(true)).ToArray();
                var level = levels.FirstOrDefault(l => l.name.Contains("超市")) ?? levels.Single();
                var items = level.GetComponentsInChildren<PickupItem>(true);
                var groups = items.GroupBy(x => (Kind(x), x.transform.localPosition, x.transform.localRotation, x.transform.localScale, x.transform.parent))
                    .Where(g => g.Count() > 1).ToArray();
                var replacements = new Dictionary<Object, Object>(); var removed = new List<string>();
                foreach (var group in groups)
                {
                    var keep = group.First();
                    foreach (var duplicate in group.Skip(1))
                    {
                        var a = keep.GetComponentsInChildren<Transform>(true); var b = duplicate.GetComponentsInChildren<Transform>(true);
                        if (a.Length != b.Length || keep.brokenPrefab != duplicate.brokenPrefab) throw new Exception("Nonidentical duplicate " + duplicate.name);
                        for (int i=0;i<a.Length;i++)
                        {
                            if (i>0 && (a[i].name!=b[i].name || a[i].localPosition!=b[i].localPosition || a[i].localRotation!=b[i].localRotation || a[i].localScale!=b[i].localScale)) throw new Exception("Different child geometry " + duplicate.name);
                            var ac=a[i].GetComponents<Component>(); var bc=b[i].GetComponents<Component>();
                            if(ac.Length!=bc.Length)throw new Exception("Different components " + duplicate.name);
                            replacements[b[i].gameObject]=a[i].gameObject;
                            for(int c=0;c<ac.Length;c++) { if(ac[c].GetType()!=bc[c].GetType())throw new Exception("Different component type"); replacements[bc[c]]=ac[c]; }
                        }
                        removed.Add(duplicate.name + " -> " + keep.name);
                    }
                }
                // Preserve any explicit references to a duplicate by pointing them at its survivor.
                foreach(var component in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Component>(true)))
                {
                    if(component==null || replacements.ContainsKey(component))continue;
                    var serialized=new SerializedObject(component);var property=serialized.GetIterator();bool changed=false;
                    while(property.Next(true))
                        if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue!=null && replacements.TryGetValue(property.objectReferenceValue,out var replacement))
                        {property.objectReferenceValue=replacement;changed=true;}
                    if(changed)serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach(var group in groups)foreach(var duplicate in group.Skip(1))Object.DestroyImmediate(duplicate.gameObject);
                if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed " + path);
                report.Add(new {path,before=items.Length,removed,after=level.GetComponentsInChildren<PickupItem>(true).Length});
            }
            finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
        }
        const string brokenPath="Assets/Prefabs/WineGlassBroken.prefab";
        var broken=PrefabUtility.LoadPrefabContents(brokenPath);
        try { WineGlassFragmentBudget.Apply(broken);PrefabUtility.SaveAsPrefabAsset(broken,brokenPath); }
        finally { PrefabUtility.UnloadPrefabContents(broken); }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs/GroceryFix");
        File.WriteAllText("Logs/GroceryFix/migration.json",JsonConvert.SerializeObject(report,Formatting.Indented));
        return report.Select(x=>x).ToArray();
    }
    public static object CacheTables()
    {
        var report=new List<object>();
        foreach(string path in new[]{"Assets/Scenes/SoundAndShell.unity","Assets/Levels/LevelWedding.unity"})
        {
            var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                foreach(var level in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>(true)))
                {
                    var tables=level.GetComponentsInChildren<DrapedRingTable>(true);
                    if(tables.Length==0)continue;
                    RingTableMeshPersistence.Persist(level);
                    report.Add(new {path,level=level.name,count=tables.Length});
                }
                if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed " + path);
            }
            finally {if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        AssetDatabase.SaveAssets();return report;
    }
}
