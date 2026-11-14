using System.Collections.Generic;
using UnityEngine;

/// <summary>World-anchored branching traces on real surfaces; no geometry or collision is added.</summary>
[DisallowMultipleComponent]
public sealed class MonsterVines : MonoBehaviour
{
    public const int Capacity = 768;
    public static readonly List<MonsterVines> Instances = new();
    [Min(.1f)] public float growthInterval = .32f;
    [Min(.5f)] public float lifetime = 2.8f;
    [Min(.5f)] public float reach = 2.8f;
    public LayerMask surfaces = Physics.DefaultRaycastLayers;
    struct Point { public Vector3 position; public float born; }
    readonly List<Point> points = new(Capacity);
    readonly RaycastHit[] hits = new RaycastHit[16];
    PatrolMonster monster;
    Vector3 previous, heading, targetHeading;
    float nextGrowth;
    int sequence;
    public int PointCount => points.Count;
    public Vector3 TravelDirection => heading;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInstances() => Instances.Clear();
    void OnEnable()
    {
        monster = GetComponent<PatrolMonster>(); previous = transform.position;
        heading = targetHeading = transform.forward; nextGrowth = Time.time; points.Clear();
        if (!Instances.Contains(this)) Instances.Add(this);
    }
    void OnDisable() { Instances.Remove(this); points.Clear(); }
    void LateUpdate()
    {
        if (monster != null && !monster.isActiveAndEnabled) { points.Clear(); return; }
        if (Time.deltaTime <= 0) return;
        Vector3 movement = transform.position - previous; previous = transform.position;
        movement.y = 0;
        if (movement.sqrMagnitude > 9) { points.Clear(); nextGrowth = Time.time; }
        else if (movement.sqrMagnitude > .00001f)
            targetHeading = movement.normalized;
        heading = Vector3.Slerp(heading, targetHeading, 1-Mathf.Exp(-Time.deltaTime*9)).normalized;
        points.RemoveAll(p => Time.time-p.born > lifetime || (p.position-transform.position).sqrMagnitude > 36);
        if (Time.time < nextGrowth) return;
        nextGrowth = Time.time + Mathf.Max(.1f, growthInterval); sequence++;
        Vector3 side = Vector3.Cross(Vector3.up, heading);
        Vector3 origin = transform.position + Vector3.up*.8f + side*(Mathf.Sin(sequence*2.4f)*.35f);
        if (SurfaceRay(origin, Vector3.down, 2, out var floor))
            Grow(floor, Quaternion.AngleAxis((sequence%3-1)*45, Vector3.up)*heading, 28, Time.time, true);
        // Adjacent walls also sprout towards the actor's travel, with an upward bias.
        Vector3 wallDirection = EightWay(Quaternion.AngleAxis((sequence%3-1)*45, Vector3.up)*heading,Vector3.up);
        if (SurfaceRay(transform.position+Vector3.up*(.35f+(sequence%4)*.28f), wallDirection, 1.7f, out var wall)
            && Mathf.Abs(wall.normal.y)<.4f)
            Grow(wall, Vector3.ProjectOnPlane(heading,wall.normal)+Vector3.up*.65f, 23, Time.time+.1f, true);
    }

    // Stable surface axes: X/Z on floors, horizontal/up on walls; never camera-relative.
    public static Vector3 EightWay(Vector3 direction, Vector3 normal)
    {
        normal.Normalize();
        Vector3 u = Vector3.ProjectOnPlane(Vector3.right,normal);
        if(u.sqrMagnitude<.01f)u=Vector3.ProjectOnPlane(Vector3.forward,normal);
        u.Normalize();Vector3 v=Vector3.Cross(normal,u).normalized;
        float angle=Mathf.Atan2(Vector3.Dot(direction,v),Vector3.Dot(direction,u));
        float snapped=Mathf.Round(angle/(Mathf.PI*.25f))*(Mathf.PI*.25f);
        return (u*Mathf.Cos(snapped)+v*Mathf.Sin(snapped)).normalized;
    }
    bool SurfaceRay(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest=default; float best=float.PositiveInfinity;
        int count=Physics.RaycastNonAlloc(origin,direction,hits,distance,surfaces,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
        {
            var hit=hits[i];
            if(hit.collider.attachedRigidbody!=null || hit.collider is CharacterController || hit.transform.IsChildOf(transform) || hit.distance>=best)continue;
            best=hit.distance;nearest=hit;
        }
        return best<float.PositiveInfinity;
    }
    void Grow(RaycastHit surface, Vector3 direction, int steps, float born, bool branch)
    {
        Vector3 p=surface.point,normal=surface.normal;
        direction=EightWay(direction,normal);
        float step=Mathf.Max(.04f,reach/28);
        for(int i=0;i<steps;i++)
        {
            if(points.Count>=Capacity)points.RemoveAt(0);
            points.Add(new Point {position=p+normal*.025f,born=born+i*.027f});
            // Straight runs with only 45/90 degree turns keep the trace on the eight-way surface grid.
            if(i==14)direction=EightWay(Quaternion.AngleAxis(sequence%2==0?45:-45,normal)*direction,normal);
            if(branch && (i==9 || i==18))
                Grow(surface,Quaternion.AngleAxis(i==9?90:-90,normal)*direction,10,born+i*.027f,false);
            // Turn onto a wall when the advancing tip meets it. Stop at unsupported edges.
            if(SurfaceRay(p+normal*.06f,direction,step+.03f,out var ahead))
            {
                direction=EightWay(normal+direction*.3f,ahead.normal);
                surface=ahead; p=ahead.point; normal=ahead.normal;
            }
            else if(SurfaceRay(p+direction*step+normal*.2f,-normal,.32f,out var next))
            {
                surface=next;p=next.point;normal=next.normal;
                direction=EightWay(direction,normal);
            }
            else break;
        }
    }
    public int Collect(Vector4[] output)
    {
        if(monster!=null && !monster.isActiveAndEnabled)return 0;
        int count=0;
        foreach(var point in points)
        {
            float age=Time.time-point.born;if(age<=0)continue;
            Vector3 relative=point.position-transform.position;
            float tail=Mathf.Max(0,-Vector3.Dot(relative,heading));
            float alpha=Mathf.SmoothStep(0,1,age/.18f)*(1-Mathf.SmoothStep(0,1,(age-lifetime*.45f)/(lifetime*.55f)));
            alpha*=1-Mathf.SmoothStep(0,1,(tail-.6f)/2.4f);
            alpha*=1-Mathf.SmoothStep(0,1,(relative.magnitude-3.3f)/1.5f);
            if(alpha<.005f)continue;
            output[count++]=new Vector4(point.position.x,point.position.y,point.position.z,alpha*.9f);
            if(count>=output.Length)break;
        }
        return count;
    }
}
