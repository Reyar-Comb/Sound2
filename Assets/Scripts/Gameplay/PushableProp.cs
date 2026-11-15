using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SonarObjectEmitter), typeof(CollisionSonarEmitter))]
public sealed class PushableProp : MonoBehaviour
{
    [Header("Player pushing")]
    [Min(.1f)] public float maximumPushSpeed = 1.8f;
    [Min(1)] public float maximumPushForce = 140;
    [Min(.1f)] public float velocityResponse = 8;
    [Min(0)] public float stoppingResistance = 5;
    [Min(.02f)] public float contactMemory = .12f;

    [Header("Rolling / scraping noise")]
    public AudioClip movementClip;
    [Min(.05f)] public float movementInterval = .4f;
    [Min(.01f)] public float minimumSoundSpeed = .12f;
    [Min(.01f)] public float minimumSoundTravel = .06f;
    [Min(1)] public float movementPulseRange = 5;
    [Min(0)] public float movementHearingRange = 12;

    [Header("连续移动声")]
    [InspectorName("循环播放移动录音"), Tooltip("沿用地面移动检测，连续播放 Movement Clip。关闭时每次移动声波播放一次录音。")]
    public bool loopMovementAudio;
    [InspectorName("移动音量"), Range(0, 1)] public float movementVolume = .65f;
    [InspectorName("移动淡入时间"), Min(0)] public float movementFadeIn = .04f;
    [InspectorName("停止淡出时间"), Min(0)] public float movementFadeOut = .12f;
    [InspectorName("移动声空间设置")]
    public SpatialAudioSettings movementAudioSettings = new SpatialAudioSettings {
        distanceMode = SoundDistanceMode.Linear, minDistance = .6f, maxDistance = 12
    };
    [InspectorName("移动录音峰值数据"), Tooltip("使用同一条录音驱动连续声波。创建 Audio Reactive Clip 并烘焙后填入这里。")]
    public AudioReactiveClip movementAnalysis;
    [InspectorName("连续声波设置")]
    public AudioReactiveVisualSettings movementVisual = new AudioReactiveVisualSettings {
        range = 5, distance = new SonarDistanceSettings { enabled = true, fadeStartDistance = .6f, fadeEndDistance = 5, falloffPower = 1, pulseStrength = 1 }
    };

    public int MovementEmissions { get; private set; }
    public int PushRequests { get; private set; }
    public bool Grounded => Time.fixedTime - lastGroundContact <= Time.fixedDeltaTime * 3;

    Rigidbody body;
    SonarObjectEmitter sonar;
    Vector3 desiredVelocity, pushPoint, previousPosition;
    float lastPush = float.NegativeInfinity, lastGroundContact = float.NegativeInfinity;
    float nextMovementSound, travel;
    bool movingOnGround;
    float movementGain;
    SpatialAudioEmitter movementLoop;
    AudioReactiveSonarField.MovingLoop movementWave;
    AudioReactiveClip waveAnalysis;
    double movementDspStart, nextWaveAttempt;
    bool HasPeakWave => loopMovementAudio && movementAnalysis != null && movementAnalysis.IsReady;
    AudioClip LoopClip => movementAnalysis != null && movementAnalysis.clip != null ? movementAnalysis.clip : movementClip;

    void Awake() { body = GetComponent<Rigidbody>(); sonar = GetComponent<SonarObjectEmitter>(); }
    void OnEnable()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        previousPosition = body.position;
        lastPush = lastGroundContact = float.NegativeInfinity;
        travel = 0; nextMovementSound = Time.time;
    }

    void OnDisable()
    {
        movingOnGround = false;
        StopMovementLoop();
    }

    public void RequestPush(Vector3 velocity, Vector3 point, Vector3 normal)
    {
        if (!isActiveAndEnabled || body == null || body.isKinematic || SonicSpaceStartMenu.BlocksGameplay
            || Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > .5f) return;
        var inward = Vector3.ProjectOnPlane(-normal, Vector3.up).normalized;
        float speed = Mathf.Min(maximumPushSpeed, Vector3.Dot(velocity, inward));
        if (speed <= .01f) return;
        var request = Vector3.ProjectOnPlane(velocity, Vector3.up).normalized * speed;
        if (Time.time > lastPush || request.sqrMagnitude > desiredVelocity.sqrMagnitude)
        {
            desiredVelocity = request;
            pushPoint = point;
        }
        lastPush = Time.time; PushRequests++;
        body.WakeUp();
    }

    void FixedUpdate()
    {
        var displacement = Vector3.ProjectOnPlane(body.position - previousPosition, Vector3.up);
        previousPosition = body.position;
        if (body.isKinematic || SonicSpaceStartMenu.BlocksGameplay)
        { lastPush = float.NegativeInfinity; travel = 0; movingOnGround = false; return; }

        var horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (Grounded)
        {
            if (Time.time - lastPush <= contactMemory)
            {
                var force = Vector3.ClampMagnitude((desiredVelocity - horizontal) * (body.mass * velocityResponse), maximumPushForce);
                var point = pushPoint; point.y = body.worldCenterOfMass.y;
                body.AddForceAtPosition(force, point, ForceMode.Force);
            }
            else body.AddForce(-horizontal * stoppingResistance, ForceMode.Acceleration);
        }

        float distance = displacement.magnitude;
        float speed = distance / Mathf.Max(.001f, Time.fixedDeltaTime);
        movingOnGround = Grounded && speed >= minimumSoundSpeed
            && distance <= Mathf.Max(.5f, maximumPushSpeed * Time.fixedDeltaTime * 4);
        if (!movingOnGround)
        { travel = 0; return; }
        travel += distance;
        if (travel < minimumSoundTravel || Time.time < nextMovementSound) return;
        if (!HasPeakWave) sonar.EmitWithClip(movementPulseRange, loopMovementAudio ? null : movementClip);
        var origin = sonar.emissionOrigin != null ? sonar.emissionOrigin.position : body.worldCenterOfMass;
        GameplayNoise.Emit(origin, movementHearingRange, GameplayNoiseKind.Movement, this);
        MovementEmissions++;
        travel = 0; nextMovementSound = Time.time + movementInterval;
    }

    void Update()
    {
        var clip = LoopClip;
        if (!loopMovementAudio || clip == null)
        {
            if (movementGain > 0) StopMovementLoop();
            return;
        }
        if (AudioListener.pause) return;

        bool moving = movingOnGround && !body.isKinematic && Time.timeScale > 0 && !SonicSpaceStartMenu.BlocksGameplay;
        float target = moving && movementVolume > 0 ? 1 : 0;
        float fadeSeconds = target > movementGain ? movementFadeIn : movementFadeOut;
        movementGain = fadeSeconds > 0 ? Mathf.MoveTowards(movementGain, target, Time.unscaledDeltaTime / fadeSeconds) : target;
        if (movementGain <= 0)
        {
            if (movementLoop != null && movementLoop.gameObject.activeSelf) StopMovementLoop();
            return;
        }

        if (movementLoop == null)
        {
            var anchor = new GameObject("Movement Loop Audio");
            anchor.transform.SetParent(transform, false);
            movementLoop = anchor.AddComponent<SpatialAudioEmitter>();
        }
        Vector3 origin = sonar.emissionOrigin != null ? sonar.emissionOrigin.position : body.worldCenterOfMass;
        movementLoop.transform.position = origin;
        movementLoop.settings = movementAudioSettings;
        movementLoop.volume = movementGain * Mathf.Clamp01(movementVolume);
        bool starting = !movementLoop.gameObject.activeSelf || movementDspStart == 0;
        if (!movementLoop.gameObject.activeSelf) movementLoop.gameObject.SetActive(true);
        var source = movementLoop.Source;
        if (starting || source.clip != clip)
        {
            source.Stop();
            source.clip = clip;
            source.loop = true;
            movementLoop.Apply();
            movementDspStart = AudioSettings.dspTime + .035;
            source.PlayScheduled(movementDspStart);
            StopMovementWave();
        }

        UpdateMovementWave(origin);
    }

    void UpdateMovementWave(Vector3 origin)
    {
        if (!HasPeakWave)
        {
            if (movementWave != null) StopMovementWave();
            return;
        }
        if (waveAnalysis != null && waveAnalysis != movementAnalysis) StopMovementWave();
        if (movementWave == null || !movementWave.IsActive)
        {
            double now = AudioSettings.dspTime;
            if (!movingOnGround || now < nextWaveAttempt) return;
            nextWaveAttempt = now + .35;
            movementWave = AudioReactiveSonarField.GetOrCreate().StartMovingLoop(origin, movementAnalysis, movementVisual, movementDspStart, transform);
            waveAnalysis = movementAnalysis;
            if (movementWave == null) return;
        }
        movementWave.Update(origin, movementGain);
    }

    void StopMovementWave()
    {
        movementWave?.Stop();
        movementWave = null;
        waveAnalysis = null;
        nextWaveAttempt = 0;
    }

    void StopMovementLoop()
    {
        movementGain = 0;
        movementDspStart = 0;
        StopMovementWave();
        if (movementLoop == null) return;
        movementLoop.volume = 0;
        movementLoop.Source.Stop();
        if (movementLoop.gameObject.activeInHierarchy) movementLoop.gameObject.SetActive(false);
    }

    void OnCollisionEnter(Collision collision) => TrackGround(collision);
    void OnCollisionStay(Collision collision) => TrackGround(collision);
    void TrackGround(Collision collision)
    {
        if (!isActiveAndEnabled || collision.collider.GetComponentInParent<PlayerController>() != null) return;
        for (int i = 0; i < collision.contactCount; i++)
            if (Vector3.Dot(collision.GetContact(i).normal, Vector3.up) > .6f)
            { lastGroundContact = Time.fixedTime; return; }
    }
}
