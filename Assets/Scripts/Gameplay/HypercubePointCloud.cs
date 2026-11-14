using System.Collections.Generic;
using UnityEngine;

/// <summary>A rotating 4D tesseract projected into 3D, drawn by the project's red point passes.</summary>
[DisallowMultipleComponent]
public sealed class HypercubePointCloud : MonoBehaviour
{
    public const int VertexCount = 16, EdgeCount = 32, InteriorPointsPerEdge = 20;
    public const int PointCount = VertexCount + EdgeCount * InteriorPointsPerEdge;
    public static readonly List<HypercubePointCloud> Instances = new();
    [Min(.1f)] public float radius = 1.48f;
    [Min(1)] public float loopSeconds = 16;
    [Range(0, 360)] public float phaseDegrees;
    readonly Vector3[] vertices = new Vector3[VertexCount];
    const float ProjectionDistance = 3.5f;

    public Bounds WorldBounds
    {
        get
        {
            var s = transform.lossyScale;
            float extent = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) * Mathf.Max(.1f, radius);
            return new Bounds(transform.position, Vector3.one * (extent * 2 + .1f));
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInstances() => Instances.Clear();
    void OnEnable() { if (!Instances.Contains(this)) Instances.Add(this); }
    void OnDisable() => Instances.Remove(this);

    public void EvaluateVertices(float seconds, Vector3[] output)
    {
        // A 4D corner has norm 2. This projection bound keeps every rotation inside radius.
        float scale = Mathf.Max(.1f,radius) * Mathf.Sqrt(ProjectionDistance*ProjectionDistance-4) / (2*ProjectionDistance);
        float angle = (Mathf.Repeat(seconds / Mathf.Max(1,loopSeconds),1)*360 + phaseDegrees)*Mathf.Deg2Rad;
        float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
        float c2 = Mathf.Cos(angle*2), s2 = Mathf.Sin(angle*2);
        for (int i=0;i<VertexCount;i++)
        {
            float x=(i&1)==0?-1:1, y=(i&2)==0?-1:1, z=(i&4)==0?-1:1, w=(i&8)==0?-1:1;
            float rotatedX=x*c-w*s, rotatedW=x*s+w*c;
            float rotatedY=y*c2-z*s2, rotatedZ=y*s2+z*c2;
            float perspective=ProjectionDistance/(ProjectionDistance-rotatedW);
            output[i]=transform.TransformPoint(new Vector3(rotatedX,rotatedY,rotatedZ)*(scale*perspective));
        }
    }
    public int Collect(Vector4[] output)
    {
        if (!isActiveAndEnabled || output==null || output.Length<PointCount) return 0;
        EvaluateVertices(Time.time,vertices);
        int count=0;
        void Add(Vector3 p,float brightness) => output[count++]=new Vector4(p.x,p.y,p.z,brightness);
        foreach(var p in vertices) Add(p,1);
        // Hamming-distance-one pairs are exactly the 32 edges of a four-dimensional cube.
        for(int i=0;i<VertexCount;i++) for(int axis=0;axis<4;axis++)
        {
            int other=i^(1<<axis); if(other<i) continue;
            for(int k=1;k<=InteriorPointsPerEdge;k++)
                Add(Vector3.Lerp(vertices[i],vertices[other],k/(float)(InteriorPointsPerEdge+1)),.9f);
        }
        return count;
    }
    void OnDrawGizmos()
    {
        if(Application.isPlaying) return;
        EvaluateVertices(0,vertices); Gizmos.color=new Color(1,.06f,.03f);
        for(int i=0;i<VertexCount;i++) for(int axis=0;axis<4;axis++)
        {
            int other=i^(1<<axis); if(other>i) Gizmos.DrawLine(vertices[i],vertices[other]);
        }
    }
}
