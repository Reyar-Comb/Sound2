using System;
using UnityEditor;
using UnityEngine;
using Sound2.LevelEditing;
using Object = UnityEngine.Object;

// Update the shared prefab so all independently authored levels inherit the controls.
public static class ConfigureElevatorDisplay
{
    const string Path = "Assets/Prefabs/LevelElevator.prefab";

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit mode required");
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var paper = AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Adventure/ElevatorPaper.mat");
            var ink = AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Adventure/ElevatorInk.mat");
            var floor = root.transform.Find("Floor");
            // Keep the flush support collider; only the level floor should be rendered.
            if (floor.TryGetComponent<MeshRenderer>(out var renderer)) Object.DestroyImmediate(renderer);
            if (floor.TryGetComponent<MeshFilter>(out var mesh)) Object.DestroyImmediate(mesh);
            var oldArrow = root.transform.Find("UP arrow");
            if (oldArrow != null) Object.DestroyImmediate(oldArrow.gameObject);
            Configure(root.transform.Find("Call button"), false, paper, ink);
            Configure(root.transform.Find("Travel button"), true, paper, ink);
            PrefabUtility.SaveAsPrefabAsset(root, Path);
            return new { floor = "Collider only", buttons = 2 };
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void Configure(Transform button, bool inside, Material paper, Material ink)
    {
        button.localPosition = inside ? new Vector3(1.94f, 1.35f, 1.15f) : new Vector3(1.7f, 1.35f, 3.02f);
        button.localRotation = Quaternion.Euler(0, inside ? -90 : 0, 0);
        button.localScale = new Vector3(.38f, .52f, .08f);
        button.GetComponent<Renderer>().sharedMaterial = ink;
        var old = button.Find("Control graphic");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var graphic = new GameObject("Control graphic").transform;
        graphic.SetParent(button, false);
        // Author the graphic in metres despite the scaled primitive plate.
        graphic.localScale = new Vector3(1 / .38f, 1 / .52f, 1 / .08f);
        graphic.localPosition = new Vector3(0, 0, .6f);
        button.GetComponent<LevelElevatorButton>().interactionAnchor = graphic;
        Stroke(graphic, paper, "Frame top", new Vector2(-.15f, .22f), new Vector2(.15f, .22f), .015f);
        Stroke(graphic, paper, "Frame bottom", new Vector2(-.15f, -.22f), new Vector2(.15f, -.22f), .015f);
        Stroke(graphic, paper, "Frame left", new Vector2(-.15f, -.22f), new Vector2(-.15f, .22f), .015f);
        Stroke(graphic, paper, "Frame right", new Vector2(.15f, -.22f), new Vector2(.15f, .22f), .015f);
        if (inside)
        {
            Stroke(graphic, paper, "Arrow stem", new Vector2(0, -.10f), new Vector2(0, .10f));
            Stroke(graphic, paper, "Arrow left", new Vector2(-.08f, .02f), new Vector2(0, .10f));
            Stroke(graphic, paper, "Arrow right", new Vector2(.08f, .02f), new Vector2(0, .10f));
        }
        else
        {
            // Door-opening pictogram: two chevrons pointing apart.
            Stroke(graphic, paper, "Open left upper", new Vector2(-.05f, .075f), new Vector2(-.115f, 0));
            Stroke(graphic, paper, "Open left lower", new Vector2(-.115f, 0), new Vector2(-.05f, -.075f));
            Stroke(graphic, paper, "Open right upper", new Vector2(.05f, .075f), new Vector2(.115f, 0));
            Stroke(graphic, paper, "Open right lower", new Vector2(.115f, 0), new Vector2(.05f, -.075f));
        }
    }

    static void Stroke(Transform parent, Material material, string name, Vector2 a, Vector2 b, float width = .028f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (a + b) * .5f;
        go.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        go.transform.localScale = new Vector3(Vector2.Distance(a, b) + width, width, .008f);
        go.GetComponent<Renderer>().sharedMaterial = material;
    }
}
