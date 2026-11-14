using UnityEngine;

public enum SonarPulseKind { Main, Footstep, Danger, Coin, Lightning }

/// <summary>Shared spherical arrival/occlusion data for BlackBox rendering. No input or audio ownership.</summary>
[DisallowMultipleComponent]
public sealed class SonarPulseField : MonoBehaviour
{
    public LayerMask obstacleLayers = Physics.DefaultRaycastLayers;
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
    readonly Vector4[] origins = new Vector4[MaxPulses];
    readonly Vector4[] parameters = new Vector4[MaxPulses];
    readonly Vector4[] distanceFalloffs = new Vector4[MaxPulses];
    readonly Vector4[] densityFalloffs = new Vector4[MaxPulses];
    readonly Color[] planes = new Color[MaskSize * MaskSize];
    Texture2DArray blockers;
    Texture2D blockerUpload;
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
        var timer = System.Diagnostics.Stopwatch.StartNew();
        LastOrigin = origin;
        Physics.SyncTransforms();
        bool footstep = bank == 1, danger = bank == 2;
        int slot = bank == 4 ? SelectSlot(LightningPulseOffset, LightningPulseCapacity, ref lightningIndex)
            : bank == 3 ? SelectSlot(CoinPulseOffset, CoinPulseCapacity, ref coinIndex)
            : danger ? SelectSlot(DangerPulseOffset, DangerPulseCapacity, ref dangerIndex)
            : footstep ? SelectSlot(FootstepPulseOffset, FootstepPulseCapacity, ref footstepIndex)
            : SelectSlot(0, MainPulseCapacity, ref pulseIndex);
        int sampleSize = bank != 0 ? FootstepMaskSize : MaskSize;
        int blockSize = MaskSize / sampleSize;
        int hitCount = 0;
        float effectiveRange = falloff.z > 0 ? Mathf.Min(pulseRange, Mathf.Max(.01f, falloff.y)) : pulseRange;
        if (densityFalloff.w>.5f) effectiveRange=Mathf.Min(effectiveRange,densityFalloff.y);
        for (int face = 0; face < 6; face++)
        {
            for (int y = 0; y < sampleSize; y++) for (int x = 0; x < sampleSize; x++)
            {
                var direction = Direction(face, (x + .5f) * 2 / sampleSize - 1, (y + .5f) * 2 / sampleSize - 1);
                Color plane;
                if (Physics.Raycast(LastOrigin, direction, out var hit, effectiveRange, obstacleLayers, QueryTriggerInteraction.Ignore))
                {
                    // The first surface plane gates reachability only. Visible contours
                    // are detected and drawn entirely by BlackBoxScreenPoints.shader.
                    var normal = hit.normal;
                    plane = new Color(normal.x, normal.y, normal.z,
                        Vector3.Dot(normal, hit.point - LastOrigin));
                    hitCount++;
                }
                else plane = Color.clear;
                // Footsteps use a coarser angular grid in the same texture layout.
                for (int by = 0; by < blockSize; by++) for (int bx = 0; bx < blockSize; bx++)
                    planes[(y * blockSize + by) * MaskSize + x * blockSize + bx] = plane;
            }
            // Upload only this wave's six faces, not the entire expanded pulse pool.
            blockerUpload.SetPixels(planes);
            blockerUpload.Apply(false);
            Graphics.CopyTexture(blockerUpload, 0, 0, blockers, slot * 6 + face, 0);
        }
        origins[slot] = new Vector4(LastOrigin.x, LastOrigin.y, LastOrigin.z, Time.time);
        parameters[slot] = new Vector4(pulseRange, pulseSpeed, pulseAfterglow, 1);
        float fadeEnd = Mathf.Clamp(falloff.y, .01f, pulseRange);
        distanceFalloffs[slot] = new Vector4(Mathf.Clamp(falloff.x, 0, fadeEnd - .001f), fadeEnd,
            Mathf.Clamp(falloff.z, 0, 8), Mathf.Clamp01(falloff.w));
        densityFalloffs[slot] = densityFalloff;
        Publish();
        Emissions++;
        if (footstep) FootstepEmissions++;
        if (danger) DangerEmissions++;
        LastRayCount = 6 * sampleSize * sampleSize;
        LastHitCount = hitCount;
        timer.Stop(); LastSamplingMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
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
        if (blockers != null) Destroy(blockers);
        if (blockerUpload != null) Destroy(blockerUpload);
        blockers = null;
        blockerUpload = null;
    }
}
