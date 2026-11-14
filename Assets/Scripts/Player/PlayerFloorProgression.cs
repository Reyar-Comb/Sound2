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
    public UnityEvent<int> onResourcesChanged = new();
    public int CollectedResources { get; private set; }
    public event Action<FloorRule> FloorChanged;
    public int CurrentFloor { get; private set; }
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
        if (shaft == null || !shaft.isActiveAndEnabled || shaft.Map != Map || shaft.sourceFloor != CurrentFloor
            || shaft.destinationFloor != CurrentFloor + 1 || !shaft.HasDestination) return false;
        if (Map != null) Map.AdvanceTo(CurrentFloor + 1);
        ApplyFloor(CurrentFloor + 1);
        return true;
    }

    public void CollectResource(int amount)
    {
        if (amount <= 0) return;
        CollectedResources += amount;
        onResourcesChanged.Invoke(CollectedResources);
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
