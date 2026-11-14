using UnityEngine;
namespace Sound2.LevelEditing
{
    public sealed class SeededFloor : MonoBehaviour
    {
        public int floorIndex;
        public SeededFloorPlan Plan { get; internal set; }
        public FloorShaftTransition Exit { get; internal set; }
        public Mesh Geometry { get; internal set; }
        void OnDestroy()
        {
            if (Geometry == null) return;
            if (Application.isPlaying) Destroy(Geometry); else DestroyImmediate(Geometry);
        }
    }
}
