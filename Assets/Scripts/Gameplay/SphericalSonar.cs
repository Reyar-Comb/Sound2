using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Feeds spherical arrival/fade and blocking data to the existing BlackBox renderer.
/// This component does not generate or draw any points, surfaces or lines.</summary>
[RequireComponent(typeof(Camera), typeof(CharacterController), typeof(BlackBoxProximitySource))]
public sealed class SphericalSonar : MonoBehaviour
{
    [Header("Spherical pulse")]
    [Min(1)] public float range = 12;
    [Min(.1f)] public float waveSpeed = 8;
    [Min(.05f)] public float afterglow = 1.4f;
    [Min(.1f)] public float emissionInterval = .8f;
    public LayerMask obstacleLayers = Physics.DefaultRaycastLayers;
    public bool automatic;
    public bool emissionSound = true;
    public float moveSpeed = 3;
    [Header("Existing BlackBox rendering")]
    public Material renderingSettings;
    public bool facePoints = true;
    public bool edgePoints = true;
    public SonarPulseField PulseField { get; private set; }
    public int Emissions => PulseField != null ? PulseField.Emissions : 0;
    public int LastHitCount => PulseField != null ? PulseField.LastHitCount : 0;
    public float LastSamplingMilliseconds => PulseField != null ? PulseField.LastSamplingMilliseconds : 0;
    public Vector3 LastOrigin => PulseField != null ? PulseField.LastOrigin : Vector3.zero;

    void OnEnable()
    {
        PulseField = GetComponent<SonarPulseField>();
        if (PulseField == null) PulseField = gameObject.AddComponent<SonarPulseField>();
        PulseField.enabled = true;
        PulseField.obstacleLayers = obstacleLayers;
        var style = GetComponent<BlackBoxProximitySource>();
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Face, facePoints);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Edge, edgePoints);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars, false);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Twinkle, false);
        ClearPulses();
    }
    CharacterController walker;
    AudioSource source;
    SonarCueSource cue;
    bool ownsCue;
    float yaw, pitch, nextEmission;
    bool captured;
    bool hud = true;

    void Start()
    {
        walker = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;
        pitch = Mathf.DeltaAngle(0, transform.eulerAngles.x);
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0; // Player-generated emission cue; not a simulated returning echo.
        source.volume = .16f;
        cue = GetComponent<SonarCueSource>();
        ownsCue = cue == null;
        if (ownsCue) cue = gameObject.AddComponent<SonarCueSource>();
        cue.output = source;
    }

    void Update()
    {
        var style = GetComponent<BlackBoxProximitySource>();
        if (style.IsStyleEnabled(BlackBoxProximitySource.PointStyle.Face) != facePoints)
            style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Face, facePoints);
        if (style.IsStyleEnabled(BlackBoxProximitySource.PointStyle.Edge) != edgePoints)
            style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Edge, edgePoints);
        var k = Keyboard.current;
        var m = Mouse.current;
        if (k != null)
        {
            if (k.escapeKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame) Capture(false);
            if (k.fKey.wasPressedThisFrame) automatic = !automatic;
            if (k.f1Key.wasPressedThisFrame) hud = !hud;
            if (k.digit1Key.wasPressedThisFrame) facePoints = !facePoints;
            if (k.digit2Key.wasPressedThisFrame) edgePoints = !edgePoints;
            if (k.rKey.wasPressedThisFrame) ClearPulses();
            if (k.spaceKey.wasPressedThisFrame) Emit();
            if (captured)
            {
                var move = new Vector3((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), 0,
                    (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
                walker.Move((Quaternion.Euler(0, yaw, 0) * Vector3.ClampMagnitude(move, 1) * moveSpeed
                    + Vector3.down * 2) * Time.deltaTime);
            }
        }
        if (automatic && Time.time >= nextEmission) Emit();
        if (m == null) return;
        if (m.leftButton.wasPressedThisFrame && (captured || m.position.ReadValue().y < Screen.height - 180))
        {
            if (!captured) Capture(true);
            Emit();
        }
        if (!captured) return;
        var delta = m.delta.ReadValue();
        yaw += delta.x * .12f;
        pitch = Mathf.Clamp(pitch - delta.y * .12f, -85, 85);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0);
    }

    public bool Emit()
    {
        if (Time.time < nextEmission || PulseField == null) return false;
        range = Mathf.Max(1, range); waveSpeed = Mathf.Max(.1f, waveSpeed);
        afterglow = Mathf.Max(.05f, afterglow);
        bool emitted = EmitFrom(transform.position, range, waveSpeed, afterglow);
        if (!emitted) return false;
        if (emissionSound && cue != null) cue.Play();
        nextEmission = Time.time + Mathf.Max(.1f, emissionInterval);
        return true;
    }

    public bool EmitFrom(Vector3 origin, float pulseRange, float pulseSpeed, float pulseAfterglow)
    {
        if (!isActiveAndEnabled || PulseField == null) return false;
        PulseField.obstacleLayers = obstacleLayers;
        return PulseField.EmitFrom(origin, pulseRange, pulseSpeed, pulseAfterglow);
    }

    public void ClearPulses() { if (PulseField != null) PulseField.ClearPulses(); }
    void Capture(bool active) { captured = active; Cursor.lockState = active ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !active; }
    void OnApplicationFocus(bool focused) { if (!focused) Capture(false); }
    void OnDisable()
    {
        Capture(false);
        if (PulseField != null) PulseField.enabled = false;
    }
    void OnDestroy()
    {
        if (ownsCue && cue != null) Destroy(cue);
        if (source != null) Destroy(source);
    }

    void OnGUI()
    {
        if (!hud) return;
        GUILayout.BeginArea(new Rect(16, 16, 440, 166), GUI.skin.box);
        GUILayout.Label("SPHERICAL SONAR  |  BlackBox dots");
        GUILayout.Label("Click / Space: pulse   WASD: move   Mouse: look");
        GUILayout.Label("1: faces   2: edges   C: cube   F: auto   R: clear   F1: HUD");
        GUILayout.Label($"Range {range:0} m   Speed {waveSpeed:0} m/s   Afterglow {afterglow:0.0} s");
        GUILayout.Label($"Auto {(automatic ? "ON" : "OFF")}   Pulses {Emissions}   Sample {LastSamplingMilliseconds:0.0} ms");
        if (GUILayout.Button("Emit spherical pulse")) Emit();
        GUILayout.EndArea();
    }
}
