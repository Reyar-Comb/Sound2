using UnityEngine;

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
    public const int MainPulseCapacity = 4;
    public const int FootstepPulseCapacity = 4;
    public const int MaxPulses = MainPulseCapacity + FootstepPulseCapacity;
    const int FootstepMaskSize = 24;
    readonly Vector4[] origins = new Vector4[MaxPulses];
    readonly Vector4[] parameters = new Vector4[MaxPulses];
    readonly Vector4[] distanceFalloffs = new Vector4[MaxPulses];
    readonly Color[] planes = new Color[MaskSize * MaskSize];
    Texture2DArray blockers;
    int pulseIndex, footstepIndex;
    public int FootstepEmissions { get; private set; }
    public int LastRayCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetShaderState() => Shader.SetGlobalFloat("_SonarActive", 0);

    void OnEnable()
    {
        blockers = new Texture2DArray(MaskSize, MaskSize, MaxPulses * 6, TextureFormat.RGBAFloat, false, true)
        { name = "Sonar direct-path blocking planes", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        ClearPulses();
        Publish();
    }

    void Publish()
    {
        Shader.SetGlobalFloat("_SonarActive", 1);
        Shader.SetGlobalFloat("_SonarMaskSize", MaskSize);
        // New names avoid Unity retaining the old four-element global array capacity after a reload.
        Shader.SetGlobalVectorArray("_SonarPulseOrigins", origins);
        Shader.SetGlobalVectorArray("_SonarPulseParameters", parameters);
        Shader.SetGlobalVectorArray("_SonarPulseDistanceFalloffs", distanceFalloffs);
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
            distanceSettings != null ? distanceSettings.Pack(pulseRange) : new Vector4(0, pulseRange, 0, 1), false);

    // Compatibility entry point; a zero power preserves the original brightness.
    public bool EmitFrom(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow,
        float fadeStart, float fadeEnd, float falloffPower, float strength)
        => Emit(origin, pulseRange, pulseSpeed, pulseAfterglow,
            new Vector4(fadeStart, fadeEnd, falloffPower, strength), false);

    public bool EmitFootstepFrom(Vector3 origin, float range, float speed, float afterglow,
        SonarDistanceSettings distanceSettings)
        => Emit(origin, range, speed, afterglow,
            distanceSettings != null ? distanceSettings.Pack(range) : new Vector4(0, range, 0, 1), true);

    bool Emit(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow,
        Vector4 falloff, bool footstep)
    {
        if (!isActiveAndEnabled || blockers == null) return false;
        pulseRange = Mathf.Max(1, pulseRange);
        pulseSpeed = Mathf.Max(.1f, pulseSpeed);
        pulseAfterglow = Mathf.Max(.05f, pulseAfterglow);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        LastOrigin = origin;
        Physics.SyncTransforms();
        int slot = footstep ? MainPulseCapacity + footstepIndex : pulseIndex;
        if (footstep) footstepIndex = (footstepIndex + 1) % FootstepPulseCapacity;
        else pulseIndex = (pulseIndex + 1) % MainPulseCapacity;
        int sampleSize = footstep ? FootstepMaskSize : MaskSize;
        int blockSize = MaskSize / sampleSize;
        int hitCount = 0;
        float effectiveRange = falloff.z > 0 ? Mathf.Min(pulseRange, Mathf.Max(.01f, falloff.y)) : pulseRange;
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
            blockers.SetPixels(planes, slot * 6 + face);
        }
        blockers.Apply(false);
        origins[slot] = new Vector4(LastOrigin.x, LastOrigin.y, LastOrigin.z, Time.time);
        parameters[slot] = new Vector4(pulseRange, pulseSpeed, pulseAfterglow, 1);
        float fadeEnd = Mathf.Clamp(falloff.y, .01f, pulseRange);
        distanceFalloffs[slot] = new Vector4(Mathf.Clamp(falloff.x, 0, fadeEnd - .001f), fadeEnd,
            Mathf.Clamp(falloff.z, 0, 8), Mathf.Clamp01(falloff.w));
        Publish();
        Emissions++;
        if (footstep) FootstepEmissions++;
        LastRayCount = 6 * sampleSize * sampleSize;
        LastHitCount = hitCount;
        timer.Stop(); LastSamplingMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
        return true;
    }

    public void ClearPulses()
    {
        for (int i = 0; i < MaxPulses; i++) parameters[i] = Vector4.zero;
        if (blockers != null) Publish();
    }
    void OnDisable()
    {
        Shader.SetGlobalFloat("_SonarActive", 0);
        Shader.SetGlobalTexture("_SonarBlockingPlanes", null);
        if (blockers != null) Destroy(blockers);
        blockers = null;
    }
}
