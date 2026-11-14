using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Sound2.LevelEditing
{
    /// <summary>Runtime grid surfaces; the existing authored polygon editor is unchanged.</summary>
    public static class SeededFloorGeometry
    {
        public const float WallHeight = 3.2f;
        public static Mesh Build(SeededFloorPlan plan, float shaftDepth, bool includeCeiling = false)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                triangles.Add(i); triangles.Add(i+1); triangles.Add(i+2); triangles.Add(i); triangles.Add(i+2); triangles.Add(i+3);
            }
            void Box(Vector3 min, Vector3 max)
            {
                Vector3 a = new(min.x,min.y,min.z), b = new(max.x,min.y,min.z), c = new(max.x,min.y,max.z), d = new(min.x,min.y,max.z);
                Vector3 e = new(min.x,max.y,min.z), f = new(max.x,max.y,min.z), g = new(max.x,max.y,max.z), h = new(min.x,max.y,max.z);
                Quad(e,h,g,f); Quad(a,b,c,d); Quad(a,e,f,b); Quad(b,f,g,c); Quad(c,g,h,d); Quad(d,h,e,a);
            }
            foreach (var cell in plan.Cells)
            {
                // Subsequent floors receive the previous floor's shaft at local (0,0).
                // Keep that 2x2 aperture open; an outgoing floor pit still has a roof overhead.
                bool incomingShaft = plan.Floor > 0 && cell.x >= -1 && cell.x < 1 && cell.y >= -1 && cell.y < 1;
                if (includeCeiling && !incomingShaft)
                    Box(new(cell.x, WallHeight, cell.y), new(cell.x + 1, WallHeight + .25f, cell.y + 1));
                if (plan.IsPit(cell)) continue;
                float x = cell.x, z = cell.y;
                Quad(new(x,0,z), new(x,0,z+1), new(x+1,0,z+1), new(x+1,0,z));
                Quad(new(x,-.25f,z), new(x+1,-.25f,z), new(x+1,-.25f,z+1), new(x,-.25f,z+1));
                if (!plan.Contains(cell + Vector2Int.left)) Box(new(x-.2f,-.25f,z), new(x,WallHeight,z+1));
                if (!plan.Contains(cell + Vector2Int.right)) Box(new(x+1,-.25f,z), new(x+1.2f,WallHeight,z+1));
                if (!plan.Contains(cell + Vector2Int.down)) Box(new(x,-.25f,z-.2f), new(x+1,WallHeight,z));
                if (!plan.Contains(cell + Vector2Int.up)) Box(new(x,-.25f,z+1), new(x+1,WallHeight,z+1.2f));
            }
            float px = plan.Exit.x, pz = plan.Exit.y;
            Box(new(px-1.2f,-shaftDepth,pz-1.2f), new(px-1,0,pz+1.2f));
            Box(new(px+1,-shaftDepth,pz-1.2f), new(px+1.2f,0,pz+1.2f));
            Box(new(px-1,-shaftDepth,pz-1.2f), new(px+1,0,pz-1));
            Box(new(px-1,-shaftDepth,pz+1), new(px+1,0,pz+1.2f));
            var mesh = new Mesh { name = "Seeded F" + plan.Floor, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
