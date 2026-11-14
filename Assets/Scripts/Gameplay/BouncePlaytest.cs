using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BouncePlaytest : MonoBehaviour
{
    public AudioClip impactClip;
    public Material ballMaterial;
    public PhysicsMaterial bounceMaterial;
    public float launchSpeed = 12f;
    public float lifetime = 12f;
    public bool gravity;
    public bool spatialize = true;
    public bool visuals = true;
    public bool reverb = true;
    public bool simulateOcclusion;
    [Header("Five gameplay switches (keys 1-5)")]
    public bool ringShot;
    public bool bounceCountOnly;
    public bool enhancedAttenuation;
    public bool flightSine;
    public bool sandShot;
    [Min(1)] public int maxBounces = 1;
    [Range(8, 48)] public int ringBallCount = 24;
    [Range(8, 48)] public int sandBallCount = 32;
    [Range(10, 140)] public float fanAngle = 70;
    [Range(0, 40)] public float fanVerticalAngle = 16;
    [Range(0.5f, 6)] public float attenuationDbPerMeter = 3;
    [Range(110, 880)] public int sineFrequency = 440;
    [Header("Impact sound only: X = metres, Y = gain (0 to 1)")]
    [Tooltip("Only collision sounds use this curve when switch 3 is on. Flight tones and the fixed hum are independent.")]
    public AnimationCurve impactDistanceCurve = CreateDefaultImpactCurve();
    public HallHumEmitter hallHum;
    const int MaxBalls = 96;
    const int ImpactVoiceCount = 32;
    public int ActiveBalls
    {
        get
        {
            int count = 0;
            foreach (var ball in balls) if (ball != null && ball.GetComponent<BounceProbe>().Active) count++;
            return count;
        }
    }
    public int LastVolleyCount { get; private set; }
    public Vector2 movementMin = new Vector2(-4.4f, -4.4f);
    public Vector2 movementMax = new Vector2(4.4f, 4.4f);
    [Range(0, 2)] public float reverbLevel = 0.65f;
    public int Shots { get; private set; }
    public int Impacts { get; private set; }
    public int Sounds { get; private set; }
    public Vector3 LastImpact { get; private set; }
    public float LastSoundDistance { get; private set; }

    readonly List<GameObject> balls = new List<GameObject>();
    Camera view;
    CharacterController walker;
    int visibleLayers;
    float yaw, pitch, nextShot;
    bool captured;
    public bool InputCaptured => captured;
    [SerializeField] bool hud;
    string directionLabel = "";
    Coroutine directionTest;
    AudioClip sineClip;
    ImpactSoundPool impactPool;
    readonly SpatialAudioSettings flightSettings = new SpatialAudioSettings();
    Rect ControlPanel => new Rect(12, 12, Mathf.Min(650, Screen.width - 24), hallHum != null ? 480 : 370);

    bool PointerOverPanel()
    {
        if (!hud || captured || Mouse.current == null) return false;
        var point = Mouse.current.position.ReadValue();
        return ControlPanel.Contains(new Vector2(point.x, Screen.height - point.y));
    }

    void Start()
    {
        view = GetComponent<Camera>();
        walker = GetComponent<CharacterController>();
        visibleLayers = view.cullingMask;
        yaw = transform.eulerAngles.y;
        pitch = Mathf.DeltaAngle(0, transform.eulerAngles.x);
        impactPool = GetComponent<ImpactSoundPool>();
        if (impactPool == null) impactPool = gameObject.AddComponent<ImpactSoundPool>();
        impactPool.capacity = ImpactVoiceCount;
        impactPool.listener = transform;
        impactPool.tailSeconds = SteamAudio.SteamAudioSettings.Singleton.realTimeDuration + .25f;
        RefreshAudioSettings();
        impactPool.Initialize();
        CreateSineClip();
        if (impactClip == null) Debug.LogError("Assign an impact clip to BouncePlaytest.");
    }

    void Update()
    {
        balls.RemoveAll(ball => ball == null);
        RefreshAudioSettings();
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard != null)
        {
            if (keyboard.f1Key.wasPressedThisFrame) hud = !hud;
            if (keyboard.digit1Key.wasPressedThisFrame) SetSwitch(1, !ringShot);
            if (keyboard.digit2Key.wasPressedThisFrame) SetSwitch(2, !bounceCountOnly);
            if (keyboard.digit3Key.wasPressedThisFrame) SetSwitch(3, !enhancedAttenuation);
            if (keyboard.digit4Key.wasPressedThisFrame) SetSwitch(4, !flightSine);
            if (keyboard.digit5Key.wasPressedThisFrame) SetSwitch(5, !sandShot);
            if (keyboard.escapeKey.wasPressedThisFrame) Capture(false);
            if (keyboard.rKey.wasPressedThisFrame) ClearBalls();
            if (keyboard.vKey.wasPressedThisFrame) SetVisuals(!visuals);
            if (keyboard.pKey.wasPressedThisFrame) SetSpatialization(!spatialize);
            if (keyboard.mKey.wasPressedThisFrame) SetReverb(!reverb);
            if (keyboard.yKey.wasPressedThisFrame) TestSingleImpact();
            if (keyboard.gKey.wasPressedThisFrame)
            {
                gravity = !gravity;
                foreach (var ball in balls) ball.GetComponent<Rigidbody>().useGravity = gravity;
            }
            if (keyboard.tKey.wasPressedThisFrame)
            {
                ClearBalls();
                if (directionTest != null) StopCoroutine(directionTest);
                directionTest = StartCoroutine(TestDirections());
            }
            if (captured)
            {
                var move = new Vector3(
                    (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0), 0,
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                move = Quaternion.Euler(0, yaw, 0) * Vector3.ClampMagnitude(move, 1);
                MovePlayer(move * (3f * Time.deltaTime));
                if (keyboard.spaceKey.wasPressedThisFrame) Fire();
            }
        }
        PlayerUiPointMask.SetLegacyPanel(this, ControlPanel, hud);
        if (mouse == null) return;
        if (mouse.leftButton.wasPressedThisFrame && !PointerOverPanel())
        {
            if (!captured) Capture(true);
            else Fire();
        }
        if (!captured) return;
        var delta = mouse.delta.ReadValue();
        yaw += delta.x * 0.12f;
        pitch = Mathf.Clamp(pitch - delta.y * 0.12f, -80, 80);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0);
    }

    public void SetSwitch(int index, bool enabled)
    {
        switch (index)
        {
            case 1: ringShot = enabled; if (enabled) sandShot = false; break;
            case 2: bounceCountOnly = enabled; break;
            case 3: enhancedAttenuation = enabled; break;
            case 4: flightSine = enabled; break;
            case 5: sandShot = enabled; if (enabled) ringShot = false; break;
        }
    }

    public void Fire()
    {
        if (Time.time < nextShot) return;
        balls.RemoveAll(ball => ball == null);
        int requested = ringShot ? Mathf.Clamp(ringBallCount, 8, 48) : sandShot ? Mathf.Clamp(sandBallCount, 8, 48) : 1;
        int count = Mathf.Min(requested, MaxBalls - balls.Count);
        LastVolleyCount = 0;
        if (count <= 0) return;
        nextShot = Time.time + 0.25f;
        // Keep a volley from becoming much louder merely because it has more sources.
        float soundScale = 1f / Mathf.Sqrt(count);
        for (int i = 0; i < count; i++)
        {
            Vector3 direction;
            if (ringShot)
                direction = Quaternion.Euler(0, yaw + i * 360f / count, 0) * Vector3.forward;
            else if (sandShot)
            {
                // A thin forward fan: wide horizontally, shallow vertically.
                float horizontal = Random.Range(-fanAngle * 0.5f, fanAngle * 0.5f);
                float vertical = Random.Range(-fanVerticalAngle * 0.5f, fanVerticalAngle * 0.5f);
                direction = transform.rotation * Quaternion.Euler(vertical, horizontal, 0) * Vector3.forward;
            }
            else direction = transform.forward;
            if (SpawnBall(direction.normalized, soundScale, sandShot ? Random.Range(0.85f, 1.15f) : 1))
                LastVolleyCount++;
        }
    }

    bool SpawnBall(Vector3 direction, float soundScale, float speedScale)
    {
        float radius = sandShot ? 0.045f : 0.12f;
        var origin = transform.position;
        // Projectiles use Ignore Raycast so siblings do not block each other's launch tests.
        if (Physics.CheckSphere(origin, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
            Physics.SphereCast(origin, radius, direction, out _, 0.4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Sound Probe " + (++Shots);
        ball.layer = 2;
        ball.transform.position = origin + direction * 0.4f;
        ball.transform.localScale = Vector3.one * (radius * 2);
        ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;
        var collider = ball.GetComponent<SphereCollider>();
        collider.sharedMaterial = bounceMaterial;
        foreach (var other in balls)
            if (other != null) Physics.IgnoreCollision(collider, other.GetComponent<Collider>());
        if (walker != null) Physics.IgnoreCollision(collider, walker);
        var body = ball.AddComponent<Rigidbody>();
        body.mass = 0.2f;
        body.useGravity = gravity;
        body.linearDamping = 0;
        body.angularDamping = 0;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = direction * launchSpeed * speedScale;
        var probe = ball.AddComponent<BounceProbe>();
        probe.owner = this;
        probe.bounceLimit = flightSine ? 1 : bounceCountOnly ? Mathf.Max(1, maxBounces) : 0;
        probe.soundScale = soundScale;
        if (flightSine)
        {
            if (sineClip == null || sineClip.name != "Flight sine " + sineFrequency) CreateSineClip();
            probe.flightEmitter = ball.AddComponent<SpatialAudioEmitter>();
            probe.flightEmitter.settings = flightSettings;
            probe.flightEmitter.listener = transform;
            probe.flightEmitter.volume = 0;
            probe.flightVoice.clip = sineClip;
            probe.flightVoice.loop = true;
            probe.flightVoice.volume = 0;
            // Different start phases reduce coherent summing in a large volley.
            probe.flightVoice.timeSamples = Random.Range(0, sineClip.samples);
            probe.flightVoice.Play();
        }
        balls.Add(ball);
        // Count-only and flight modes have no time based expiration.
        if (!bounceCountOnly && !flightSine) Destroy(ball, Mathf.Max(0.05f, lifetime));
        return true;
    }

    void CreateSineClip()
    {
        sineFrequency = Mathf.Clamp(sineFrequency, 110, 880);
        var previous = sineClip;
        sineClip = ProceduralAudioClips.CreateLoopingSine(sineFrequency);
        // Existing flight voices may still reference an earlier frequency.
        if (previous != null) retiredSineClips.Add(previous);
    }

    readonly List<AudioClip> retiredSineClips = new List<AudioClip>();

    void RefreshAudioSettings()
    {
        ConfigureSettings(flightSettings, false);
        if (impactPool == null) return;
        impactPool.clip = impactClip;
        ConfigureSettings(impactPool.settings, true);
    }

    void ConfigureSettings(SpatialAudioSettings settings, bool impact)
    {
        settings.hrtf = spatialize;
        settings.occlusion = simulateOcclusion;
        settings.reflections = impact;
        settings.reflectionMix = reverb ? reverbLevel : 0;
        settings.distanceMode = !enhancedAttenuation ? SoundDistanceMode.Physics :
            impact && impactDistanceCurve != null && impactDistanceCurve.length > 0
                ? SoundDistanceMode.Curve : SoundDistanceMode.DecibelsPerMetre;
        settings.distanceCurve = impactDistanceCurve;
        settings.decibelsPerMetre = Mathf.Max(.5f, attenuationDbPerMeter);
    }

    public static AnimationCurve CreateDefaultImpactCurve()
    {
        var points = new[] { new Keyframe(0, 1), new Keyframe(0.5f, 1), new Keyframe(1, 0.8f),
            new Keyframe(3, 0.35f), new Keyframe(6, 0.12f), new Keyframe(12, 0.02f), new Keyframe(20, 0) };
        // Linear segments are predictable and do not overshoot the edited gain range.
        for (int i = 0; i < points.Length; i++)
        {
            if (i > 0) points[i].inTangent = (points[i].value - points[i - 1].value) / (points[i].time - points[i - 1].time);
            if (i + 1 < points.Length) points[i].outTangent = (points[i + 1].value - points[i].value) / (points[i + 1].time - points[i].time);
        }
        return new AnimationCurve(points) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
    }

    public float ImpactDistanceGain(float distance)
    {
        if (!enhancedAttenuation) return 1 / Mathf.Max(1, distance);
        if (impactDistanceCurve == null || impactDistanceCurve.length == 0) return DistanceGain(distance);
        // Distance is in world metres; clamp to the endpoints regardless of editor wrap mode.
        return SpatialAudioSettings.EvaluateCurve(impactDistanceCurve, distance);
    }

    public float DistanceGain(float distance)
    {
        return enhancedAttenuation
            ? Mathf.Pow(10, -Mathf.Max(0.5f, attenuationDbPerMeter) * Mathf.Max(0, distance - 1) / 20)
            : 1f / Mathf.Max(1, distance);
    }

    public void MovePlayer(Vector3 displacement)
    {
        var position = transform.position + displacement;
        position.x = Mathf.Clamp(position.x, movementMin.x, movementMax.x);
        position.z = Mathf.Clamp(position.z, movementMin.y, movementMax.y);
        if (walker != null) walker.Move(position - transform.position);
        else transform.position = position;
    }

    public void Impact(Vector3 position, float speed, float soundScale = 1)
    {
        Impacts++;
        LastImpact = position;
        if (speed < 0.5f) return;
        PlayImpact(position, Mathf.Lerp(0.12f, 0.8f, Mathf.InverseLerp(0.5f, 12, speed)) * soundScale);
    }

    void PlayImpact(Vector3 position, float volume)
    {
        if (impactPool == null) return;
        RefreshAudioSettings();
        if (!impactPool.PlayAt(position, volume)) return;
        LastSoundDistance = impactPool.LastDistance;
        Sounds++;
    }

    public void SetSpatialization(bool enabled)
    {
        spatialize = enabled;
        RefreshAudioSettings();
    }

    public void SetReverb(bool enabled)
    {
        reverb = enabled;
        RefreshAudioSettings();
    }

    public void TestSingleImpact()
    {
        ClearBalls();
        var origin = transform.position;
        var direction = Quaternion.Euler(0, transform.eulerAngles.y, 0) * Vector3.forward;
        float distance = 2;
        if (Physics.Raycast(origin, direction, out var hit, distance))
            distance = Mathf.Max(0.1f, hit.distance - 0.05f);
        PlayImpact(origin + direction * distance, 0.7f);
    }

    public void SetVisuals(bool enabled)
    {
        visuals = enabled;
        view.cullingMask = enabled ? visibleLayers : 0;
    }

    public void ClearBalls()
    {
        if (directionTest != null)
        {
            StopCoroutine(directionTest);
            directionTest = null;
            directionLabel = "";
        }
        foreach (var ball in balls) if (ball != null) Destroy(ball);
        balls.Clear();
        if (impactPool != null) impactPool.StopAll();
    }

    IEnumerator TestDirections()
    {
        var origin = transform.position;
        var rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        var directions = new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back, Vector3.up, Vector3.down };
        var labels = new[] { "LEFT", "RIGHT", "FRONT", "BACK", "UP", "DOWN" };
        for (int i = 0; i < directions.Length; i++)
        {
            directionLabel = labels[i];
            var delta = rotation * directions[i];
            float distance = 2;
            if (Physics.Raycast(origin, delta, out var hit, 2)) distance = Mathf.Max(0.1f, hit.distance - 0.05f);
            PlayImpact(origin + delta * distance, 0.7f);
            yield return new WaitForSeconds(2.2f);
        }
        directionLabel = "";
        directionTest = null;
    }

    void Capture(bool enabled)
    {
        captured = enabled;
        Cursor.lockState = enabled ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !enabled;
    }

    void OnApplicationFocus(bool focus) { if (!focus) Capture(false); }
    void OnDisable() { PlayerUiPointMask.SetLegacyPanel(this, ControlPanel, false); Capture(false); }
    void OnDestroy()
    {
        foreach (var ball in balls) if (ball != null) Destroy(ball);
        if (sineClip != null) Destroy(sineClip);
        foreach (var clip in retiredSineClips) if (clip != null) Destroy(clip);
    }

    void OnGUI()
    {
        if (!hud) return;
        var previousFont = GUI.skin.font;
        if (GameUiTheme.Current != null && GameUiTheme.Current.legacyFont != null) GUI.skin.font = GameUiTheme.Current.legacyFont;
        GUI.enabled = !captured;
        var panel = ControlPanel;
        GUI.Box(panel, "SPATIAL AUDIO / FIVE SWITCHES");
        float x = panel.x + 12, width = panel.width - 24;
        GUI.Label(new Rect(x, 37, width, 22), "Click / Space: fire | WASD: move | Esc: release mouse | R: clear");
        string[] labels = {
            "1  Ring shot: 360 degrees / " + ringBallCount + " balls",
            "2  Bounce count only: no lifetime timer",
            "3  Impact distance curve: editable on Main Camera",
            "4  Flight sine: " + sineFrequency + " Hz / disappear on first impact",
            "5  Sand fan: " + sandBallCount + " balls / " + fanAngle.ToString("F0") + " degree spread"
        };
        bool[] values = { ringShot, bounceCountOnly, enhancedAttenuation, flightSine, sandShot };
        for (int i = 0; i < labels.Length; i++)
        {
            bool value = GUI.Toggle(new Rect(x, 64 + i * 28, width, 25), values[i], labels[i]);
            if (value != values[i]) SetSwitch(i + 1, value);
        }
        GUI.Label(new Rect(x, 207, 165, 24), "Bounce limit: " + maxBounces);
        if (GUI.Button(new Rect(x + 165, 207, 35, 24), "-")) maxBounces = Mathf.Max(1, maxBounces - 1);
        if (GUI.Button(new Rect(x + 205, 207, 35, 24), "+")) maxBounces = Mathf.Min(20, maxBounces + 1);
        GUI.Label(new Rect(x + 252, 207, width - 252, 24), "Shot settings apply to new balls");
        GUI.Label(new Rect(x, 236, width, 22), "Impact gain at 1 / 6 / 12m: " + ImpactDistanceGain(1).ToString("P1") + " / " + ImpactDistanceGain(6).ToString("P1") + " / " + ImpactDistanceGain(12).ToString("P1"));
        GUI.Label(new Rect(x, 260, width, 22), "Balls: " + ActiveBalls + " / " + MaxBalls + " | Last volley: " + LastVolleyCount + " | Impacts: " + Impacts + " | Sounds: " + Sounds);
        GUI.Label(new Rect(x, 284, width, 22), "M: reverb " + (reverb ? "ON" : "OFF") + " | P: HRTF " + (spatialize ? "ON" : "OFF") + " | V: visuals | G: gravity | Y: knock | T: directions");
        if (GUI.Button(new Rect(x, 316, 135, 30), "Fire / test volley")) Fire();
        if (GUI.Button(new Rect(x + 145, 316, 100, 30), "Clear balls")) ClearBalls();
        if (GUI.Button(new Rect(x + 255, 316, 140, 30), "Capture mouse")) Capture(true);
        if (hallHum != null)
        {
            hallHum.humEnabled = GUI.Toggle(new Rect(x, 352, 220, 24), hallHum.humEnabled, "H  Fixed center hum / amplitude LFO");
            GUI.Label(new Rect(x + 252, 352, width - 252, 24), "Hum: dry / gentle falloff / ear focus");
            GUI.Label(new Rect(x, 381, 92, 22), "Hum volume");
            hallHum.volume = GUI.HorizontalSlider(new Rect(x + 95, 387, 170, 18), hallHum.volume, 0, 1);
            GUI.Label(new Rect(x + 280, 381, 60, 22), hallHum.volume.ToString("P0"));
            GUI.Label(new Rect(x, 410, 92, 22), "Tone " + hallHum.carrierHz.ToString("F0") + " Hz");
            hallHum.carrierHz = GUI.HorizontalSlider(new Rect(x + 95, 416, 170, 18), hallHum.carrierHz, 50, 220);
            GUI.Label(new Rect(x + 285, 410, 105, 22), "LFO " + hallHum.lfoHz.ToString("F1") + " Hz");
            hallHum.lfoHz = GUI.HorizontalSlider(new Rect(x + 395, 416, Mathf.Max(50, width - 400), 18), hallHum.lfoHz, 0.1f, 3);
            GUI.Label(new Rect(x, 439, 92, 22), "LFO depth");
            hallHum.lfoDepth = GUI.HorizontalSlider(new Rect(x + 95, 445, 170, 18), hallHum.lfoDepth, 0, 1);
#if UNITY_EDITOR
            if (GUI.Button(new Rect(x + 285, 437, 250, 26), "Edit IMPACT curve in Inspector"))
            {
                UnityEditor.Selection.activeGameObject = gameObject;
                UnityEditor.EditorGUIUtility.PingObject(this);
            }
#endif
        }
        GUI.enabled = true;
        GUI.skin.font = previousFont;
    }
}
