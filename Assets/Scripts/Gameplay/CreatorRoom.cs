using System.Collections.Generic;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Builds isolated stations from the editor-synced catalog when the room starts.</summary>
[RequireComponent(typeof(Camera)), DisallowMultipleComponent]
public sealed class CreatorRoom : MonoBehaviour
{
    [SerializeField] private CreatorRoomCatalog catalog;
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material standMaterial;
    [SerializeField] private Material pointMaterial;
    [SerializeField] private GameObject radioPrefab;
    [SerializeField] private AudioReactiveClip radioAnalysis;
    [SerializeField] private bool startInPointCloud;

    private const int StationsPerRow = 6;
    private const float FrontClearance = 4.5f;
    private const float BackClearance = .65f;
    private const float WallThickness = .18f;
    private const float WallHeight = 3f;
    private const float StandHeight = .95f;

    private struct Surface
    {
        public Renderer renderer;
        public Material[] solid;
        public Material[] points;
    }

    private struct Station
    {
        public GameObject prop;
        public CreatorRoomCatalog.Entry entry;
        public Bounds bounds;
        public LevelProp metadata;
        public float width;
    }

    private readonly List<Surface> surfaces = new();
    private readonly Dictionary<Transform, string> labels = new();
    private Transform room;
    private Camera view;
    private PlayerInteraction interaction;
    private GUIStyle hudLabel;
    private string roomTitle;
    private string focusedLabel;
    private Rect panel;
    private bool resetting;
    private float nextLabelUpdate;

    public bool PointCloud { get; private set; }
    public int StationCount { get; private set; }

    private void Awake()
    {
        if (catalog == null)
        {
            Debug.LogError("Assign the Creator Room catalog before opening the room.", this);
            enabled = false;
            return;
        }
        view = GetComponent<Camera>();
        interaction = GetComponentInParent<PlayerInteraction>();
        var proximity = GetComponentInParent<BlackBoxProximitySource>();
        if (proximity != null) proximity.SetRevealModes(true, true);
        BuildRoom();
        SetPointCloud(startInPointCloud);
    }

    private void BuildRoom()
    {
        var root = new GameObject("Creator Room Stations");
        SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        room = root.transform;
        root.SetActive(false);

        var stations = new List<Station>();
        foreach (var entry in catalog.entries)
        {
            if (entry.prefab == null && entry.mesh == null) continue;
            var prop = entry.prefab != null ? Instantiate(entry.prefab, room) : CreateMeshModel(entry.mesh);
            prop.name = entry.prefab != null ? entry.prefab.name : entry.mesh.name;
            var metadata = prop.GetComponent<LevelProp>();
            bool wallMounted = IsWallMounted(prop, metadata);
            prop.transform.SetPositionAndRotation(Vector3.zero, wallMounted ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
            Bounds bounds = FindBounds(prop);
            if (entry.modelOnly && prop.GetComponentInChildren<Collider>(true) == null)
            {
                var collider = prop.AddComponent<BoxCollider>();
                collider.center = prop.transform.InverseTransformPoint(bounds.center);
                var scale = prop.transform.lossyScale;
                collider.size = new Vector3(
                    Mathf.Max(.02f, bounds.size.x / Mathf.Max(.001f, Mathf.Abs(scale.x))),
                    Mathf.Max(.02f, bounds.size.y / Mathf.Max(.001f, Mathf.Abs(scale.y))),
                    Mathf.Max(.02f, bounds.size.z / Mathf.Max(.001f, Mathf.Abs(scale.z))));
            }
            stations.Add(new Station { prop = prop, entry = entry, bounds = bounds,
                metadata = metadata, width = Mathf.Max(2.55f, bounds.size.x + 1.65f) });
        }

        float roomWidth = 8;
        float rowDepth = 0;
        for (int row = 0; row < stations.Count; row += StationsPerRow)
        {
            int end = Mathf.Min(row + StationsPerRow, stations.Count);
            float rowWidth = 0;
            float largestDepth = .85f;
            for (int i = row; i < end; i++)
            {
                rowWidth += stations[i].width;
                largestDepth = Mathf.Max(largestDepth, stations[i].bounds.size.z);
            }

            float wallZ = rowDepth + FrontClearance + largestDepth + BackClearance;
            float x = -.5f * rowWidth;
            for (int i = row; i < end; i++)
            {
                PlaceStation(stations[i], x + .5f * stations[i].width, wallZ, largestDepth);
                x += stations[i].width;
            }
            rowDepth = wallZ + WallThickness;
            roomWidth = Mathf.Max(roomWidth, rowWidth + 4);
        }

        float back = rowDepth + 2, front = -4, span = back - front;
        float center = (front + back) * .5f;
        float height = 4;
        foreach (var station in stations) height = Mathf.Max(height, station.bounds.size.y + 2);
        Block("Floor", new Vector3(0, -.15f, center), new Vector3(roomWidth, .3f, span), floorMaterial);
        Block("Ceiling", new Vector3(0, height + .15f, center), new Vector3(roomWidth, .3f, span), wallMaterial);
        Block("Entrance wall", new Vector3(0, height * .5f, front), new Vector3(roomWidth, height, WallThickness), wallMaterial);
        Block("Far wall", new Vector3(0, height * .5f, back), new Vector3(roomWidth, height, WallThickness), wallMaterial);
        Block("Side wall L", new Vector3(-roomWidth * .5f, height * .5f, center), new Vector3(WallThickness, height, span), wallMaterial);
        Block("Side wall R", new Vector3(roomWidth * .5f, height * .5f, center), new Vector3(WallThickness, height, span), wallMaterial);
        view.farClipPlane = Mathf.Max(view.farClipPlane, span + roomWidth);
        StationCount = stations.Count;
        roomTitle = $"造物主房间  ·  {StationCount} 件";
        root.SetActive(true);
        Physics.SyncTransforms();
    }

    private GameObject CreateMeshModel(Mesh mesh)
    {
        var prop = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
        prop.transform.SetParent(room, false);
        prop.GetComponent<MeshFilter>().sharedMesh = mesh;
        var materials = new Material[mesh.subMeshCount];
        for (int i = 0; i < materials.Length; i++) materials[i] = standMaterial;
        prop.GetComponent<MeshRenderer>().sharedMaterials = materials;
        return prop;
    }

    private static bool IsWallMounted(GameObject prop, LevelProp metadata) =>
        metadata != null && metadata.surface == PropSurface.Wall && prop.GetComponent<EmbeddedDoor>() == null;

    private static Bounds FindBounds(GameObject prop)
    {
        if (prop.TryGetComponent<MonsterVineArea>(out var area)) return area.WorldBounds;
        var renderers = prop.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(prop.transform.position, Vector3.one);
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private void PlaceStation(Station station, float x, float wallZ, float rowModelDepth)
    {
        var prop = station.prop;
        var bounds = station.bounds;
        string label = station.entry.modelOnly ? station.entry.label + "  ·  模型" : station.entry.label;
        string id = "Station - " + prop.name;
        var stationRoot = new GameObject(id).transform;
        stationRoot.SetParent(room, false);
        labels.Add(stationRoot, label);
        prop.transform.SetParent(stationRoot, false);

        Block("Impact wall", new Vector3(x, WallHeight * .5f, wallZ + WallThickness * .5f),
            new Vector3(station.width - 1.25f, WallHeight, WallThickness), wallMaterial, stationRoot);
        if (IsWallMounted(prop, station.metadata))
        {
            Vector3 anchor = prop.transform.TransformVector(station.metadata.surfaceAnchor);
            float y = Mathf.Max(1.45f, anchor.y - bounds.min.y + .08f);
            prop.transform.position = new Vector3(x - bounds.center.x, y - anchor.y,
                wallZ - anchor.z - Mathf.Max(.01f, station.metadata.surfaceOffset));
        }
        else
        {
            bool onStand = bounds.size.y <= .75f && bounds.size.x <= 1.2f && bounds.size.z <= .95f;
            float z = wallZ - BackClearance - rowModelDepth + bounds.extents.z + .45f;
            if (onStand)
            {
                Block("Test stand", new Vector3(x, StandHeight * .5f, z),
                    new Vector3(Mathf.Max(.85f, bounds.size.x + .25f), StandHeight, Mathf.Max(.7f, bounds.size.z + .25f)),
                    standMaterial, stationRoot);
            }
            prop.transform.position = new Vector3(x - bounds.center.x, (onStand ? StandHeight : 0) - bounds.min.y + .012f, z - bounds.center.z);
        }

        if (prop.TryGetComponent<PickupItem>(out var item)) item.RemainsSpawned += RegisterModel;
        if (station.entry.prefab == radioPrefab) ConfigureRadio(prop);
        RegisterModel(prop);
    }

    private void ConfigureRadio(GameObject prop)
    {
        if (radioAnalysis == null || prop.GetComponentInChildren<AudioReactiveSonarEmitter>(true) != null
            || prop.GetComponentInChildren<PropAudio>(true) != null || prop.GetComponentInChildren<AudioSource>(true) != null
            || !prop.TryGetComponent<InteractableObject>(out var action) || action.onInteract.GetPersistentEventCount() > 0) return;
        var audio = prop.AddComponent<AudioReactiveSonarEmitter>();
        audio.analysis = radioAnalysis;
        audio.loop = true;
        audio.AudioPool.capacity = 1;
        audio.AudioPool.settings.reflections = false;
        audio.AudioPool.settings.distanceMode = SoundDistanceMode.Linear;
        audio.AudioPool.settings.maxDistance = audio.visual.range;
        action.interactionHint = "开关收音机";
        action.onInteract.AddListener(_ => audio.Toggle());
    }

    private void Block(string name, Vector3 position, Vector3 scale, Material material, Transform parent = null)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent != null ? parent : room, false);
        block.transform.SetPositionAndRotation(position, Quaternion.identity);
        block.transform.localScale = scale;
        var renderer = block.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        RegisterSurface(renderer);
    }

    private void RegisterModel(GameObject prop)
    {
        // Disable the level's automatic black material swap on this instance only.
        foreach (var metadata in prop.GetComponentsInChildren<LevelProp>(true)) metadata.enabled = false;
        foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true)) RegisterSurface(renderer);
    }

    private void RegisterSurface(Renderer renderer)
    {
        var solid = renderer.sharedMaterials;
        var points = new Material[solid.Length];
        for (int i = 0; i < points.Length; i++) points[i] = pointMaterial;
        surfaces.Add(new Surface { renderer = renderer, solid = solid, points = points });
        if (PointCloud) renderer.sharedMaterials = points;
    }

    public void SetPointCloud(bool enabled)
    {
        PointCloud = enabled;
        view.backgroundColor = enabled ? Color.black : new Color(.12f, .14f, .16f);
        surfaces.RemoveAll(surface => surface.renderer == null);
        foreach (var surface in surfaces)
            surface.renderer.sharedMaterials = enabled ? surface.points : surface.solid;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && !resetting)
        {
            if (keyboard.rKey.wasPressedThisFrame) ResetRoom();
            else if (keyboard.vKey.wasPressedThisFrame) SetPointCloud(!PointCloud);
        }

        if (Time.unscaledTime >= nextLabelUpdate)
        {
            nextLabelUpdate = Time.unscaledTime + .1f;
            var target = interaction != null ? interaction.FocusedTarget : null;
            focusedLabel = target != null ? FindLabel(target.transform) : null;
            if (focusedLabel == null && Physics.Raycast(view.transform.position, view.transform.forward, out var hit, 10,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) focusedLabel = FindLabel(hit.collider.transform);
        }
        panel = new Rect(16, 16, 330, 87);
        PlayerUiPointMask.SetLegacyPanel(this, panel, true);
    }

    private string FindLabel(Transform target)
    {
        for (Transform current = target; current != null && current != room; current = current.parent)
            if (labels.TryGetValue(current, out string label)) return label;
        return null;
    }

    public void ResetRoom()
    {
        if (resetting) return;
        resetting = true;
        var scene = gameObject.scene;
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadSceneAsync(scene.buildIndex, LoadSceneMode.Single);
#endif
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (hudLabel == null)
        {
            hudLabel = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            if (GameUiTheme.Current != null) hudLabel.font = GameUiTheme.Current.legacyFont;
        }
        GUI.Box(panel, GUIContent.none);
        GUI.Label(new Rect(26, 21, 315, 22), roomTitle, hudLabel);
        GUI.Label(new Rect(26, 46, 315, 22), PointCloud ? "R 重置   V 切换为实体渲染" : "R 重置   V 切换为点云渲染", hudLabel);
        GUI.Label(new Rect(26, 70, 315, 22), focusedLabel ?? "左键交互、拾取、投掷   右键放下", hudLabel);
    }

    private void OnDisable() => PlayerUiPointMask.SetLegacyPanel(this, default, false);
}
