using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Feeds spherical arrival/fade and blocking data to the existing BlackBox renderer.
/// This component does not generate or draw any points, surfaces or lines.</summary>
[RequireComponent(typeof(Camera), typeof(BlackBoxProximitySource))]
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
    [Header("Existing BlackBox rendering")]
    public Material renderingSettings;
    [Tooltip("Shared face and contour dot radius in pixels at 720p. Diameter is twice this value; scales with resolution. Capped by the tighter grid spacing.")]
    [Range(.25f, 4f)] public float dotRadius = 1.5f;
    public bool facePoints = true;
    public bool edgePoints = true;
    [Header("Independent reveal modes (keys 3 / 4)")]
    [Tooltip("Continuously reveal nearby surfaces using the original BlackBox distance rules.")]
    public bool proximityRendering;
    [Tooltip("Reveal surfaces when player or object pulses reach them. Does not mute emission sounds.")]
    public bool sonarRendering = true;
    public SonarPulseField PulseField { get; private set; }
    public int Emissions => PulseField != null ? PulseField.Emissions : 0;
    public int LastHitCount => PulseField != null ? PulseField.LastHitCount : 0;
    public float LastSamplingMilliseconds => PulseField != null ? PulseField.LastSamplingMilliseconds : 0;
    public Vector3 LastOrigin => PulseField != null ? PulseField.LastOrigin : Vector3.zero;

    void OnEnable()
    {
        player = GetComponentInParent<PlayerController>();
        if (player == null)
        {
            Debug.LogError("SphericalSonar must be attached to the Player prefab's camera.", this);
            enabled = false;
            return;
        }
        PulseField = GetComponent<SonarPulseField>();
        if (PulseField == null) PulseField = gameObject.AddComponent<SonarPulseField>();
        PulseField.enabled = true;
        PulseField.obstacleLayers = obstacleLayers;
        var style = GetComponent<BlackBoxProximitySource>();
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Face, facePoints);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Edge, edgePoints);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars, false);
        style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Twinkle, false);
        style.SetRevealModes(proximityRendering, sonarRendering);
        ClearPulses();
    }
    PlayerController player;
    AudioSource source;
    SonarCueSource cue;
    bool ownsCue;
    float nextEmission;
    bool hud = true;
    static readonly Rect HudRect = new Rect(16, 16, 480, 232);
    bool InputCaptured => player != null && player.isActiveAndEnabled && !player.InputSuspended;

    public void SetRevealModes(bool proximity, bool sonar)
    {
        proximityRendering = proximity;
        sonarRendering = sonar;
        GetComponent<BlackBoxProximitySource>().SetRevealModes(proximity, sonar);
    }

    void Start()
    {
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
        style.SetRevealModes(proximityRendering, sonarRendering);
        var k = Keyboard.current;
        var m = Mouse.current;
        if (k != null)
        {
            if (k.escapeKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame) Capture(false);
            if (k.fKey.wasPressedThisFrame) automatic = !automatic;
            if (k.f1Key.wasPressedThisFrame) hud = !hud;
            if (k.digit1Key.wasPressedThisFrame) facePoints = !facePoints;
            if (k.digit2Key.wasPressedThisFrame) edgePoints = !edgePoints;
            if (k.digit3Key.wasPressedThisFrame) SetRevealModes(!proximityRendering, sonarRendering);
            if (k.digit4Key.wasPressedThisFrame) SetRevealModes(proximityRendering, !sonarRendering);
            if (k.rKey.wasPressedThisFrame) ClearPulses();
            if (k.enterKey.wasPressedThisFrame) Emit();
        }
        if (automatic && Time.time >= nextEmission) Emit();
        if (m == null) return;
        var mousePosition = m.position.ReadValue();
        bool overHud = hud && HudRect.Contains(new Vector2(mousePosition.x, Screen.height - mousePosition.y));
        if (m.leftButton.wasPressedThisFrame && (InputCaptured || !overHud))
        {
            if (!InputCaptured) Capture(true);
            Emit();
        }
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
    void Capture(bool active) { if (player != null) player.SetInputCaptured(active); }
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
        GUILayout.BeginArea(HudRect, GUI.skin.box);
        GUILayout.Label("SPHERICAL SONAR  |  BlackBox dots");
        GUILayout.Label("Click / Enter: pulse   WASD: move   Mouse: look");
        GUILayout.Label("Space / Shift: up / down   Esc / Tab: release cursor");
        GUILayout.Label("1: faces   2: edges   C: cube   F: auto   R: clear   F1: HUD");
        GUILayout.BeginHorizontal();
        bool proximity = GUILayout.Toggle(proximityRendering, "3  Proximity reveal");
        bool sonar = GUILayout.Toggle(sonarRendering, "4  Sonar reveal");
        GUILayout.EndHorizontal();
        if (proximity != proximityRendering || sonar != sonarRendering) SetRevealModes(proximity, sonar);
        GUILayout.Label($"Range {range:0} m   Speed {waveSpeed:0} m/s   Afterglow {afterglow:0.0} s");
        GUILayout.Label($"Auto {(automatic ? "ON" : "OFF")}   Pulses {Emissions}   Sample {LastSamplingMilliseconds:0.0} ms");
        if (GUILayout.Button("Emit spherical pulse")) Emit();
        GUILayout.EndArea();
    }
}
