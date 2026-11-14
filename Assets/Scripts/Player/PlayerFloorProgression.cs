using System;
using System.Collections;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.Events;

[DefaultExecutionOrder(70)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController), typeof(PlayerSubtitles))]
public sealed class PlayerFloorProgression : MonoBehaviour
{
    public FloorProgressionProfile profile;
    public UnityEvent<int> onFloorChanged = new();
    public event Action<FloorRule> FloorChanged;
    public int CurrentFloor { get; private set; }
    public bool AwaitingLanding { get; private set; }
    public FloorRule CurrentRule { get; private set; }
    public float Difficulty => CurrentRule != null ? CurrentRule.difficulty : 1;
    public LevelMap Map { get; private set; }
    public LevelWorld World { get; private set; }
    PlayerSubtitles subtitles;
    PlayerController controller;
    SphericalSonar sonar;
    float baseRange, baseAfterglow;
    Vector3 previousPosition;

    PlayerVitality vitality;
    Coroutine respawn;
    Material respawnMaterial;
    Canvas respawnCanvas;
    LevelElevator respawnLift;
    bool inputWasSuspended;
    public bool IsRespawning => respawn != null;

    void Start()
    {
        subtitles = GetComponent<PlayerSubtitles>(); controller = GetComponent<PlayerController>();
        sonar = GetComponentInChildren<SphericalSonar>();
        if (sonar != null) { baseRange = sonar.range; baseAfterglow = sonar.afterglow; }
        foreach (var map in FindObjectsByType<LevelMap>())
            if (map.gameObject.scene == gameObject.scene) { Map = map; break; }
        if (Map != null && Map.profile != null) profile = Map.profile;
        foreach(var world in FindObjectsByType<LevelWorld>())if(world.player==controller){World=world;Map=null;profile=world.profile;break;}
        if (profile == null) profile = Resources.Load<FloorProgressionProfile>("FloorProgression");
        previousPosition = transform.position;
        ApplyFloor(World != null ? World.levels.IndexOf(World.CurrentLevel)+1 : 0);
        vitality = GetComponent<PlayerVitality>();
        if (World != null && vitality != null) vitality.Died += BeginRespawn;
    }

    void BeginRespawn()
    {
        if (respawn != null || !isActiveAndEnabled) return;
        inputWasSuspended = controller.InputSuspended;
        controller.InputSuspended = true;
        respawn = StartCoroutine(Respawn());
    }

    IEnumerator Respawn()
    {
        yield return null;
        // A lift already in motion finishes its floor transaction before choosing a checkpoint.
        var menu = GetComponent<SonicSpaceStartMenu>() ?? FindAnyObjectByType<SonicSpaceStartMenu>();
        Shader shader = menu != null ? menu.menuShader : Shader.Find("Sound2/SonicSpace Point Menu");
        if (shader != null)
        {
            var cover = new GameObject("Respawn point cover", typeof(RectTransform), typeof(Canvas));
            cover.transform.SetParent(transform, false);
            respawnCanvas = cover.GetComponent<Canvas>();
            respawnCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            respawnCanvas.sortingOrder = 31999;
            var panel = new GameObject("White points", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
            panel.transform.SetParent(cover.transform, false);
            var image = panel.GetComponent<UnityEngine.UI.RawImage>();
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            respawnMaterial = new Material(shader);
            image.material = respawnMaterial;
        }

        var camera = controller.GetComponentInChildren<Camera>();
        if (respawnMaterial != null)
        {
            for (float time = 0; time < .48f; time += Time.unscaledDeltaTime)
            {
                float height = Mathf.Max(1, Screen.height);
                respawnMaterial.SetVector("_MenuSize", new Vector4(Screen.width, height, 720 * Screen.width / height, 720));
                respawnMaterial.SetFloat("_GridPitch", BlackBoxPointCloudFeature.GetDotSpacing(camera));
                respawnMaterial.SetFloat("_DotRadius", BlackBoxPointCloudFeature.GetDotDiameter(camera) * .5f);
                respawnMaterial.SetFloat("_RespawnCover", Mathf.Max(.001f, time / .48f));
                yield return null;
            }
            respawnMaterial.SetFloat("_RespawnCover", 1);
            yield return null;
        }

        while (World.Travelling) yield return null;

        var level = World.CurrentLevel;
        respawnLift = World.ArrivalCheckpoint;
        if (respawnLift == null || respawnLift.Level != level)
            respawnLift = level != null ? level.GetComponentInChildren<LevelElevator>() : null;
        if (respawnLift != null)
        {
            var capsule = GetComponent<CharacterController>();
            Vector3 local = respawnLift.interiorCenter;
            local.y += capsule.height * .5f - capsule.center.y + capsule.skinWidth - respawnLift.interiorSize.y * .5f;
            respawnLift.Open(true);
            respawnLift.SetPaperEffect("_RespawnPaper", .001f);
            controller.Teleport(respawnLift.transform.TransformPoint(local));
            controller.FaceDirection(respawnLift.transform.forward);
        }
        else if (World.startingPoint != null && level == World.startingLevel)
            controller.Teleport(World.startingPoint.position);

        if (respawnCanvas != null) respawnCanvas.enabled = false;
        if (respawnLift != null)
        {
            for (float time = 0; time < .65f; time += Time.unscaledDeltaTime)
            {
                respawnLift.SetPaperEffect("_RespawnPaper", Mathf.Max(.001f, time / .65f));
                yield return null;
            }
            respawnLift.SetPaperEffect("_RespawnPaper", 1);
        }

        vitality.RestoreHealth();
        if (CurrentRule != null) subtitles.Replace(CurrentRule.entrySubtitles);
        FinishRespawn();
        sonar?.Emit();
    }

    void FinishRespawn()
    {
        if (respawnCanvas != null) Destroy(respawnCanvas.gameObject);
        if (respawnMaterial != null) Destroy(respawnMaterial);
        if (controller != null) controller.InputSuspended = inputWasSuspended;
        respawn = null;
    }

    void OnDestroy()
    {
        if (vitality != null) vitality.Died -= BeginRespawn;
        if (respawnLift != null) respawnLift.SetPaperEffect("_RespawnPaper", 0);
        if (respawn != null) FinishRespawn();
    }

    void LateUpdate()
    {
        Vector3 position = transform.position;
        if (World != null)
        {
            if (respawn == null && respawnLift != null && !respawnLift.Contains(position))
            {
                respawnLift.SetPaperEffect("_RespawnPaper", 0);
                respawnLift = null;
            }
            previousPosition = position;
            return;
        }
        if (AwaitingLanding && Map != null && controller != null)
        {
            var floor = Map.FindFloor(CurrentFloor);
            var capsule = GetComponent<CharacterController>();
            if (floor != null && capsule.enabled)
            {
                float localY = floor.transform.InverseTransformPoint(position).y;
                if (capsule.isGrounded && localY >= 0 && localY < capsule.height + .5f)
                    AwaitingLanding = false;
                else if (localY < 0)
                {
                    // Recover on the destination's safe arrival slab if a long frame or
                    // external teleport skipped its floor. Never promote a second floor.
                    controller.Teleport(floor.transform.TransformPoint(new Vector3(0,
                        capsule.height * .5f - capsule.center.y + capsule.skinWidth + .05f, 0)));
                    position = transform.position;
                }
            }
        }
        if (controller != null && controller.isActiveAndEnabled && !controller.InputSuspended)
        {
            foreach (var shaft in FloorShaftTransition.Active)
            {
                if (shaft == null || !shaft.isActiveAndEnabled || shaft.gameObject.scene != gameObject.scene) continue;
                if (shaft.Map != Map || !shaft.CrossedDown(previousPosition, position)) continue;
                if (TryDescend(shaft)) break; // One completed shaft per movement sample.
            }
        }
        previousPosition = position;
    }

    public bool TryDescend(FloorShaftTransition shaft)
    {
        if (World != null) return false;
        if (AwaitingLanding || shaft == null || !shaft.isActiveAndEnabled || shaft.Map != Map || shaft.sourceFloor != CurrentFloor
            || shaft.destinationFloor != CurrentFloor + 1 || !shaft.HasDestination) return false;
        if (Map != null) Map.AdvanceTo(CurrentFloor + 1);
        AwaitingLanding = Map != null;
        ApplyFloor(CurrentFloor + 1);
        return true;
    }

    void ApplyFloor(int floor)
    {
        CurrentFloor = floor;
        CurrentRule = profile != null ? profile.Evaluate(floor) : new FloorRule { floor = floor };
        if (sonar != null && profile != null && profile.applySonarDifficulty)
        {
            // Always relative to the initial values; repeated events cannot compound multipliers.
            sonar.range = Mathf.Max(1, baseRange * CurrentRule.sonarRangeMultiplier);
            sonar.afterglow = Mathf.Max(.05f, baseAfterglow * CurrentRule.sonarAfterglowMultiplier);
        }
        subtitles.SetFloor(floor); subtitles.Replace(CurrentRule.entrySubtitles);
        onFloorChanged.Invoke(floor); FloorChanged?.Invoke(CurrentRule);
    }

    public void EnterAuthoredLevel(int number)
    {
        if(World==null||number<1||number>World.levels.Count)return;
        AwaitingLanding=false;ApplyFloor(number);previousPosition=transform.position;
    }
}
