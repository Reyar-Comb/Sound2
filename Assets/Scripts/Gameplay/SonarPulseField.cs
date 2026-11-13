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
    const int MaxPulses = 4;
    readonly Vector4[] origins = new Vector4[MaxPulses];
    readonly Vector4[] parameters = new Vector4[MaxPulses];
    readonly Color[] planes = new Color[MaskSize * MaskSize];
    Texture2DArray blockers;
    int pulseIndex;

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
        Shader.SetGlobalVectorArray("_SonarOrigins", origins);
        Shader.SetGlobalVectorArray("_SonarParameters", parameters);
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
    {
        if (!isActiveAndEnabled || blockers == null) return false;
        pulseRange = Mathf.Max(1, pulseRange);
        pulseSpeed = Mathf.Max(.1f, pulseSpeed);
        pulseAfterglow = Mathf.Max(.05f, pulseAfterglow);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        LastOrigin = origin;
        Physics.SyncTransforms();
        int slot = pulseIndex++ % MaxPulses;
        int hitCount = 0;
        for (int face = 0; face < 6; face++)
        {
            for (int y = 0; y < MaskSize; y++) for (int x = 0; x < MaskSize; x++)
            {
                var direction = Direction(face, (x + .5f) * 2 / MaskSize - 1, (y + .5f) * 2 / MaskSize - 1);
                if (Physics.Raycast(LastOrigin, direction, out var hit, pulseRange, obstacleLayers, QueryTriggerInteraction.Ignore))
                {
                    // The first surface plane gates reachability only. Visible contours
                    // are detected and drawn entirely by BlackBoxScreenPoints.shader.
                    var normal = hit.normal;
                    planes[y * MaskSize + x] = new Color(normal.x, normal.y, normal.z,
                        Vector3.Dot(normal, hit.point - LastOrigin));
                    hitCount++;
                }
                else planes[y * MaskSize + x] = Color.clear;
            }
            blockers.SetPixels(planes, slot * 6 + face);
        }
        blockers.Apply(false);
        origins[slot] = new Vector4(LastOrigin.x, LastOrigin.y, LastOrigin.z, Time.time);
        parameters[slot] = new Vector4(pulseRange, pulseSpeed, pulseAfterglow, 1);
        Publish();
        Emissions++;
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
