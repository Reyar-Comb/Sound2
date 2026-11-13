using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using SteamAudio;
using Vector3 = UnityEngine.Vector3;
using Material = UnityEngine.Material;

public static class CreateMazeRoom
{
    const int Size = 6;
    const float Cell = 10f / Size;
    const float Height = 3.2f;
    static bool[,] east = new bool[Size, Size];
    static bool[,] north = new bool[Size, Size];

    public static string Main()
    {
        const string path = "Assets/Scenes/MazeRoom.unity";
        var scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/CubeRoom.unity")
            throw new InvalidOperationException("Open CubeRoom in Edit mode first.");
        if (System.IO.File.Exists(path)) throw new InvalidOperationException("MazeRoom already exists.");
        // Preserve the user's current sound settings in both the original and the new scene.
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save original scene.");
        if (!EditorSceneManager.SaveScene(scene, path, true)) throw new InvalidOperationException("Could not copy scene.");
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        GenerateLayout();

        var root = GameObject.Find("Cube Room (10m Interior)");
        root.name = "Maze Room (10m x 10m)";
        foreach (Transform part in root.transform)
        {
            if (part.name == "Ceiling") part.position = new Vector3(0, Height + 0.15f, 0);
            else if (part.name.StartsWith("Wall"))
            {
                var p = part.position; p.y = Height / 2; part.position = p;
                var scale = part.localScale; scale.y = Height; part.localScale = scale;
            }
        }
        const string materials = "Assets/Materials/MazeRoom";
        if (!AssetDatabase.IsValidFolder(materials)) AssetDatabase.CreateFolder("Assets/Materials", "MazeRoom");
        var wall = MakeMaterial(materials + "/Partitions.mat", new Color(0.39f, 0.49f, 0.53f));
        var start = MakeMaterial(materials + "/Start.mat", new Color(0.12f, 0.65f, 0.42f));
        var goal = MakeMaterial(materials + "/Goal.mat", new Color(0.95f, 0.58f, 0.12f));
        int partitions = 0;
        for (int z = 0; z < Size; z++)
        for (int x = 0; x < Size; x++)
        {
            if (x < Size - 1 && !east[x,z])
            {
                Cube("Partition E " + x + "," + z,
                    new Vector3(-5 + (x + 1) * Cell, Height / 2, Center(z)),
                    new Vector3(0.18f, Height, Cell + 0.18f), wall, root.transform);
                partitions++;
            }
            if (z < Size - 1 && !north[x,z])
            {
                Cube("Partition N " + x + "," + z,
                    new Vector3(Center(x), Height / 2, -5 + (z + 1) * Cell),
                    new Vector3(Cell + 0.18f, Height, 0.18f), wall, root.transform);
                partitions++;
            }
        }
        var markers = new GameObject("Navigation Markers");
        var a = Cube("Start Tile", new Vector3(Center(0), 0.006f, Center(0)), new Vector3(0.8f,0.01f,0.8f), start, markers.transform);
        var b = Cube("Goal Tile", new Vector3(Center(Size-1),0.006f,Center(Size-1)), new Vector3(0.8f,0.01f,0.8f), goal, markers.transform);
        UnityEngine.Object.DestroyImmediate(a.GetComponent<Collider>());
        UnityEngine.Object.DestroyImmediate(b.GetComponent<Collider>());

        var camera = Camera.main;
        camera.transform.position = new Vector3(Center(0),1.6f,Center(0));
        camera.transform.rotation = Quaternion.Euler(0, east[0,0] ? 90 : 0, 0);
        camera.gameObject.layer = 2; // Ignore Raycast: do not let the launcher hit its own capsule.
        var walker = camera.gameObject.AddComponent<CharacterController>();
        walker.height = 1.6f;
        walker.radius = 0.22f;
        walker.center = new Vector3(0,-0.7f,0);
        walker.skinWidth = 0.025f;
        walker.stepOffset = 0.1f;
        walker.minMoveDistance = 0;
        var playtest = camera.GetComponent<BouncePlaytest>();
        playtest.simulateOcclusion = true;
        EditorUtility.SetDirty(playtest);

        var lighting = GameObject.Find("Room Lighting");
        foreach (var old in lighting.GetComponentsInChildren<Light>())
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        for (int z = 0; z < 2; z++)
        for (int x = 0; x < 2; x++)
        {
            var lamp = new GameObject("Ceiling Light " + x + "," + z, typeof(Light));
            lamp.transform.SetParent(lighting.transform);
            lamp.transform.position = new Vector3(x == 0 ? -2.5f : 2.5f,2.8f,z == 0 ? -2.5f : 2.5f);
            var light = lamp.GetComponent<Light>();
            light.type = LightType.Point; light.range = 8; light.intensity = 1.4f;
            light.color = new Color(1,0.96f,0.89f); light.shadows = LightShadows.None;
        }

        const string acousticPath = "Assets/Audio/RoomAcoustics/MazeRoomGeometry.asset";
        var data = ScriptableObject.CreateInstance<SerializedData>();
        AssetDatabase.CreateAsset(data, acousticPath);
        var mesh = UnityEngine.Object.FindAnyObjectByType<SteamAudioStaticMesh>();
        mesh.asset = data;
        mesh.sceneNameWhenExported = scene.name;
        SteamAudioManager.ExportScene(scene, false);
        EditorUtility.SetDirty(mesh); EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Physics.SyncTransforms();
        int reachable = ValidatePhysicalPaths();
        if (reachable != Size * Size) throw new InvalidOperationException("Physical paths are disconnected: " + reachable);
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(0,1.6f,0),Quaternion.Euler(65,0,0),10);
        return "Saved " + path + "; " + reachable + " connected cells; " + partitions
            + " partitions; two extra loops; height " + Height + "m; exported geometry " + data.data.Length
            + " bytes; preserved reverb mix " + playtest.reverbLevel;
    }

    static float Center(int i) { return -5 + (i + 0.5f) * Cell; }

    static void GenerateLayout()
    {
        east = new bool[Size,Size]; north = new bool[Size,Size];
        var visited = new bool[Size,Size];
        var stack = new Stack<Vector2Int>();
        var random = new System.Random(20261113);
        stack.Push(Vector2Int.zero); visited[0,0] = true;
        var directions = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        while (stack.Count > 0)
        {
            var cell = stack.Peek();
            var candidates = new List<Vector2Int>();
            foreach (var delta in directions)
            {
                var next = cell + delta;
                if (Inside(next) && !visited[next.x,next.y]) candidates.Add(next);
            }
            if (candidates.Count == 0) { stack.Pop(); continue; }
            var chosen = candidates[random.Next(candidates.Count)];
            Open(cell,chosen); visited[chosen.x,chosen.y] = true; stack.Push(chosen);
        }
        int loops = 0;
        while (loops < 2)
        {
            int x = random.Next(Size), z = random.Next(Size);
            bool horizontal = random.Next(2) == 0;
            if (horizontal && x < Size-1 && !east[x,z]) { east[x,z] = true; loops++; }
            else if (!horizontal && z < Size-1 && !north[x,z]) { north[x,z] = true; loops++; }
        }
    }

    static bool Inside(Vector2Int p) { return p.x >= 0 && p.x < Size && p.y >= 0 && p.y < Size; }
    static void Open(Vector2Int a, Vector2Int b)
    {
        if (a.x != b.x) east[Math.Min(a.x,b.x),a.y] = true;
        else north[a.x,Math.Min(a.y,b.y)] = true;
    }

    static int ValidatePhysicalPaths()
    {
        var visited = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(Vector2Int.zero); visited.Add(Vector2Int.zero);
        var directions = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var origin = new Vector3(Center(p.x),1.2f,Center(p.y));
            foreach (var direction in directions)
            {
                var next = p + direction;
                if (!Inside(next) || visited.Contains(next)) continue;
                var delta = new Vector3(direction.x,0,direction.y);
                if (Physics.SphereCast(origin,0.24f,delta,out _,Cell)) continue;
                visited.Add(next); queue.Enqueue(next);
            }
        }
        return visited.Count;
    }

    static Material MakeMaterial(string path, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor",color); material.SetFloat("_Smoothness",0.1f);
        AssetDatabase.CreateAsset(material,path); return material;
    }
    static GameObject Cube(string name, Vector3 position, Vector3 size, Material material, Transform parent)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name; obj.transform.SetParent(parent); obj.transform.position = position;
        obj.transform.localScale = size; obj.GetComponent<Renderer>().sharedMaterial = material;
        obj.isStatic = true; return obj;
    }
}
