using System;
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
    PlayerSubtitles subtitles;
    PlayerController controller;
    SphericalSonar sonar;
    float baseRange, baseAfterglow;
    Vector3 previousPosition;

    void Start()
    {
        subtitles = GetComponent<PlayerSubtitles>(); controller = GetComponent<PlayerController>();
        sonar = GetComponentInChildren<SphericalSonar>();
        if (sonar != null) { baseRange = sonar.range; baseAfterglow = sonar.afterglow; }
        foreach (var map in FindObjectsByType<LevelMap>())
            if (map.gameObject.scene == gameObject.scene) { Map = map; break; }
        if (Map != null && Map.profile != null) profile = Map.profile;
        if (profile == null) profile = Resources.Load<FloorProgressionProfile>("FloorProgression");
        previousPosition = transform.position;
        ApplyFloor(0);
    }

    void LateUpdate()
    {
        Vector3 position = transform.position;
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
}
