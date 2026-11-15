using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;

public enum SonarPulseKind { Main, Footstep, Danger, Coin, Lightning, Gold }

/// <summary>Shared spherical arrival/occlusion data for BlackBox rendering. No input or audio ownership.</summary>
[DisallowMultipleComponent]
public sealed class SonarPulseField : MonoBehaviour
{
    public LayerMask obstacleLayers = Physics.DefaultRaycastLayers;
    [Tooltip("Parallel occlusion queries at the original resolution. Disable only to compare the legacy synchronous path.")]
    public bool batchRaycasts = true;
    public int Emissions { get; private set; }
    public int LastHitCount { get; private set; }
    public float LastSamplingMilliseconds { get; private set; }
    public Vector3 LastOrigin { get; private set; }

    const int MaskSize = 48;
    public const int MainPulseCapacity = 32;
    public const int FootstepPulseCapacity = 4;
    public const int DangerPulseCapacity = 4;
    public const int CoinPulseCapacity = 8;
    public const int LightningPulseCapacity = 4;
    public const int FootstepPulseOffset = MainPulseCapacity;
    public const int DangerPulseOffset = FootstepPulseOffset + FootstepPulseCapacity;
    public const int CoinPulseOffset = DangerPulseOffset + DangerPulseCapacity;
    public const int LightningPulseOffset = CoinPulseOffset + CoinPulseCapacity;
    public const int MaxPulses = MainPulseCapacity + FootstepPulseCapacity + DangerPulseCapacity + CoinPulseCapacity + LightningPulseCapacity;
    const int FootstepMaskSize = 24;
    const int BossWaveCapacity = 6;
    const int RaysPerJob = 64;
    const int HitsPerRay = 8;
    static readonly Vector3[] mainDirections = BuildDirections(MaskSize);
    static readonly Vector3[] localDirections = BuildDirections(FootstepMaskSize);
    static readonly ProfilerMarker emitMarker = new ProfilerMarker("Sonar.Emit");
    static readonly ProfilerMarker syncMarker = new ProfilerMarker("Sonar.SyncTransforms");
    static readonly ProfilerMarker prepareMarker = new ProfilerMarker("Sonar.PrepareRays");
    static readonly ProfilerMarker batchMarker = new ProfilerMarker("Sonar.RaycastBatch");
    static readonly ProfilerMarker legacyMarker = new ProfilerMarker("Sonar.RaycastLegacy");
    static readonly ProfilerMarker encodeMarker = new ProfilerMarker("Sonar.EncodePlanes");
    static readonly ProfilerMarker uploadMarker = new ProfilerMarker("Sonar.UploadPlanes");
    static readonly ProfilerMarker publishMarker = new ProfilerMarker("Sonar.Publish");
    readonly Vector4[] origins = new Vector4[MaxPulses];
    readonly Vector4[] parameters = new Vector4[MaxPulses];
    readonly Vector4[] distanceFalloffs = new Vector4[MaxPulses];
    readonly Vector4[] densityFalloffs = new Vector4[MaxPulses];
    // cos/sin of accumulated doorway yaw; blocker textures stay in their sampled frame.
    readonly Vector4[] blockerRotations = new Vector4[MaxPulses];
    readonly Vector4[] bossWaveOrigins = new Vector4[BossWaveCapacity];
    readonly Vector4[] bossWaveShapes = new Vector4[BossWaveCapacity];
    readonly Vector4[] bossWaveMotion = new Vector4[BossWaveCapacity];
    readonly Vector4[] bossWaveArenas = new Vector4[BossWaveCapacity];
    readonly uint[] pulseTokens = new uint[MaxPulses];
    readonly Color[] planes = new Color[MaskSize * MaskSize];
    Texture2DArray blockers;
    Texture2D blockerUpload;
    NativeArray<RaycastCommand> rayCommands;
    NativeArray<RaycastHit> rayHits;
    NativeArray<RaycastHit> batchHits;
    RaycastHit[] overflowHits = new RaycastHit[16];
    readonly Dictionary<EntityId, bool> ignoredColliders = new();
    Transform samplingSource;
    int pulseIndex, footstepIndex, dangerIndex, coinIndex, lightningIndex;
    int bossWaveIndex;
    bool bossWavesActive;
    static SonarPulseField activeField;
    public static int ActiveColoredPalettes => activeField != null ? activeField.ColoredPalettes() : 0;
    int ColoredPalettes()
    {
        int mask = BallLightning.Instances.Count>0 ? 1<<2 : 0;
        for (int i=DangerPulseOffset;i<MaxPulses;i++)
            if (parameters[i].w>.5f && Time.time-origins[i].w < parameters[i].x/parameters[i].y+parameters[i].z)
                mask |= 1 << (i<CoinPulseOffset ? 1 : i<LightningPulseOffset ? (parameters[i].w > 1.5f ? 4 : 3) : 2);
        for (int i = 0; bossWavesActive && i < BossWaveCapacity; i++)
        {
            var motion = bossWaveMotion[i];
            if (motion.x > 0 && Time.time - bossWaveOrigins[i].w < motion.y / motion.x + motion.z)
                return mask | (1 << 1);
        }
        return mask;
    }
    public int FootstepEmissions { get; private set; }
    public int DangerEmissions { get; private set; }
    public int LastRayCount { get; private set; }
    public int ActivePulseReplacements { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetShaderState() { activeField=null; Shader.SetGlobalFloat("_SonarActive",0); }

    void OnEnable()
    {
        activeField = this;
        blockers = new Texture2DArray(MaskSize, MaskSize, MaxPulses * 6, TextureFormat.RGBAFloat, false, true)
        { name = "Sonar direct-path blocking planes", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        blockerUpload = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBAFloat, false, true)
        { name = "Sonar single-face upload", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        // Full-size buffers also serve the coarser banks through subarrays. Never allocate per emission.
        rayCommands = new NativeArray<RaycastCommand>(mainDirections.Length, Allocator.Persistent);
        rayHits = new NativeArray<RaycastHit>(mainDirections.Length, Allocator.Persistent);
        batchHits = new NativeArray<RaycastHit>(mainDirections.Length * HitsPerRay, Allocator.Persistent);
        ClearPulses();
        Publish();
    }

    void Publish()
    {
        Shader.SetGlobalFloat("_SonarActive", 1);
        Shader.SetGlobalFloat("_SonarMaskSize", MaskSize);
        // New names avoid Unity retaining the previous global array capacity after a reload.
        Shader.SetGlobalVectorArray("_SonarPulseOrigins", origins);
        Shader.SetGlobalVectorArray("_SonarPulseParameters", parameters);
        Shader.SetGlobalVectorArray("_SonarPulseDistanceFalloffs", distanceFalloffs);
        Shader.SetGlobalVectorArray("_SonarPulseDensityFalloffs", densityFalloffs);
        Shader.SetGlobalVectorArray("_SonarBlockerRotations", blockerRotations);
        Shader.SetGlobalTexture("_SonarBlockingPlanes", blockers);
    }

    void PublishBossWaves()
    {
        Shader.SetGlobalFloat("_BossWavesActive", bossWavesActive ? 1 : 0);
        Shader.SetGlobalVectorArray("_BossWaveOrigins", bossWaveOrigins);
        Shader.SetGlobalVectorArray("_BossWaveShapes", bossWaveShapes);
        Shader.SetGlobalVectorArray("_BossWaveMotion", bossWaveMotion);
        Shader.SetGlobalVectorArray("_BossWaveArenas", bossWaveArenas);
    }
    // Identical sampling on all six cube faces avoids excessive samples at sphere poles.
    static Vector3 Direction(int face, float x, float y)
    {
        switch (face)
        {
            case 0: return new Vector3(1, y, x).normalized;
            case 1: return new Vector3(-1, y, x).normalized;
            case 2: return new Vector3(x, 1, y).normalized;
            case 3: return new Vector3(x, -1, y).normalized;
            case 4: return new Vector3(x, y, 1).normalized;
            default: return new Vector3(x, y, -1).normalized;
        }
    }

    static Vector3[] BuildDirections(int size)
    {
        var directions = new Vector3[6 * size * size];
        int index = 0;
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                directions[index++] = Direction(face, (x + .5f) * 2 / size - 1, (y + .5f) * 2 / size - 1);
        return directions;
    }

    bool IgnoreHit(RaycastHit hit)
    {
        var id = hit.colliderEntityId;
        if (ignoredColliders.TryGetValue(id, out bool ignored)) return ignored;
        var collider = hit.collider;
        ignored = collider != null && ((samplingSource != null && collider.transform.IsChildOf(samplingSource))
            || collider.GetComponentInParent<PlayerController>() != null);
        ignoredColliders[id] = ignored;
        return ignored;
    }

    // NonAlloc results are unordered. Grow on saturation rather than accidentally losing a wall
    // behind a compound source. This also serves collectible arrival checks for the current pulse.
    internal bool TryGetPulseBlocker(Vector3 origin, Vector3 direction, float range, out RaycastHit nearest)
    {
        int count;
        while ((count = Physics.RaycastNonAlloc(origin, direction, overflowHits, range,
            obstacleLayers, QueryTriggerInteraction.Ignore)) == overflowHits.Length)
            System.Array.Resize(ref overflowHits, overflowHits.Length * 2);
        nearest = default;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
            if (overflowHits[i].distance < distance && !IgnoreHit(overflowHits[i]))
            { nearest = overflowHits[i]; distance = nearest.distance; }
        return nearest.colliderEntityId != EntityId.None;
    }

    void SampleRays(Vector3 origin, float range, Vector3[] directions, Transform source)
    {
        samplingSource = source;
        ignoredColliders.Clear();
        if (!batchRaycasts)
        {
            using (legacyMarker.Auto())
                for (int i = 0; i < directions.Length; i++)
                {
                    TryGetPulseBlocker(origin, directions[i], range, out var hit);
                    rayHits[i] = hit;
                }
            return;
        }
        using (prepareMarker.Auto())
        {
            var query = new QueryParameters(obstacleLayers, false, QueryTriggerInteraction.Ignore, Physics.queriesHitBackfaces);
            var scene = Physics.defaultPhysicsScene;
            for (int i = 0; i < directions.Length; i++)
                rayCommands[i] = new RaycastCommand(scene, origin, directions[i], query, range);
        }
        using (batchMarker.Auto())
        {
            // Complete before publishing or returning: keep same-frame audio/visual triggers and
            // never leave a job using these buffers when the field is disabled or destroyed.
            RaycastCommand.ScheduleBatch(rayCommands.GetSubArray(0, directions.Length),
                batchHits.GetSubArray(0, directions.Length * HitsPerRay), RaysPerJob, HitsPerRay).Complete();
            for (int i = 0; i < directions.Length; i++)
            {
                RaycastHit nearest = default;
                float distance = float.PositiveInfinity;
                int count = 0;
                for (; count < HitsPerRay; count++)
                {
                    var hit = batchHits[i * HitsPerRay + count];
                    // Unity only guarantees the first invalid entry; later entries may be stale.
                    if (hit.colliderEntityId == EntityId.None) break;
                    if (hit.distance < distance && !IgnoreHit(hit))
                    { nearest = hit; distance = hit.distance; }
                }
                if (count == HitsPerRay)
                    TryGetPulseBlocker(origin, directions[i], range, out nearest);
                rayHits[i] = nearest;
            }
        }
    }

    public bool EmitFrom(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow)
        => EmitFrom(origin, pulseRange, pulseSpeed, pulseAfterglow, 0, pulseRange, 0, 1);

    public bool EmitFrom(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow,
        SonarDistanceSettings distanceSettings, Transform source = null)
        => Emit(origin, pulseRange, pulseSpeed, pulseAfterglow,
            distanceSettings != null ? distanceSettings.Pack(pulseRange) : new Vector4(0, pulseRange, 0, 1), 0, source: source);

    internal bool IsWhitePulseCurrent(int slot, uint token) => isActiveAndEnabled && parameters[slot].w > .5f && pulseTokens[slot] == token;

    // Compatibility entry point; a zero power preserves the original brightness.
    public bool EmitFrom(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow,
        float fadeStart, float fadeEnd, float falloffPower, float strength, Transform source = null)
        => Emit(origin, pulseRange, pulseSpeed, pulseAfterglow,
            new Vector4(fadeStart, fadeEnd, falloffPower, strength), 0, source: source);

    public bool EmitFootstepFrom(Vector3 origin, float range, float speed, float afterglow,
        SonarDistanceSettings distanceSettings, Transform source = null)
        => Emit(origin, range, speed, afterglow,
            distanceSettings != null ? distanceSettings.Pack(range) : new Vector4(0, range, 0, 1), 1, source: source);

    public bool EmitDangerFrom(Vector3 origin, float range, float speed, float afterglow,
        SonarDistanceSettings distanceSettings, SonarDensitySettings densitySettings = null, Transform source = null)
        => EmitFrom(origin,range,speed,afterglow,SonarPulseKind.Danger,distanceSettings,densitySettings,source);

    internal void EmitBossGroundWave(Vector3 origin, Vector3 arenaCenter, float arenaRadius,
        Vector3 forward, BossWaveSettings settings)
    {
        if (!isActiveAndEnabled || blockers == null) return;
        int slot = bossWaveIndex;
        bossWaveIndex = (slot + 1) % BossWaveCapacity;
        bossWaveOrigins[slot] = new Vector4(origin.x, origin.y, origin.z, Time.time);
        bossWaveArenas[slot] = new Vector4(arenaCenter.x, arenaCenter.z, arenaRadius, settings.height);
        bossWaveShapes[slot] = new Vector4(forward.x, forward.z, settings.sweepWidth * .5f, settings.thickness * .5f);
        bossWaveMotion[slot] = new Vector4(settings.speed, settings.range, Mathf.Max(.05f, settings.afterglow), 0);
        bossWavesActive = true;
        PublishBossWaves();
    }

    public bool EmitFrom(Vector3 origin, float range, float speed, float afterglow, SonarPulseKind kind,
        SonarDistanceSettings distanceSettings, SonarDensitySettings densitySettings = null, Transform source = null)
        => Emit(origin,range,speed,afterglow,
            distanceSettings != null ? distanceSettings.Pack(range) : new Vector4(0,range,0,1),
            (int)kind,densitySettings != null ? densitySettings.Pack(range) : Vector4.zero,source);

    bool Emit(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow,
        Vector4 falloff, int bank, Vector4 densityFalloff = default, Transform source = null)
    {
        if (!isActiveAndEnabled || blockers == null) return false;
        pulseRange = Mathf.Max(1, pulseRange);
        pulseSpeed = Mathf.Max(.1f, pulseSpeed);
        pulseAfterglow = Mathf.Max(.05f, pulseAfterglow);
        using var emissionScope = emitMarker.Auto();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        LastOrigin = origin;
        using (syncMarker.Auto()) Physics.SyncTransforms();
        bool footstep = bank == 1, danger = bank == 2;
        int slot = bank == 4 ? SelectSlot(LightningPulseOffset, LightningPulseCapacity, ref lightningIndex)
            : bank == 3 || bank == 5 ? SelectSlot(CoinPulseOffset, CoinPulseCapacity, ref coinIndex)
            : danger ? SelectSlot(DangerPulseOffset, DangerPulseCapacity, ref dangerIndex)
            : footstep ? SelectSlot(FootstepPulseOffset, FootstepPulseCapacity, ref footstepIndex)
            : SelectSlot(0, MainPulseCapacity, ref pulseIndex);
        int sampleSize = bank != 0 ? FootstepMaskSize : MaskSize;
        int hitCount = 0;
        float effectiveRange = falloff.z > 0 ? Mathf.Min(pulseRange, Mathf.Max(.01f, falloff.y)) : pulseRange;
        if (densityFalloff.w>.5f) effectiveRange=Mathf.Min(effectiveRange,densityFalloff.y);
        SampleRays(origin, effectiveRange, bank == 0 ? mainDirections : localDirections, source);
        hitCount = EncodePlanes(origin, sampleSize, blockers, slot * 6);
        origins[slot] = new Vector4(LastOrigin.x, LastOrigin.y, LastOrigin.z, Time.time);
        blockerRotations[slot] = new Vector4(1, 0, 0, 0);
        parameters[slot] = new Vector4(pulseRange, pulseSpeed, pulseAfterglow, bank == 5 ? 2 : 1);
        float fadeEnd = Mathf.Clamp(falloff.y, .01f, pulseRange);
        distanceFalloffs[slot] = new Vector4(Mathf.Clamp(falloff.x, 0, fadeEnd - .001f), fadeEnd,
            Mathf.Clamp(falloff.z, 0, 8), Mathf.Clamp01(falloff.w));
        densityFalloffs[slot] = densityFalloff;
        uint token = ++pulseTokens[slot];
        if (bank < (int)SonarPulseKind.Danger && falloff.w > 0 && StoryCollectible.Active.Count > 0
            && (!TryGetComponent<SphericalSonar>(out var sonar) || sonar.sonarRendering))
            StoryCollectible.ScheduleWhitePulse(this, slot, token, origin, effectiveRange, pulseSpeed);
        using (publishMarker.Auto()) Publish();
        Emissions++;
        if (footstep) FootstepEmissions++;
        if (danger) DangerEmissions++;
        LastRayCount = 6 * sampleSize * sampleSize;
        LastHitCount = hitCount;
        LastSamplingMilliseconds = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - start)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        return true;
    }

    int EncodePlanes(Vector3 origin, int sampleSize, Texture2DArray target, int firstSlice)
    {
        int blockSize = MaskSize / sampleSize, hitCount = 0;
        int rayIndex = 0;
        for (int face = 0; face < 6; face++)
        {
            using (encodeMarker.Auto())
                for (int y = 0; y < sampleSize; y++) for (int x = 0; x < sampleSize; x++)
                {
                    var hit = rayHits[rayIndex++];
                    Color plane;
                    // Only hit presence is needed; avoid resolving thousands of managed Collider objects.
                    if (hit.colliderEntityId != EntityId.None)
                    {
                        // The first surface plane gates reachability only. Visible contours
                        // are detected and drawn entirely by BlackBoxScreenPoints.shader.
                        var normal = hit.normal;
                        plane = new Color(normal.x, normal.y, normal.z,
                            Vector3.Dot(normal, hit.point - origin));
                        hitCount++;
                    }
                    else plane = Color.clear;
                    // Local waves keep the same replicated coarse grid and texture layout.
                    for (int by = 0; by < blockSize; by++) for (int bx = 0; bx < blockSize; bx++)
                        planes[(y * blockSize + by) * MaskSize + x * blockSize + bx] = plane;
                }
            using (uploadMarker.Auto())
            {
                // Upload only this wave's six faces, not the entire expanded pulse pool.
                blockerUpload.SetPixels(planes);
                blockerUpload.Apply(false);
                Graphics.CopyTexture(blockerUpload, 0, 0, target, firstSlice + face, 0);
            }
        }
        return hitCount;
    }

    /// <summary>One snapshot for an audio-driven source; does not consume or publish a manual pulse.</summary>
    public bool CaptureBlockingPlanes(Vector3 origin, float range, Texture2DArray target, int firstSlice, bool highQuality = false, Transform source = null)
    {
        if (!isActiveAndEnabled || blockers == null || target == null || target.width != MaskSize
            || target.height != MaskSize || firstSlice < 0 || firstSlice + 6 > target.depth) return false;
        using (syncMarker.Auto()) Physics.SyncTransforms();
        int size = highQuality ? MaskSize : FootstepMaskSize;
        SampleRays(origin, Mathf.Max(.01f, range), highQuality ? mainDirections : localDirections, source);
        EncodePlanes(origin, size, target, firstSlice);
        return true;
    }

    int SelectSlot(int offset, int capacity, ref int cursor)
    {
        int selected = offset + cursor;
        float earliestEnd = float.PositiveInfinity;
        for (int n = 0; n < capacity; n++)
        {
            int slot = offset + (cursor + n) % capacity;
            var settings = parameters[slot];
            float end = settings.w < .5f ? float.NegativeInfinity
                : origins[slot].w + settings.x / settings.y + settings.z;
            if (end <= Time.time)
            {
                cursor = (slot - offset + 1) % capacity;
                return slot;
            }
            if (end < earliestEnd) { earliestEnd = end; selected = slot; }
        }
        // At saturation retire the wave closest to its natural end; other banks stay isolated.
        ActivePulseReplacements++;
        cursor = (selected - offset + 1) % capacity;
        return selected;
    }

    // Preserve the existing white reveal without emitting sound, restarting its fade, or
    // resampling thousands of rays during a seamless doorway transfer. New waves sample normally.
    // Colored world cues keep their real positions: never copy a danger or collectible hint.
    public void RemapDoorwayReveal(Vector3 origin, Vector3 destination, float yawDelta)
    {
        if (!isActiveAndEnabled || blockers == null) return;
        var rotation = Quaternion.Euler(0, yawDelta, 0);
        float c = Mathf.Cos(yawDelta * Mathf.Deg2Rad), s = Mathf.Sin(yawDelta * Mathf.Deg2Rad);
        for (int i = 0; i < DangerPulseOffset; i++)
        {
            var settings = parameters[i];
            if (settings.w < .5f || Time.time - origins[i].w >= settings.x / settings.y + settings.z) continue;
            Vector3 mapped = destination + rotation * ((Vector3)origins[i] - origin);
            origins[i] = new Vector4(mapped.x, mapped.y, mapped.z, origins[i].w);
            var basis = blockerRotations[i];
            blockerRotations[i] = new Vector4(c * basis.x - s * basis.y, s * basis.x + c * basis.y, 0, 0);
        }
        Publish();
    }

    public void ClearPulses()
    {
        for (int i = 0; i < MaxPulses; i++) parameters[i] = Vector4.zero;
        for (int i = 0; i < BossWaveCapacity; i++) bossWaveMotion[i] = Vector4.zero;
        bossWaveIndex = 0;
        bossWavesActive = false;
        PublishBossWaves();
        pulseIndex = footstepIndex = dangerIndex = coinIndex = lightningIndex = 0;
        if (blockers != null) Publish();
    }
    void OnDisable()
    {
        if (activeField == this) activeField = null;
        Shader.SetGlobalFloat("_SonarActive", 0);
        Shader.SetGlobalFloat("_BossWavesActive", 0);
        Shader.SetGlobalTexture("_SonarBlockingPlanes", null);
        // SampleRays completes its batch synchronously, so no outstanding job can reference these.
        if (rayCommands.IsCreated) rayCommands.Dispose();
        if (rayHits.IsCreated) rayHits.Dispose();
        if (batchHits.IsCreated) batchHits.Dispose();
        samplingSource = null;
        ignoredColliders.Clear();
        if (blockers != null) Destroy(blockers);
        if (blockerUpload != null) Destroy(blockerUpload);
        blockers = null;
        blockerUpload = null;
    }
}
