using UnityEngine;

/// <summary>Feeds the player's eye position and style visibility to the point-cloud shader.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class BlackBoxProximitySource : MonoBehaviour
{
    [SerializeField, Tooltip("Use the player camera for an eye-height wall projection.")]
    private Transform trackingPoint;
    public enum PointStyle { Face, Edge, Stars, Twinkle }
    [SerializeField] private bool facePoints = true;
    [SerializeField] private bool edgePoints = true;
    [SerializeField] private bool stars = true;
    [SerializeField] private bool twinkle = true;

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

    [Header("Reveal modes")]
    [SerializeField] private bool proximityRendering = true;
    [SerializeField] private bool sonarRendering = true;
    public bool ProximityRendering => proximityRendering;
    public bool SonarRendering => sonarRendering;

    public void SetRevealModes(bool proximity, bool sonar)
    {
        proximityRendering = proximity;
        sonarRendering = sonar;
        PublishPosition();
    }

    private static readonly int RevealModesId = Shader.PropertyToID("_BlackBoxRevealModes");
    private static readonly int PositionId = Shader.PropertyToID("_BlackBoxPlayerPosition");
    private static readonly int ActiveId = Shader.PropertyToID("_BlackBoxPlayerActive");
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
        Shader.SetGlobalVector(StylesId, Vector4.one);
        Shader.SetGlobalVector(RevealModesId, new Vector4(1, 1, 0, 0));
    }

    private void OnEnable() => PublishPosition();
    private void LateUpdate() => PublishPosition();

    public void PublishPosition()
    {
        if (!isActiveAndEnabled) return;
        activeSource = this;
        Vector3 position = trackingPoint != null ? trackingPoint.position : transform.position;
        Shader.SetGlobalVector(StylesId, new Vector4(facePoints ? 1 : 0, edgePoints ? 1 : 0,
            stars ? 1 : 0, twinkle ? 1 : 0));
        Shader.SetGlobalVector(RevealModesId, new Vector4(proximityRendering ? 1 : 0, sonarRendering ? 1 : 0, 0, 0));
        Shader.SetGlobalVector(PositionId, new Vector4(position.x, position.y, position.z, 1f));
        Shader.SetGlobalFloat(ActiveId, 1f);
    }

    private void OnDisable()
    {
        if (activeSource != this) return;
        Shader.SetGlobalFloat(ActiveId, 0f);
        activeSource = null;
    }
}
