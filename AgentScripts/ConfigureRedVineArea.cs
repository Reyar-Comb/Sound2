using System;
using System.IO;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ConfigureRedVineArea
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
        const string path = "Assets/Prefabs/RedVineArea.prefab", folder = "Assets/Models/EffectProps";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return path;
        Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        string materialPath = folder + "/RedVinePreview.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "RedVinePreview" };
            material.SetColor("_BaseColor", new Color(.8f, .055f, .035f)); AssetDatabase.CreateAsset(material, materialPath);
        }
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("RedVineArea"); SceneManager.MoveGameObjectToScene(root, scene);
            var area = root.AddComponent<MonsterVineArea>(); var vines = root.GetComponent<MonsterVines>();
            vines.growthInterval = .32f; vines.lifetime = 3.6f; vines.reach = 2.8f;
            var prop = root.AddComponent<LevelProp>(); prop.surface = PropSurface.Floor; prop.surfaceAnchor = Vector3.zero; prop.surfaceOffset = .01f;
            var preview = new GameObject("Editor preview (hidden in Play)"); preview.transform.SetParent(root.transform, false);
            Transform Bar(string name, Vector3 a, Vector3 b, float thickness)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name; part.transform.SetParent(preview.transform, false);
                Object.DestroyImmediate(part.GetComponent<Collider>()); part.GetComponent<Renderer>().sharedMaterial = material;
                part.transform.localPosition = (a + b) * .5f; part.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                part.transform.localScale = new Vector3(thickness, Vector3.Distance(a, b), thickness); return part.transform;
            }
            var edges = new Transform[12]; for (int i = 0; i < edges.Length; i++) edges[i] = Bar("Range edge " + i, Vector3.zero, Vector3.up, .018f);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Bar("Vine symbol stem", new Vector3(0, .04f, -.7f), new Vector3(0, .04f, .7f), .025f);
                Bar("Vine symbol branch", new Vector3(0, .04f, -.3f), new Vector3(sign * .4f, .04f, .1f), .025f);
                Bar("Vine symbol tip", new Vector3(sign * .4f, .04f, .1f), new Vector3(sign * .7f, .04f, .1f), .025f);
                Bar("Vine symbol upper", new Vector3(0, .04f, .25f), new Vector3(sign * .35f, .04f, .6f), .025f);
            }
            area.ConfigurePreview(preview.transform, edges);
            PrefabUtility.SaveAsPrefabAsset(root, path); AssetDatabase.SaveAssets(); return path;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
