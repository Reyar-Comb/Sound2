using System.Collections.Generic;
using UnityEngine;

namespace Sound2.LevelEditing
{
    /// <summary>A bounded crossing plane at the shaft exit; segment checks survive fast falls.</summary>
    public sealed class FloorShaftTransition : MonoBehaviour
    {
        static readonly List<FloorShaftTransition> active = new();
        public static IReadOnlyList<FloorShaftTransition> Active => active;
        public int sourceFloor, destinationFloor = 1;
        [Min(.1f)] public float width = 2;
        public SeededFloor destination;
        public LevelMap Map => GetComponentInParent<LevelMap>();
        public bool HasDestination => destination != null && destination.gameObject.activeInHierarchy
            && destination.floorIndex == destinationFloor;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry() => active.Clear();
        void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        void OnDisable() => active.Remove(this);
        public bool CrossedDown(Vector3 previous, Vector3 current)
        {
            var a = transform.InverseTransformPoint(previous); var b = transform.InverseTransformPoint(current);
            if (a.y < 0 || b.y >= 0 || b.y >= a.y) return false;
            Vector3 crossing = Vector3.Lerp(a, b, a.y / (a.y - b.y));
            return Mathf.Abs(crossing.x) < width * .5f && Mathf.Abs(crossing.z) < width * .5f;
        }
    }
}
