using System;
using System.Collections.Generic;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyPropOverlap
{
    public static object Run()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode first.");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Overlap checks");
        SceneManager.MoveGameObjectToScene(root, scene);
        var layout = root.AddComponent<LevelLayout>();
        var props = new GameObject("Props"); props.transform.SetParent(root.transform, false);
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        GameObject Box(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = position;
            go.AddComponent<BoxCollider>(); return go;
        }
        try
        {
            var source = Box("Source", root.transform, Vector3.left * 10);
            var obstacle = Box("Obstacle", props.transform, Vector3.zero);
            bool Over(Vector3 p, Quaternion? r = null) => LevelPropOverlap.HasOverlap(layout, source, new Pose(p, r ?? Quaternion.identity));
            Check(Over(Vector3.zero), "Coincident boxes rejected");
            Check(Over(Vector3.right * .9f), "Partial overlap rejected");
            Check(!Over(Vector3.right), "Touching faces allowed");
            Check(!Over(Vector3.right * 1.01f), "Separated boxes allowed");
            Check(!Over(Vector3.up), "Tabletop contact allowed");
            obstacle.GetComponent<BoxCollider>().isTrigger = true;
            Check(!Over(Vector3.zero), "Trigger ignored");
            obstacle.GetComponent<BoxCollider>().isTrigger = false;
            obstacle.GetComponent<BoxCollider>().enabled = false;
            Check(!Over(Vector3.zero), "Disabled collider ignored");
            obstacle.GetComponent<BoxCollider>().enabled = true;
            obstacle.SetActive(false); Check(!Over(Vector3.zero), "Inactive object ignored"); obstacle.SetActive(true);
            source.transform.SetParent(props.transform, true);
            Check(!Over(source.transform.position), "Reposition excludes itself");
            var original = source.transform.position;
            bool rejected = false;
            try { LevelPropPlacement.Reposition(layout, source, new Pose(Vector3.zero, Quaternion.identity)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && source.transform.position == original, "Rejected reposition preserves original transform");
            source.transform.localScale = new Vector3(2, 1, 1);
            Check(Over(Vector3.right * 1.4f), "Scaled collision respected");
            source.transform.localScale = Vector3.one;
            source.GetComponent<BoxCollider>().size = new Vector3(2, 1, .1f);
            obstacle.GetComponent<BoxCollider>().size = new Vector3(2, 1, .1f);
            obstacle.transform.rotation = Quaternion.Euler(0, 45, 0);
            Check(!Over(new Vector3(.2f, 0, .2f), obstacle.transform.rotation), "Rotated thin boxes with overlapping AABBs remain valid");
            Object.DestroyImmediate(source); Object.DestroyImmediate(obstacle);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(cube);
            source = new GameObject("Mesh source"); source.transform.SetParent(props.transform, false);
            source.transform.position = Vector3.left * 10;
            source.AddComponent<MeshCollider>().sharedMesh = mesh;
            obstacle = new GameObject("Mesh obstacle"); obstacle.transform.SetParent(props.transform, false);
            obstacle.AddComponent<MeshCollider>().sharedMesh = mesh;
            Check(Over(Vector3.zero), "Coincident non-convex meshes rejected");
            Check(Over(Vector3.right * .75f), "Intersecting non-convex meshes rejected");
            Check(!Over(Vector3.up), "Non-convex mesh surface contact allowed");
            source.transform.localScale = Vector3.one * .2f;
            Check(Over(Vector3.zero), "Contained non-convex mesh rejected");
            var compound = new Mesh();
            try
            {
                compound.CombineMeshes(new[] {
                    new CombineInstance { mesh = mesh, transform = Matrix4x4.Translate(Vector3.left) },
                    new CombineInstance { mesh = mesh, transform = Matrix4x4.Translate(Vector3.right) }
                });
                obstacle.GetComponent<MeshCollider>().sharedMesh = compound;
                Check(!Over(Vector3.zero), "Baked compound mesh retains the gap between its parts");
                Check(Over(Vector3.right), "Baked compound solid part still blocks placement");
                compound.UploadMeshData(true);
                Check(!Over(Vector3.zero) && Over(Vector3.right), "Unreadable imported mesh data supported without changing import settings");
            }
            finally { Object.DestroyImmediate(compound); }
            Object.DestroyImmediate(source); Object.DestroyImmediate(obstacle);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Chair.prefab");
            var first = LevelPropPlacement.Place(layout, prefab, new Pose(Vector3.zero, Quaternion.identity));
            int count = props.transform.childCount;
            rejected = false;
            try { LevelPropPlacement.Place(layout, prefab, new Pose(Vector3.zero, Quaternion.identity)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && props.transform.childCount == count, "Duplicate prefab placement rejected without creating an instance");
            Check(!LevelPropOverlap.HasOverlap(layout, first, new Pose(first.transform.position, first.transform.rotation)), "Compound prop ignores its own child colliders");
            return new { passed = true, checks };
        }
        finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
    }
}

return VerifyPropOverlap.Run();
