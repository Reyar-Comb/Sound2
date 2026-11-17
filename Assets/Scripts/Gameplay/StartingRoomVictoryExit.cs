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
        sampled = crossedDoor = ambienceStarted = false;
    }

    void Update()
    {
        if (!StoryRunProgress.Current.DefeatedBoss || door == null || !door.isActiveAndEnabled) return;
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
}
