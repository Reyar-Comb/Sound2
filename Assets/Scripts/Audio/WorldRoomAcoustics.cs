using System.Collections.Generic;
using Sound2.LevelEditing;
using Unity.Profiling;
using UnityEngine;

/// <summary>One listener-centred room reverb and a shared budget for world-sound occlusion.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(LevelWorld))]
public sealed class WorldRoomAcoustics : MonoBehaviour
{
    internal static WorldRoomAcoustics Current { get; private set; }
    internal const float ClearCutoff = 22000;
    internal const float WallCutoff = 1500;
    internal const float WallGain = .32f;
    const float MaxCheckDistance = 24;
    const int ChecksPerFrame = 6;
    static readonly ProfilerMarker wallCheck = new("Audio.WorldWallOcclusion");

    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly HashSet<LevelElevator> elevators = new();
    LevelWorld world;
    LevelLayout elevatorLevel;
    AudioReverbZone reverb;
    int checkFrame = -1, usedChecks;
    bool insideElevator;
    float wetLevel = -10000, decay = .7f;
    internal bool Available => isActiveAndEnabled && world.roomAcousticsEnabled;

    void Awake() => world = GetComponent<LevelWorld>();

    void OnEnable() => Current = this;

    void OnDisable()
    {
        if (Current == this) Current = null;
        if (reverb != null) reverb.enabled = false;
    }

    void Update()
    {
        if (!Available)
        {
            if (reverb != null) reverb.enabled = false;
            wetLevel = -10000;
            return;
        }
        var player = world.player;
        if (player == null || player.ViewTransform == null || world.CurrentLevel == null) return;
        if (reverb == null)
        {
            var zone = new GameObject("World room reverb");
            zone.transform.SetParent(transform, false);
            reverb = zone.AddComponent<AudioReverbZone>();
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.minDistance = 1;
            reverb.maxDistance = 2;
            reverb.room = -10000;
            reverb.roomHF = -350;
            reverb.decayHFRatio = .65f;
            reverb.reflections = -2300;
            reverb.reverb = -400;
        }

        reverb.transform.position = player.ViewTransform.position;
        var level = world.CurrentLevel;
        insideElevator = world.Travelling || IsInsideElevator(level, player.transform.position);
        var room = FindRoom(level, player.transform.position);
        RoomReverb(room, out float roomWet, out float roomDecay);
        float amount = Mathf.Clamp(level.roomReverbAmount, 0, 2);
        if (insideElevator || SonicSpaceStartMenu.BlocksGameplay || amount <= 0) roomWet = -10000;
        else roomWet = Mathf.Clamp(roomWet + 2000 * Mathf.Log10(amount), -10000, 0);
        float blend = 1 - Mathf.Exp(-8 * Time.unscaledDeltaTime);
        wetLevel = Mathf.Lerp(wetLevel, roomWet, blend);
        decay = Mathf.Lerp(decay, roomDecay, blend);
        reverb.room = Mathf.RoundToInt(wetLevel);
        reverb.decayTime = decay;
        reverb.enabled = wetLevel > -9900;
    }

    bool IsInsideElevator(LevelLayout level, Vector3 position)
    {
        if (elevatorLevel != level)
        {
            elevatorLevel = level;
            elevators.Clear();
            foreach (var connection in world.connections)
            {
                AddElevator(connection.source);
                AddElevator(connection.destination);
            }
        }
        foreach (var elevator in elevators)
            if (elevator != null && elevator.isActiveAndEnabled && elevator.ContainsForAcoustics(position))
                return true;
        return false;
    }

    void AddElevator(LevelElevator elevator)
    {
        if (elevator != null && elevator.Level == elevatorLevel) elevators.Add(elevator);
    }

    static LevelRoom FindRoom(LevelLayout level, Vector3 position)
    {
        position = level.transform.InverseTransformPoint(position);
        var point = new Vector2(position.x, position.z);
        LevelRoom best = null;
        float smallest = float.PositiveInfinity;
        foreach (var room in level.rooms)
        {
            if (room == null) continue;
            float area = room.size.x * room.size.y;
            if (area >= smallest) continue;
            Vector2 local = room.PartitionLocal(point);
            bool contains = room.shape == RoomShape.Circle ? local.sqrMagnitude <= room.size.x * room.size.x * .25f
                : Mathf.Abs(local.x) <= room.size.x * .5f && Mathf.Abs(local.y) <= room.size.y * .5f;
            if (!contains) continue;
            best = room;
            smallest = area;
        }
        return best;
    }

    static void RoomReverb(LevelRoom room, out float wet, out float seconds)
    {
        wet = -1700;
        seconds = .85f;
        if (room == null) return; // Connecting corridors and uncategorized space.
        seconds = Mathf.Lerp(.45f, 1.55f, Mathf.InverseLerp(4, 30, Mathf.Max(room.size.x, room.size.y)));
        switch (room.use)
        {
            case RoomUse.Living: case RoomUse.Bedroom: case RoomUse.Reading: case RoomUse.Shelves:
                wet = -2250; seconds *= .72f; break;
            case RoomUse.Classroom: case RoomUse.Ward: case RoomUse.Clinic:
                wet = -1500; break;
            case RoomUse.Bathroom: case RoomUse.Treatment:
                wet = -1200; seconds = Mathf.Max(.9f, seconds); break;
            case RoomUse.Hall: case RoomUse.Dining: case RoomUse.Arena:
                wet = -1050; seconds = Mathf.Max(1.35f, seconds); break;
        }
    }

    internal bool TrySample(Transform source, Vector3 listener, float distance, out bool blocked)
    {
        blocked = false;
        var level = world.CurrentLevel;
        if (insideElevator || level == null || world.Travelling || distance < .2f || distance > MaxCheckDistance) return true;
        if (checkFrame != Time.frameCount) { checkFrame = Time.frameCount; usedChecks = 0; }
        if (usedChecks >= ChecksPerFrame) return false;
        usedChecks++;
        using var scope = wallCheck.Auto();
        var direction = (source.position - listener) / distance;
        int count = Physics.RaycastNonAlloc(listener, direction, hits, distance - .08f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        var geometry = level.generatedRoot;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i].collider;
            if (hit == null) continue;
            if (geometry != null && hit.transform.IsChildOf(geometry.transform)) { blocked = true; return true; }
            var door = hit.GetComponentInParent<DoorInteraction>();
            if (door != null && hit == door.leafCollider && !source.IsChildOf(door.transform)) { blocked = true; return true; }
            var lift = hit.GetComponentInParent<LevelElevator>();
            if (lift != null && ((lift.leftDoor != null && hit.transform.IsChildOf(lift.leftDoor))
                || (lift.rightDoor != null && hit.transform.IsChildOf(lift.rightDoor))))
            { blocked = true; return true; }
        }
        return true;
    }
}
