using Unity.Collections;
using Unity.Profiling;
using UnityEngine;

public enum SonarPulseKind { Main, Footstep, Danger, Coin, Lightning }

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
    const int RaysPerJob = 64;
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
    readonly Color[] planes = new Color[MaskSize * MaskSize];
    Texture2DArray blockers;
    Texture2D blockerUpload;
    NativeArray<RaycastCommand> rayCommands;
    NativeArray<RaycastHit> rayHits;
    int pulseIndex, footstepIndex, dangerIndex, coinIndex, lightningIndex;
    static SonarPulseField activeField;
    public static int ActiveColoredPalettes => activeField != null ? activeField.ColoredPalettes() : 0;
    int ColoredPalettes()
    {
        int mask = BallLightning.Instances.Count>0 ? 1<<2 : 0;
        for (int i=DangerPulseOffset;i<MaxPulses;i++)
            if (parameters[i].w>.5f && Time.time-origins[i].w < parameters[i].x/parameters[i].y+parameters[i].z)
                mask |= 1 << (i<CoinPulseOffset ? 1 : i<LightningPulseOffset ? 3 : 2);
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

    void SampleRays(Vector3 origin, float range, Vector3[] directions)
    {
        if (!batchRaycasts)
        {
            using (legacyMarker.Auto())
                for (int i = 0; i < directions.Length; i++)
                {
                    Physics.Raycast(origin, directions[i], out var hit, range, obstacleLayers, QueryTriggerInteraction.Ignore);
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
                rayHits.GetSubArray(0, directions.Length), RaysPerJob, 1).Complete();
        }
    }

    public bool EmitFrom(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow)
        => EmitFrom(origin, pulseRange, pulseSpeed, pulseAfterglow, 0, pulseRange, 0, 1);

    public bool EmitFrom(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow,
        SonarDistanceSettings distanceSettings)
        => Emit(origin, pulseRange, pulseSpeed, pulseAfterglow,
            distanceSettings != null ? distanceSettings.Pack(pulseRange) : new Vector4(0, pulseRange, 0, 1), 0);

    // Compatibility entry point; a zero power preserves the original brightness.
    public bool EmitFrom(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow,
        float fadeStart, float fadeEnd, float falloffPower, float strength)
        => Emit(origin, pulseRange, pulseSpeed, pulseAfterglow,
            new Vector4(fadeStart, fadeEnd, falloffPower, strength), 0);

    public bool EmitFootstepFrom(Vector3 origin, float range, float speed, float afterglow,
        SonarDistanceSettings distanceSettings)
        => Emit(origin, range, speed, afterglow,
            distanceSettings != null ? distanceSettings.Pack(range) : new Vector4(0, range, 0, 1), 1);

    public bool EmitDangerFrom(Vector3 origin, float range, float speed, float afterglow,
        SonarDistanceSettings distanceSettings, SonarDensitySettings densitySettings = null)
        => EmitFrom(origin,range,speed,afterglow,SonarPulseKind.Danger,distanceSettings,densitySettings);

    public bool EmitFrom(Vector3 origin, float range, float speed, float afterglow, SonarPulseKind kind,
        SonarDistanceSettings distanceSettings, SonarDensitySettings densitySettings = null)
        => Emit(origin,range,speed,afterglow,
            distanceSettings != null ? distanceSettings.Pack(range) : new Vector4(0,range,0,1),
            (int)kind,densitySettings != null ? densitySettings.Pack(range) : Vector4.zero);

    bool Emit(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow,
        Vector4 falloff, int bank, Vector4 densityFalloff = default)
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
            : bank == 3 ? SelectSlot(CoinPulseOffset, CoinPulseCapacity, ref coinIndex)
            : danger ? SelectSlot(DangerPulseOffset, DangerPulseCapacity, ref dangerIndex)
            : footstep ? SelectSlot(FootstepPulseOffset, FootstepPulseCapacity, ref footstepIndex)
            : SelectSlot(0, MainPulseCapacity, ref pulseIndex);
        int sampleSize = bank != 0 ? FootstepMaskSize : MaskSize;
        int hitCount = 0;
        float effectiveRange = falloff.z > 0 ? Mathf.Min(pulseRange, Mathf.Max(.01f, falloff.y)) : pulseRange;
        if (densityFalloff.w>.5f) effectiveRange=Mathf.Min(effectiveRange,densityFalloff.y);
        SampleRays(origin, effectiveRange, bank == 0 ? mainDirections : localDirections);
        hitCount = EncodePlanes(origin, sampleSize, blockers, slot * 6);
        origins[slot] = new Vector4(LastOrigin.x, LastOrigin.y, LastOrigin.z, Time.time);
        blockerRotations[slot] = new Vector4(1, 0, 0, 0);
        parameters[slot] = new Vector4(pulseRange, pulseSpeed, pulseAfterglow, 1);
        float fadeEnd = Mathf.Clamp(falloff.y, .01f, pulseRange);
        distanceFalloffs[slot] = new Vector4(Mathf.Clamp(falloff.x, 0, fadeEnd - .001f), fadeEnd,
            Mathf.Clamp(falloff.z, 0, 8), Mathf.Clamp01(falloff.w));
        densityFalloffs[slot] = densityFalloff;
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
    public bool CaptureBlockingPlanes(Vector3 origin, float range, Texture2DArray target, int firstSlice, bool highQuality = false)
    {
        if (!isActiveAndEnabled || blockers == null || target == null || target.width != MaskSize
            || target.height != MaskSize || firstSlice < 0 || firstSlice + 6 > target.depth) return false;
        using (syncMarker.Auto()) Physics.SyncTransforms();
        int size = highQuality ? MaskSize : FootstepMaskSize;
        SampleRays(origin, Mathf.Max(.01f, range), highQuality ? mainDirections : localDirections);
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
        pulseIndex = footstepIndex = dangerIndex = coinIndex = lightningIndex = 0;
        if (blockers != null) Publish();
    }
    void OnDisable()
    {
        if (activeField == this) activeField = null;
        Shader.SetGlobalFloat("_SonarActive", 0);
        Shader.SetGlobalTexture("_SonarBlockingPlanes", null);
        // SampleRays completes its batch synchronously, so no outstanding job can reference these.
        if (rayCommands.IsCreated) rayCommands.Dispose();
        if (rayHits.IsCreated) rayHits.Dispose();
        if (blockers != null) Destroy(blockers);
        if (blockerUpload != null) Destroy(blockerUpload);
        blockers = null;
        blockerUpload = null;
    }
}
