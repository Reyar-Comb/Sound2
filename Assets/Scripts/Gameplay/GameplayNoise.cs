using System;
using UnityEngine;

public enum GameplayNoiseKind { Footstep, Impact }

/// <summary>AI hearing stimulus; independent of AudioClip assignment and visual reveal range.</summary>
public readonly struct GameplayNoiseEvent
{
    public readonly Vector3 position;
    public readonly float range;
    public readonly GameplayNoiseKind kind;
    public readonly Component source;
    public GameplayNoiseEvent(Vector3 position, float range, GameplayNoiseKind kind, Component source)
    { this.position = position; this.range = range; this.kind = kind; this.source = source; }
}

public static class GameplayNoise
{
    public static event Action<GameplayNoiseEvent> Emitted;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => Emitted = null;

    public static void Emit(Vector3 position, float range, GameplayNoiseKind kind, Component source)
    {
        if (range > 0) Emitted?.Invoke(new GameplayNoiseEvent(position, range, kind, source));
    }
}
