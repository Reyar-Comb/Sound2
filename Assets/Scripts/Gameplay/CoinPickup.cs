using System.Collections.Generic;
using UnityEngine;

/// <summary>Physical coin prop with an outward travelling white point wave. No collection/economy.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(SonarObjectEmitter))]
public sealed class CoinPickup : MonoBehaviour
{
    public Transform visual;
    public Renderer body;
    [Min(.05f)] public float bodyRevealSeconds=.55f;
    [Range(.01f,.5f)] public float pointDensity=.16f;
    [Range(.2f,.85f)] public float waveTravelFraction=.65f;
    [Min(0)] public float bobAmplitude=.12f;
    [Min(.1f)] public float bobPeriod=3.2f;
    public float spinDegreesPerSecond=48;
    public float phaseOffset;
    public bool BodyVisible => body!=null && body.enabled;
    SonarObjectEmitter pulse;
    PickupItem item;
    bool handled;
    Vector3 basePosition;Quaternion baseRotation;
    float visibleUntil=-1;
    float revealStarted=-1;
    public const int SurfaceCapacity=128;
    static readonly List<CoinPickup> activeCoins=new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSurfaces()=>activeCoins.Clear();

    // Snapshot per camera. Normalized cylinder space keeps the wave attached while coins bob/spin.
    public static int CollectSurfaceWaves(Camera camera,Matrix4x4[] spaces,Vector4[] waves)
    {
        int count=0;
        var planes=GeometryUtility.CalculateFrustumPlanes(camera);
        foreach(var coin in activeCoins)
        {
            if(coin==null || !coin.BodyVisible || Time.time>=coin.visibleUntil
                || (camera.cullingMask & (1<<coin.body.gameObject.layer))==0
                || !GeometryUtility.TestPlanesAABB(planes,coin.body.bounds))continue;
            var bounds=coin.body.localBounds;
            var e=bounds.extents;
            spaces[count]=Matrix4x4.Scale(new Vector3(1/Mathf.Max(.001f,e.x),1/Mathf.Max(.001f,e.y),1/Mathf.Max(.001f,e.z)))
                * Matrix4x4.Translate(-bounds.center)*coin.body.transform.worldToLocalMatrix;
            float duration=Mathf.Max(.05f,coin.bodyRevealSeconds);
            waves[count]=new Vector4(coin.revealStarted,duration,Mathf.Clamp01(coin.pointDensity),Mathf.Clamp(coin.waveTravelFraction,.2f,.85f));
            if(++count>=Mathf.Min(SurfaceCapacity,spaces.Length))break;
        }
        return count;
    }
    void Awake()
    {
        pulse=GetComponent<SonarObjectEmitter>();
        item=GetComponent<PickupItem>();
        if(visual!=null) {basePosition=visual.localPosition;baseRotation=visual.localRotation;}
    }
    void OnEnable() { if(pulse==null)pulse=GetComponent<SonarObjectEmitter>();pulse.Emitted+=Reveal;activeCoins.Add(this);Hide(); }
    void OnDisable() { if(pulse!=null)pulse.Emitted-=Reveal;activeCoins.Remove(this);Hide(); }
    void Reveal() {revealStarted=Time.time;visibleUntil=Time.time+Mathf.Max(.05f,bodyRevealSeconds);if(body!=null)body.enabled=true;}
    void Hide() {visibleUntil=-1;if(body!=null)body.enabled=false;}
    void Update()
    {
        if(item!=null && item.IsHeld)handled=true;
        if(visual!=null && !handled)
        {
            float phase=(Time.time/Mathf.Max(.1f,bobPeriod)+phaseOffset)*Mathf.PI*2;
            visual.localPosition=basePosition+Vector3.up*(Mathf.Sin(phase)*bobAmplitude);
            visual.localRotation=Quaternion.AngleAxis(Time.time*spinDegreesPerSecond,Vector3.up)*baseRotation;
        }
        else if(visual!=null){visual.localPosition=basePosition;visual.localRotation=baseRotation;}
        if(Time.time>=visibleUntil)
        {
            if(item!=null && item.IsHeld)Reveal();
            else if(body!=null)body.enabled=false;
        }
    }
}
