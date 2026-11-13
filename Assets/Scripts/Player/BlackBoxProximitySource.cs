using UnityEngine;

/// <summary>Feeds the player's eye position to the black-box wall shader.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class BlackBoxProximitySource : MonoBehaviour
{
    public enum CenterMode { Gaze = 0, WallProjection = 1, Midpoint = 2 }

    [SerializeField, Tooltip("Use the player camera for an eye-height wall projection.")]
    private Transform trackingPoint;
    public enum PointStyle { Face, Edge, Stars, Twinkle }
    [SerializeField] private bool facePoints = true;
    [SerializeField] private bool edgePoints = true;
    [SerializeField] private bool stars = true;
    [SerializeField] private bool twinkle = true;
    [SerializeField, Min(0.1f)] private float gazeDistance = 40f;
    [SerializeField] private LayerMask obstacleLayers = ~0;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[32];
    public bool HasGazeHit { get; private set; }
    public Vector3 GazePoint { get; private set; }
    public Vector3 GazeNormal { get; private set; }
    public Vector3 WallProjectionPoint { get; private set; }
    public Vector3 SelectedCenter => WallProjectionPoint;
    public CenterMode Mode
    {
        get => CenterMode.WallProjection;
        set { PublishPosition(); } // Retain API compatibility; face centre is locked.
    }

    public bool IsStyleEnabled(PointStyle style) => style == PointStyle.Face ? facePoints :
        style == PointStyle.Edge ? edgePoints : style == PointStyle.Stars ? stars : twinkle;

    public void SetStyleEnabled(PointStyle style, bool enabled)
    {
        switch (style)
        {
            case PointStyle.Face: facePoints = enabled; break;
            case PointStyle.Edge: edgePoints = enabled; break;
            case PointStyle.Stars: stars = enabled; break;
            case PointStyle.Twinkle: twinkle = enabled; break;
        }
        PublishPosition();
    }

    private static readonly int PositionId = Shader.PropertyToID("_BlackBoxPlayerPosition");
    private static readonly int ActiveId = Shader.PropertyToID("_BlackBoxPlayerActive");
    private static readonly int HitId = Shader.PropertyToID("_BlackBoxGazeHit");
    private static readonly int NormalId = Shader.PropertyToID("_BlackBoxGazeNormal");
    private static readonly int ModeId = Shader.PropertyToID("_BlackBoxCenterMode");
    private static readonly int StylesId = Shader.PropertyToID("_BlackBoxStyleVisibility");
    private static BlackBoxProximitySource activeSource;

    public Transform TrackingPoint
    {
        get => trackingPoint;
        set { trackingPoint = value; PublishPosition(); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShaderState()
    {
        activeSource = null;
        Shader.SetGlobalFloat(ActiveId, 0f);
        Shader.SetGlobalVector(HitId, Vector4.zero);
        Shader.SetGlobalFloat(ModeId, (float)CenterMode.WallProjection);
        Shader.SetGlobalVector(StylesId, Vector4.one);
    }

    private void OnEnable() => PublishPosition();
    private void LateUpdate() => PublishPosition();

    public void PublishPosition()
    {
        if (!isActiveAndEnabled) return;
        activeSource = this;
        Vector3 position = trackingPoint != null ? trackingPoint.position : transform.position;
        Transform eye = trackingPoint != null ? trackingPoint : transform;
        var ray = new Ray(position, eye.forward);
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, gazeDistance, obstacleLayers, QueryTriggerInteraction.Ignore);
        // Saturation is rare; retain the nearest non-player hit even in a crowded scene.
        RaycastHit[] hits = count == hitBuffer.Length
            ? Physics.RaycastAll(ray, gazeDistance, obstacleLayers, QueryTriggerInteraction.Ignore) : hitBuffer;
        if (hits != hitBuffer) count = hits.Length;
        HasGazeHit = false;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(transform)
                || hits[i].distance >= nearest) continue;
            nearest = hits[i].distance;
            HasGazeHit = true;
            GazePoint = hits[i].point;
            GazeNormal = hits[i].normal;
        }
        WallProjectionPoint = HasGazeHit
            ? position - GazeNormal * Vector3.Dot(position - GazePoint, GazeNormal) : position;
        Shader.SetGlobalVector(HitId, new Vector4(GazePoint.x, GazePoint.y, GazePoint.z, HasGazeHit ? 1f : 0f));
        Shader.SetGlobalVector(NormalId, GazeNormal);
        Shader.SetGlobalFloat(ModeId, (float)CenterMode.WallProjection);
        Shader.SetGlobalVector(StylesId, new Vector4(facePoints ? 1 : 0, edgePoints ? 1 : 0,
            stars ? 1 : 0, twinkle ? 1 : 0));
        Shader.SetGlobalVector(PositionId, new Vector4(position.x, position.y, position.z, 1f));
        Shader.SetGlobalFloat(ActiveId, 1f);
    }

    private void OnDisable()
    {
        if (activeSource != this) return;
        Shader.SetGlobalFloat(ActiveId, 0f);
        Shader.SetGlobalVector(HitId, Vector4.zero);
        activeSource = null;
    }
}
