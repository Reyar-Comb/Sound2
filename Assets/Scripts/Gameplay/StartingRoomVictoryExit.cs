using Sound2.LevelEditing;
using UnityEngine;

/// <summary>Replaces the first elevator after victory; crossing the ordinary door shows The World.</summary>
[DisallowMultipleComponent]
public sealed class StartingRoomVictoryExit : MonoBehaviour
{
    public LevelElevator elevator;
    public GameObject exitRoot;
    public DoorInteraction door;
    [Tooltip("门框本地坐标下的通行宽度和高度。房间内在门的 +Z 一侧。")]
    public Vector2 doorwaySize = new(1.1f, 2.25f);
    [Min(.5f), Tooltip("先穿过门洞，再向门外走这么远才开始演出，单位为门框本地米。")]
    public float exitInset = 2.5f;
    [Header("四结局衣柜与戒指")]
    public GameObject wardrobePrefab;
    public GameObject ringPrefab;
    public Vector3 wardrobePosition = new(-4.45f, 0, 1.5f);
    public float wardrobeYaw = -90;
    [Tooltip("蓝色大衣本地坐标中的戒指取出位置。")]
    public Vector3 ringPocketLocalPosition = new(.18f, .42f, -.145f);
    [Min(0)] public float ringLiftSeconds = .8f;
    [Min(0)] public float ringInspectSeconds = 2.5f;
    [Min(0)] public float ringStoreSeconds = .8f;
    [Header("四结局外围白色遮挡")]
    [Min(10)] public float whiteBoundaryDistance = 12;
    [Min(30)] public float whiteBoundarySize = 400;
    FourthEndingWardrobe wardrobe;
    bool exteriorBuilt;

    SoundShellOpening opening;
    Vector3 previousPosition;
    bool sampled, crossedDoor, ambienceStarted;

    void OnEnable()
    {
        StoryRunProgress.Current.EndingProgressChanged += RefreshPresentation;
        RefreshPresentation();
    }

    void OnDisable()
    {
        StoryRunProgress.Current.EndingProgressChanged -= RefreshPresentation;
        sampled = crossedDoor = ambienceStarted = false;
    }

    void RefreshPresentation()
    {
        bool won = StoryRunProgress.Current.DefeatedBoss;
        if (elevator != null) elevator.gameObject.SetActive(!won);
        if (exitRoot != null) exitRoot.SetActive(won);
        if (won) BuildFinalRoom();
        if (door != null) door.RuntimeLocked = won && (wardrobe == null || !wardrobe.RingStored);
        sampled = crossedDoor = ambienceStarted = false;
    }

    void Update()
    {
        if (!StoryRunProgress.Current.DefeatedBoss || door == null || !door.isActiveAndEnabled) return;
        door.RuntimeLocked = wardrobe == null || !wardrobe.RingStored;
        if (opening == null)
        {
            var level = GetComponentInParent<LevelLayout>();
            foreach (var candidate in FindObjectsByType<SoundShellOpening>())
                if (candidate.startingRoom == level) { opening = candidate; break; }
        }
        if (opening == null || !opening.InStartingRoom || opening.CurrentPhase != SoundShellOpening.Phase.Room
            || opening.player == null || opening.player.InputSuspended || opening.player.TransportLocked
            || SonicSpaceStartMenu.BlocksGameplay)
        { sampled = false; return; }

        if (!ambienceStarted && door.TargetOpen)
        {
            opening.roomMusic.BeginEndingAmbience();
            ambienceStarted = true;
        }

        Vector3 position = door.transform.InverseTransformPoint(opening.player.transform.position);
        // Test the segment at the threshold so frame rate cannot skip the doorway.
        if (sampled && previousPosition.z >= 0 && position.z < 0
            && Mathf.Abs(door.CurrentAngle) > 5)
        {
            float t = Mathf.InverseLerp(previousPosition.z, position.z, 0);
            Vector3 crossing = Vector3.Lerp(previousPosition, position, t);
            if (Mathf.Abs(crossing.x) <= doorwaySize.x * .5f && crossing.y >= 0 && crossing.y <= doorwaySize.y)
                crossedDoor = true;
        }
        if (position.z >= 0) crossedDoor = false;
        if (crossedDoor && position.z <= -Mathf.Max(.5f, exitInset))
        {
            // Keep the doorway and room behind the camera throughout the gradual white cover.
            opening.player.FaceDirection(-door.transform.forward);
            if (opening.TryUseVictoryExit(opening.player)) crossedDoor = false;
        }
        previousPosition = position;
        sampled = true;
    }

    void BuildFinalRoom()
    {
        if (exitRoot == null) return;
        if (wardrobe == null && wardrobePrefab != null)
        {
            // Door leaves have independent kinematic rigidbodies. Instantiate at the
            // final world pose so their first physics/interpolation pose is not the origin.
            var cabinet = Instantiate(wardrobePrefab, exitRoot.transform.TransformPoint(wardrobePosition),
                exitRoot.transform.rotation * Quaternion.Euler(0, wardrobeYaw, 0), exitRoot.transform);
            cabinet.name = "Fourth ending wardrobe";
            wardrobe = cabinet.AddComponent<FourthEndingWardrobe>();
            wardrobe.ringPrefab = ringPrefab;
            wardrobe.pocketLocalPosition = ringPocketLocalPosition;
            wardrobe.liftSeconds = ringLiftSeconds;
            wardrobe.inspectSeconds = ringInspectSeconds;
            wardrobe.storeSeconds = ringStoreSeconds;
            wardrobe.Initialize(wardrobePrefab);
        }
        if (exteriorBuilt) return;
        var backdrop = exitRoot.transform.Find("Victory White Backdrop");
        var white = backdrop != null ? backdrop.GetComponent<Renderer>()?.sharedMaterial : null;
        if (white == null) return;
        exteriorBuilt = true;
        var surround = new GameObject("Fourth ending white surround");
        surround.transform.SetParent(exitRoot.transform, false);
        float distance = Mathf.Max(10, whiteBoundaryDistance), size = Mathf.Max(30, whiteBoundarySize);
        AddPanel("White left", new Vector3(-distance, 0, 0), new Vector3(.2f, size, size));
        AddPanel("White right", new Vector3(distance, 0, 0), new Vector3(.2f, size, size));
        AddPanel("White forward", new Vector3(0, 0, distance), new Vector3(size, size, .2f));
        AddPanel("White behind window", new Vector3(0, 0, -distance), new Vector3(size, size, .2f));
        AddPanel("White above", new Vector3(0, distance, 0), new Vector3(size, .2f, size));
        AddPanel("White below", new Vector3(0, -.2f, 0), new Vector3(size, .2f, size));

        void AddPanel(string label, Vector3 position, Vector3 scale)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = label;
            panel.transform.SetParent(surround.transform, false);
            panel.transform.localPosition = position; panel.transform.localScale = scale;
            panel.GetComponent<Renderer>().sharedMaterial = white;
            var collider = panel.GetComponent<Collider>();
            collider.enabled = false; Destroy(collider);
        }
    }
}
