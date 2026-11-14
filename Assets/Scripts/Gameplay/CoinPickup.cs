using UnityEngine;

/// <summary>Floating animated reward; its own timed pulse briefly reveals the solid coin.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(SonarObjectEmitter))]
public sealed class CoinPickup : MonoBehaviour
{
    public PlayerController player;
    public Transform visual;
    public Renderer body;
    [Min(1)] public int value=1;
    [Min(.05f)] public float pickupRadius=.65f;
    [Min(.05f)] public float bodyRevealSeconds=.55f;
    [Min(0)] public float bobAmplitude=.12f;
    [Min(.1f)] public float bobPeriod=3.2f;
    public float spinDegreesPerSecond=48;
    public float phaseOffset;
    public bool Collected { get; private set; }
    public bool BodyVisible => body!=null && body.enabled;
    SonarObjectEmitter pulse;
    Vector3 basePosition;Quaternion baseRotation;
    float visibleUntil=-1;
    void Awake()
    {
        pulse=GetComponent<SonarObjectEmitter>();
        if(visual!=null) {basePosition=visual.localPosition;baseRotation=visual.localRotation;}
    }
    void OnEnable() { if(pulse==null)pulse=GetComponent<SonarObjectEmitter>();pulse.Emitted+=Reveal;Hide(); }
    void OnDisable() { if(pulse!=null)pulse.Emitted-=Reveal;Hide(); }
    void Start() { if(player==null)player=FindAnyObjectByType<PlayerController>(); }
    void Reveal() { if(!Collected) {visibleUntil=Time.time+bodyRevealSeconds;if(body!=null)body.enabled=true;} }
    void Hide() {visibleUntil=-1;if(body!=null)body.enabled=false;}
    void Update()
    {
        if(Collected) return;
        if(visual!=null)
        {
            float phase=(Time.time/Mathf.Max(.1f,bobPeriod)+phaseOffset)*Mathf.PI*2;
            visual.localPosition=basePosition+Vector3.up*(Mathf.Sin(phase)*bobAmplitude);
            visual.localRotation=Quaternion.AngleAxis(Time.time*spinDegreesPerSecond,Vector3.up)*baseRotation;
        }
        if(Time.time>=visibleUntil && body!=null)body.enabled=false;
        if(player!=null && player.TryGetComponent<PlayerWallet>(out var wallet))TryCollect(wallet);
    }
    public bool TryCollect(PlayerWallet wallet)
    {
        if(Collected || wallet==null || !wallet.isActiveAndEnabled
            || !wallet.TryGetComponent<CharacterController>(out var capsule) || !capsule.enabled) return false;
        Vector3 center=visual!=null?visual.position:transform.position;
        Vector3 nearest=capsule.ClosestPoint(center);
        if(Vector3.Distance(nearest,center)>Mathf.Max(.05f,pickupRadius)) return false;
        // Do not collect through a thin wall merely because the proximity spheres overlap.
        Vector3 delta=center-capsule.bounds.center;
        foreach(var hit in Physics.RaycastAll(capsule.bounds.center,delta.normalized,delta.magnitude,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(wallet.transform)) return false;
        Collected=true;
        if(!wallet.TryCredit(Mathf.Max(1,value),"Coin pickup")){Collected=false;return false;}
        pulse.enabled=false;Hide();Destroy(gameObject);return true;
    }
}
