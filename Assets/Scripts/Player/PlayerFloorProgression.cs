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
    PlayerDamageFeedback damageFeedback;
    Vector3 initialPosition;
    Quaternion initialRotation;
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
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        ApplyFloor(World != null ? World.levels.IndexOf(World.CurrentLevel)+1 : 0);
        vitality = GetComponent<PlayerVitality>();
        damageFeedback = GetComponent<PlayerDamageFeedback>();
        if (vitality != null) vitality.Died += BeginRespawn;
    }

    void BeginRespawn()
    {
        if (respawn != null || !isActiveAndEnabled) return;
        controller.DeathSequenceLocked = true;
        damageFeedback?.BeginDeath();
        respawn = StartCoroutine(Respawn());
    }

    IEnumerator Animate(float seconds, Action<float> show, bool smooth = true)
    {
        float duration = Mathf.Max(.05f, seconds);
        for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            show(smooth ? Mathf.SmoothStep(0, 1, elapsed / duration) : elapsed / duration);
            yield return null;
        }
        show(1);
    }

    IEnumerator Respawn()
    {
        yield return null; // Ensure the coroutine handle is assigned before cleanup can run.
        if (damageFeedback != null)
        {
            yield return Animate(damageFeedback.engulfSeconds, damageFeedback.ShowEngulf);
            yield return new WaitForSecondsRealtime(Mathf.Max(0, damageFeedback.deathHoldSeconds));
            yield return Animate(damageFeedback.dissolveSeconds, damageFeedback.ShowDissolve, false);
        }
        // Finish an in-flight level transaction before resolving this floor's checkpoint.
        while (World != null && World.Travelling) yield return null;
        ReturnToCheckpoint();
        if (damageFeedback != null)
        {
            damageFeedback.ShowWake(0);
            yield return new WaitForSecondsRealtime(Mathf.Max(0, damageFeedback.whiteHoldSeconds));
            yield return Animate(damageFeedback.wakeSeconds, damageFeedback.ShowWake);
        }
        vitality.RestoreAfterRespawn();
        if (CurrentRule != null) subtitles.Replace(CurrentRule.entrySubtitles);
        FinishRespawn();
        sonar?.Emit();
    }

    void ReturnToCheckpoint()
    {
        var level = World != null ? World.CurrentLevel : null;
        var lift = World != null ? World.ArrivalCheckpoint : null;
        if (lift == null || lift.Level != level)
            lift = level != null ? level.GetComponentInChildren<LevelElevator>() : null;
        if (World == null)
        {
            foreach (var candidate in FindObjectsByType<LevelElevator>())
                if (candidate.gameObject.scene == gameObject.scene) { lift = candidate; break; }
        }
        if (lift != null)
        {
            var capsule = GetComponent<CharacterController>();
            Vector3 local = lift.interiorCenter;
            local.y += capsule.height * .5f - capsule.center.y + capsule.skinWidth - lift.interiorSize.y * .5f;
            lift.SetPaperEffect("_RespawnPaper", 0);
            lift.Open(true);
            controller.Teleport(lift.transform.TransformPoint(local));
            controller.FaceDirection(lift.transform.forward);
        }
        else if (World != null && World.startingPoint != null && level == World.startingLevel)
        {
            controller.Teleport(World.startingPoint.position);
            controller.FaceDirection(World.startingPoint.forward);
        }
        else if (Map != null && Map.FindFloor(CurrentFloor) != null)
        {
            var capsule = GetComponent<CharacterController>();
            var floor = Map.FindFloor(CurrentFloor);
            controller.Teleport(floor.transform.TransformPoint(new Vector3(0,
                capsule.height * .5f - capsule.center.y + capsule.skinWidth + .05f, 0)));
            controller.FaceDirection(floor.transform.forward);
        }
        else if (World == null)
        {
            controller.Teleport(initialPosition);
            controller.FaceDirection(initialRotation * Vector3.forward);
        }
        else Debug.LogWarning("Current level has no elevator checkpoint; awakening in place.", this);
        AwaitingLanding = false;
        previousPosition = transform.position;
    }

    void FinishRespawn()
    {
        damageFeedback?.Clear();
        if (controller != null) controller.DeathSequenceLocked = false;
        respawn = null;
    }

    void OnDisable()
    {
        if (respawn == null) return;
        StopCoroutine(respawn);
        vitality?.RestoreAfterRespawn();
        FinishRespawn();
    }
    void OnDestroy()
    {
        if (vitality != null) vitality.Died -= BeginRespawn;
    }

    public void ResetTraversalSample() => previousPosition = transform.position;

    void LateUpdate()
    {
        Vector3 position = transform.position;
        if (World != null)
        {
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
