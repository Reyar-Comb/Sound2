using System;
using UnityEngine;

[Serializable]
public sealed class BossWaveSettings
{
    [Min(.1f)] public float speed = 12;
    [Min(.1f)] public float range = 38;
    [Min(.05f)] public float thickness = .9f;
    [Min(.05f)] public float height = .55f;
    [Min(.1f)] public float sweepWidth = 12;
    [Min(.05f)] public float afterglow = .26f;
}

internal sealed class BossGroundWave
{
    readonly LevelArenaBoss owner;
    readonly BossWaveSettings settings;
    readonly Vector2 origin, forward, right;
    readonly bool directional;
    float distance;
    bool hit;

    public BossGroundWave(LevelArenaBoss owner, BossWaveSettings settings, Vector2 origin, Vector2 forward)
    {
        this.owner = owner;
        this.settings = settings;
        this.origin = origin;
        this.forward = forward;
        right = new Vector2(forward.y, -forward.x);
        directional = forward.sqrMagnitude > .5f;
    }

    public bool Advance(float deltaTime, Bounds target, bool canHit, Vector2 previousTarget, float previousFeetY)
    {
        float previous = distance;
        distance = Mathf.Min(settings.range, distance + settings.speed * deltaTime);
        if (!hit && canHit && target.max.y >= owner.FloorY)
        {
            Vector2 local = owner.ToArena(target.center);
            float halfDepth = settings.thickness * .5f + Mathf.Max(target.extents.x, target.extents.z);
            Vector2 a = previousTarget - origin;
            Vector2 b = local - origin;
            float oldDepth = directional ? Vector2.Dot(a, forward) : a.magnitude;
            float newDepth = directional ? Vector2.Dot(b, forward) : b.magnitude;
            float oldGap = oldDepth - previous;
            float newGap = newDepth - distance;
            if (Mathf.Min(oldGap, newGap) <= halfDepth && Mathf.Max(oldGap, newGap) >= -halfDepth)
            {
                float fraction = Mathf.Approximately(oldGap, newGap) ? 1 : Mathf.Clamp01(oldGap / (oldGap - newGap));
                Vector2 crossing = Vector2.Lerp(previousTarget, local, fraction);
                float feet = Mathf.Lerp(previousFeetY, target.min.y, fraction);
                if (feet <= owner.FloorY + settings.height && owner.InArena(crossing) && (!directional
                    || Mathf.Abs(Vector2.Dot(crossing - origin, right)) <= settings.sweepWidth * .5f + target.extents.x))
                {
                    hit = true;
                    owner.DamagePlayer();
                }
            }
        }
        return distance < settings.range;
    }
}
