using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Object = UnityEngine.Object;

public static class VerifyLevelEditor
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        checks++;
    }

    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Run in edit mode.");
        checks = 0;
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        const string path = "Assets/Scenes/LevelEditorDemo.unity";
        if (File.Exists(path)) { EditorSceneManager.CloseScene(scene, true); throw new Exception("Demo exists; use VerifySaved to recheck without overwriting."); }
        try
        {
            SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Editable Level", typeof(LevelLayout)).GetComponent<LevelLayout>();
            // Separate verification physics from existing loaded scenes without changing user objects.
            layout.transform.position = new Vector3(5000, 0, 5000);
            var a = new LevelRoom { name = "起点 · 矩形房间", center = Vector2.zero, size = new Vector2(10, 10) };
            var b = new LevelRoom { name = "圆柱大厅", shape = RoomShape.Circle, center = new Vector2(22, 0), size = Vector2.one * 12 };
            var c = new LevelRoom { name = "终点 · 旋转房间", center = new Vector2(10, 22), size = new Vector2(10, 8), rotation = 15 };
            layout.rooms.AddRange(new[] { a, b, c });
            var straight = new LevelCorridor { name = "直线连接 · 3m", width = 3, points = new List<Vector2> { new Vector2(5, 0), new Vector2(16, 0) } };
            straight.start = new RoomConnection { roomId = a.id, edge = .125f };
            straight.end = new RoomConnection { roomId = b.id, edge = .5f };
            var curved = new LevelCorridor { name = "曲线连接 · 4m", curved = true, width = 4, points = new List<Vector2> { new Vector2(0, 5), new Vector2(0, 10), new Vector2(7, 12), new Vector2(10, 18) } };
            curved.start = new RoomConnection { roomId = a.id, edge = .375f };
            curved.end = new RoomConnection { roomId = c.id, edge = .875f };
            layout.corridors.AddRange(new[] { straight, curved });
            layout.pits.Add(new LevelPit { name = "房间下落坑", center = new Vector2(-2, 0), size = 2, depth = 8 });
            layout.pits.Add(new LevelPit { name = "走廊下落坑", center = new Vector2(12, 0), size = 1.6f, depth = 6, rotation = 10 });
            LevelBuilder.Rebuild(layout);
            ValidatePhysics(layout);

            var oldEndpoint = LevelGeometry.Endpoint(layout, straight, false);
            a.center += Vector2.left;
            Check(Vector2.Distance(LevelGeometry.Endpoint(layout, straight, false), oldEndpoint + Vector2.left) < .001f, "Connection follows room translation");
            a.center -= Vector2.left;
            double area = LevelGeometry.Area(LevelGeometry.Footprint(layout));
            b.size = Vector2.one * 14;
            Check(LevelGeometry.Area(LevelGeometry.Footprint(layout)) > area, "Circle resizing changes footprint");
            b.size = Vector2.one * 12;
            var oldMesh = layout.generatedRoot;
            layout.pits.Add(new LevelPit { center = new Vector2(200, 200) });
            bool rejected = false;
            try { LevelBuilder.Rebuild(layout); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && layout.generatedRoot == oldMesh, "Invalid hole preserves previous mesh");
            layout.pits.RemoveAt(layout.pits.Count - 1);

            Undo.RecordObject(layout, "Test level undo");
            layout.wallHeight = 4.2f;
            Undo.FlushUndoRecordObjects();
            LevelBuilder.Rebuild(layout);
            Undo.PerformUndo();
            Check(Mathf.Abs(layout.wallHeight - 3.2f) < .001f, "Undo restores layout data");
            LevelBuilder.Rebuild(layout);
            Check(layout.transform.Cast<Transform>().Count(t => t.name == "Generated Geometry") == 1, "Undo rebuild leaves exactly one geometry root");
            Undo.PerformRedo(); LevelBuilder.Rebuild(layout);
            Check(Mathf.Abs(layout.wallHeight - 4.2f) < .001f, "Redo restores layout data");
            layout.wallHeight = 3.2f;
            layout.transform.position = Vector3.zero;
            LevelBuilder.Rebuild(layout);
            LevelBuilder.AddLighting();
            var player = LevelBuilder.AddPlayer(layout);
            Check(player.transform.position.y > 1, "Player capsule spawns above floor");
            Check(new SerializedObject(player.GetComponentInChildren<PlayerController>()).FindProperty("useGravity").boolValue, "Demo player has gravity enabled");
            LevelBuilder.Persist(layout, path);
            Check(EditorSceneManager.SaveScene(scene, path), "Scene save succeeds");
            foreach (var filter in layout.generatedRoot.GetComponentsInChildren<MeshFilter>()) Check(AssetDatabase.Contains(filter.sharedMesh), "Mesh is a persistent asset");
            EditorSceneManager.CloseScene(scene, true);
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            layout = scene.GetRootGameObjects().Select(g => g.GetComponent<LevelLayout>()).First(l => l != null);
            Check(layout.rooms.Count == 3 && layout.corridors.Count == 2 && layout.pits.Count == 2, "Editable data survives scene reload");
            layout.transform.position = new Vector3(5000, 0, 5000);
            ValidatePhysics(layout);
            layout.transform.position = Vector3.zero;
            LevelBuilder.Rebuild(layout);
            Check(EditorSceneManager.SaveScene(scene), "Manual save hook persists regenerated meshes");
            string exportPath = "Assets/Scenes/LevelEditorExportCheck.unity";
            Check(!File.Exists(exportPath), "Export test destination is unused");
            LevelBuilder.Export(layout, exportPath, false);
            var exported = EditorSceneManager.OpenScene(exportPath, OpenSceneMode.Additive);
            var exportedLayout = exported.GetRootGameObjects().Select(g => g.GetComponent<LevelLayout>()).First(l => l != null);
            Check(exportedLayout.geometryAsset != layout.geometryAsset, "Export uses independent mesh assets");
            Check(exportedLayout.rooms.Count == layout.rooms.Count, "Export retains editable room data");
            EditorSceneManager.CloseScene(exported, true);
            AssetDatabase.DeleteAsset(exportPath);
            AssetDatabase.DeleteAsset("Assets/Scenes/LevelEditorExportCheck_Geometry");
            var window = EditorWindow.GetWindow<LevelDesignerWindow>("关卡编辑器");
            window.position = new Rect(50, 80, 460, 850);
            window.Bind(layout);
            SceneManager.SetActiveScene(scene);
            return checks + " checks passed. Saved " + path + ". Demo is open additively; original scene preserved: " + original.path;
        }
        catch
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            throw;
        }
    }

    public static string VerifySaved()
    {
        checks = 0;
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/LevelEditorDemo.unity", OpenSceneMode.Additive);
        try
        {
            var layout = scene.GetRootGameObjects().Select(g => g.GetComponent<LevelLayout>()).First(l => l != null && l.rooms.Count > 0);
            AssetDatabase.ImportAsset(layout.geometryAsset, ImportAssetOptions.ForceUpdate);
            var expected = LevelGeometry.Build(layout);
            try
            {
                foreach (var filter in layout.generatedRoot.GetComponentsInChildren<MeshFilter>())
                    Check(filter.sharedMesh.vertices.SequenceEqual(expected[filter.name].vertices), "Disk mesh matches saved layout: " + filter.name);
            }
            finally { foreach (var mesh in expected.Values) Object.DestroyImmediate(mesh); }
            layout.transform.position = new Vector3(5000, 0, 5000);
            ValidatePhysics(layout);
            return checks + " saved-scene physics checks passed.";
        }
        finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
    }

    static void ValidatePhysics(LevelLayout layout)
    {
        Physics.SyncTransforms();
        var colliders = layout.generatedRoot.GetComponentsInChildren<MeshCollider>();
        bool Hit(Vector2 point, float height, Vector3 dir, float distance)
        {
            var ray = new Ray(layout.World(point, height), layout.transform.TransformDirection(dir));
            return colliders.Any(c => c.Raycast(ray, out _, distance));
        }
        foreach (var room in layout.rooms)
            Check(Hit(room.center, 2, Vector3.down, 3), "Room center has floor: " + room.name);
        foreach (var pit in layout.pits)
        {
            Check(!Hit(pit.center, 2, Vector3.down, pit.depth + 10), "Pit has no floor or bottom cap: " + pit.name);
            float a = pit.rotation * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Check(Hit(pit.center, -pit.depth * .5f, dir, pit.size), "Shaft inner wall collides: " + pit.name);
            var body = new GameObject("Fall probe", typeof(CharacterController));
            try
            {
                var cc = body.GetComponent<CharacterController>(); cc.height = 1.6f; cc.radius = .3f; cc.skinWidth = .02f;
                body.transform.position = layout.World(pit.center, 1.2f);
                Physics.SyncTransforms();
                for (int i = 0; i < 120; i++) cc.Move(Vector3.down * .1f);
                Check(layout.transform.InverseTransformPoint(body.transform.position).y < -pit.depth - .5f, "CharacterController falls through entire shaft");
            }
            finally { Object.DestroyImmediate(body); }
        }
        foreach (var corridor in layout.corridors)
        foreach (bool end in new[] { false, true })
        {
            var link = end ? corridor.end : corridor.start;
            var room = layout.FindRoom(link.roomId);
            var door = room.Edge(link.edge, out Vector2 normal);
            var lateral = new Vector2(-normal.y, normal.x);
            foreach (float offset in new[] { -.35f, 0, .35f })
            {
                var start = door - normal * 1.2f + lateral * corridor.width * offset;
                Check(!Hit(start, 1, new Vector3(normal.x, 0, normal.y), 2.4f), "Door opening has no blocking wall");
            }
            Check(Hit(door, 1, Vector3.down, 2), "Door threshold has continuous floor");
        }
        var curve = LevelGeometry.CorridorPath(layout, layout.corridors[1]);
        for (int i = 1; i < curve.Count; i += 5)
            Check(Hit(curve[i], 2, Vector3.down, 3), "Curve floor continuous at sample " + i);
        Check(Hit(new Vector2(-4, -4), 1, Vector3.left, 2), "Outer wall collides from interior");
        Check(Hit(new Vector2(-6, -4), 1, Vector3.right, 2), "Outer wall collides from exterior");
    }
}
