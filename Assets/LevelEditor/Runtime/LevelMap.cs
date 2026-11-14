using System.Collections.Generic;
using UnityEngine;
namespace Sound2.LevelEditing
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class LevelMap : MonoBehaviour
    {
        public FloorProgressionProfile profile;
        public Material geometryMaterial, resourceMaterial;
        [Min(8)] public float floorSpacing = 12;
        [Range(0,100)] public int growthLimit = 24;
        public bool generateOnPlay = true;
        readonly SortedDictionary<int, SeededFloor> floors = new();
        public IEnumerable<SeededFloor> Floors => floors.Values;
        Material ownedMaterial;
        Mesh resourceMesh;
        void Awake()
        {
            if (!Application.isPlaying || !generateOnPlay) return;
            ClearGenerated(); EnsureFloor(0); EnsureFloor(1);
            foreach (var player in FindObjectsByType<PlayerController>())
                if (player.gameObject.scene == gameObject.scene)
                {
                    var capsule = player.GetComponent<CharacterController>();
                    bool enabled = capsule.enabled; capsule.enabled = false;
                    player.transform.position = FindFloor(0).transform.TransformPoint(new Vector3(0,1.1f,0));
                    capsule.enabled = enabled;
                }
        }
        public SeededFloor FindFloor(int index) => floors.TryGetValue(index, out var floor) ? floor : null;
        public SeededFloor EnsureFloor(int index)
        {
            if (floors.TryGetValue(index, out var existing) && existing != null) return existing;
            Vector3 position = transform.position;
            if (index > 0)
            {
                var previous = FindFloor(index-1);
                if (previous == null) throw new System.InvalidOperationException("Generate the preceding floor first.");
                position = previous.transform.TransformPoint(new Vector3(previous.Plan.Exit.x,-Mathf.Max(8,floorSpacing),previous.Plan.Exit.y));
            }
            return Generate(index, position);
        }
        public SeededFloor Preview(int floor)
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Preview is edit-mode only.");
            ClearGenerated();
            var result = Generate(Mathf.Max(0,floor), transform.position);
            result.gameObject.hideFlags = HideFlags.DontSave;
            return result;
        }
        SeededFloor Generate(int index, Vector3 position)
        {
            var plan = SeededFloorPlan.Generate(index,growthLimit);
            var root = new GameObject("Generated F"+index,typeof(SeededFloor),typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
            root.transform.SetParent(transform,false); root.transform.position = position;
            // Preserve the seeded layout, but orient it so loaded floors' outgoing shafts
            // never share a vertical column. The incoming shaft still lands at local zero.
            for (int turn = 0; turn < 4; turn++)
            {
                root.transform.rotation = transform.rotation * Quaternion.Euler(0, turn * 90, 0);
                Vector3 candidate = root.transform.TransformPoint(new Vector3(plan.Exit.x, 0, plan.Exit.y));
                bool overlaps = false;
                foreach (var previous in floors.Values)
                {
                    if (previous == null || previous.Exit == null) continue;
                    Vector3 delta = candidate - previous.Exit.transform.position;
                    if (new Vector2(delta.x, delta.z).sqrMagnitude < 36) { overlaps = true; break; }
                }
                if (!overlaps) break;
            }
            var floor = root.GetComponent<SeededFloor>(); floor.floorIndex = index; floor.Plan = plan;
            float depth = Mathf.Max(8,floorSpacing)-SeededFloorGeometry.WallHeight;
            floor.Geometry = SeededFloorGeometry.Build(plan,depth);
            root.GetComponent<MeshFilter>().sharedMesh = floor.Geometry; root.GetComponent<MeshCollider>().sharedMesh = floor.Geometry;
            // The shaft arrives at local (0,0). A solid landing slab avoids relying on
            // four coplanar mesh triangles meeting exactly under a fast-falling capsule.
            var landing = new GameObject("Arrival landing support");
            landing.transform.SetParent(root.transform, false);
            var support = landing.AddComponent<BoxCollider>();
            support.center = new Vector3(0, -.25f, 0);
            support.size = new Vector3(6, .5f, 6);
            if (geometryMaterial == null)
            {
                if (ownedMaterial == null) ownedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color=Color.black };
                root.GetComponent<MeshRenderer>().sharedMaterial = ownedMaterial;
            }
            else root.GetComponent<MeshRenderer>().sharedMaterial = geometryMaterial;
            var exit = new GameObject("Shaft to F"+(index+1),typeof(FloorShaftTransition));
            exit.transform.SetParent(root.transform,false); exit.transform.localPosition = new Vector3(plan.Exit.x,-depth,plan.Exit.y);
            floor.Exit = exit.GetComponent<FloorShaftTransition>(); floor.Exit.sourceFloor = index;
            floor.Exit.destinationFloor = index+1; floor.Exit.width = 2;
            floors[index] = floor;
            if (FindFloor(index-1) != null) FindFloor(index-1).Exit.destination = floor;
            for (int i=0;i<plan.Resources.Count;i++)
            {
                if (resourceMesh == null) resourceMesh = CreateResourceMesh();
                var item = new GameObject("可搬运棱晶 F"+index+"-"+i, typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
                item.GetComponent<MeshFilter>().sharedMesh = resourceMesh;
                item.GetComponent<BoxCollider>().size = Vector3.one;
                item.transform.SetParent(root.transform,false);
                item.transform.localPosition = new Vector3(plan.Resources[i].x,.6f,plan.Resources[i].y);
                item.transform.localScale = new Vector3(.45f, .75f, .45f);
                item.GetComponent<MeshRenderer>().sharedMaterial = resourceMaterial != null ? resourceMaterial : root.GetComponent<MeshRenderer>().sharedMaterial;
                item.AddComponent<MapResource>();
                var physics=item.GetComponent<Rigidbody>();physics.mass=.4f;physics.interpolation=RigidbodyInterpolation.Interpolate;
                physics.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            }
            return floor;
        }
        static Mesh CreateResourceMesh()
        {
            var mesh = new Mesh { name = "Resource crystal (octahedron)" };
            mesh.vertices = new[] { Vector3.up * .5f, Vector3.down * .5f,
                Vector3.right * .5f, Vector3.forward * .5f, Vector3.left * .5f, Vector3.back * .5f };
            mesh.triangles = new[] { 0,3,2, 0,4,3, 0,5,4, 0,2,5,
                1,2,3, 1,3,4, 1,4,5, 1,5,2 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        public void AdvanceTo(int current)
        {
            EnsureFloor(current+1);
            var retired = new List<int>();
            foreach (var pair in floors) if (pair.Key<current-1) retired.Add(pair.Key);
            foreach (int index in retired) { var floor = floors[index]; floors.Remove(index); Dispose(floor.gameObject); }
        }
        public void ClearGenerated()
        {
            foreach (var floor in GetComponentsInChildren<SeededFloor>(true)) Dispose(floor.gameObject);
            floors.Clear();
        }
        static void Dispose(GameObject go)
        {
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
        void OnDestroy()
        {
            if (resourceMesh != null)
            {
                if (Application.isPlaying) Destroy(resourceMesh); else DestroyImmediate(resourceMesh);
            }
            if (ownedMaterial == null) return;
            if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
        }
    }
}
