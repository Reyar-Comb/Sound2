using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Newtonsoft.Json;

public static class GroundHospitalProps
{
    const string Dir = "Logs/HospitalJourney/";
    public static object Run(bool apply)
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required");
        var scene = SceneManager.GetSceneByPath("Assets/Levels/LevelHospital.unity");
        var layout = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LevelLayout>(true)).Single();
        var props = layout.transform.Find("Props").Cast<Transform>().ToArray();
        var floor = layout.generatedRoot.GetComponentsInChildren<Collider>().Where(c => c.name == "Floor").ToArray();
        if (floor.Length == 0 || Vector3.Dot(layout.transform.up, Vector3.up) < .9999f) throw new Exception("Expected horizontal hospital floor");
        Physics.SyncTransforms();
        var rows = new List<object>(); var moved = new List<string>();
        int group = Undo.GetCurrentGroup();
        if (apply) { Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("医院物品吸附地面"); }
        foreach (var t in props)
        {
            var prop = t.GetComponent<LevelProp>();
            if (prop == null || prop.surface != PropSurface.Floor || t.GetComponent<LevelElevator>() != null) continue;
            var colliders = t.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
            bool mobile = t.GetComponent<PushableProp>() != null;
            bool area = t.name.StartsWith("RedVineArea", StringComparison.Ordinal);
            float bottom = colliders.Select(c => c.bounds.min.y).DefaultIfEmpty(t.TransformPoint(prop.surfaceAnchor).y).Min();
            // Keep the visible lowest point above the floor too, including authored tilted carts.
            if (mobile)
                foreach (var filter in t.GetComponentsInChildren<MeshFilter>())
                    if (filter.sharedMesh != null && filter.sharedMesh.isReadable)
                        foreach (var vertex in filter.sharedMesh.vertices) bottom = Mathf.Min(bottom, filter.transform.TransformPoint(vertex).y);
            var origin = new Vector3(t.position.x, Math.Max(t.position.y, bottom) + .5f, t.position.z);
            float support = float.NegativeInfinity; string supportName = null;
            IEnumerable<Collider> surfaces = floor;
            // Mobile furniture and floor hazards belong on the architectural floor;
            // tabletop monitors and small items retain their authored supporting furniture.
            if (!mobile && !area) surfaces = surfaces.Concat(props.Where(p => p != t).SelectMany(p => p.GetComponentsInChildren<Collider>()));
            foreach (var c in surfaces)
                if (c.enabled && !c.isTrigger && c.Raycast(new Ray(origin, Vector3.down), out var hit, 100)
                    && hit.normal.y > .9f && hit.point.y > support) { support = hit.point.y; supportName = c.name; }
            if (float.IsNegativeInfinity(support)) { rows.Add(new { t.name, status = "no supporting floor; untouched" }); continue; }
            float gap = bottom - support;
            bool change = mobile || area ? gap > .03f || gap < -.005f : gap > .03f;
            float delta = change ? support + Mathf.Max(.005f, prop.surfaceOffset) - bottom : 0;
            var before = t.position;
            if (apply && change)
            {
                Undo.RecordObject(t, "医院物品吸附地面"); t.position += Vector3.up * delta;
                PrefabUtility.RecordPrefabInstancePropertyModifications(t); moved.Add(t.name);
            }
            rows.Add(new { t.name, mobile, supportName, support, bottom, gap, delta, change,
                before = new { before.x, before.y, before.z }, afterY = before.y + delta });
        }
        if (apply && moved.Count > 0)
        {
            Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene);
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save hospital");
        }
        Directory.CreateDirectory(Dir);
        var path = Dir + (apply ? "grounding-applied" : "grounding-audit") + ".json";
        File.WriteAllText(path, JsonConvert.SerializeObject(rows, Formatting.Indented));
        return new { path, props = props.Length, moved };
    }
}
