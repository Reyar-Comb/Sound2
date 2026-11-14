using UnityEngine;

namespace Sound2.LevelEditing
{
    // The editor owns attachment and wall cuts; saved meshes work without editor code in builds.
    [DisallowMultipleComponent]
    public sealed class EmbeddedDoor : MonoBehaviour
    {
        public const float Width = 1.3f, Height = 2.35f;
        [HideInInspector] public string roomId;
        [HideInInspector] public string partitionId;
        [HideInInspector] public string corridorId; // Legacy data only: retained to report unsupported corridor-middle doors.
        [HideInInspector] public string sharedBoundaryId;
        [HideInInspector] public float along;
        [HideInInspector] public int facing = 1;
        [HideInInspector] public Transform frontTrim, backTrim, jamb;

        public void FitDepth(float depth)
        {
            if (frontTrim != null) frontTrim.localPosition = new Vector3(0, 0, depth * .5f);
            if (backTrim != null) backTrim.localPosition = new Vector3(0, 0, -depth * .5f);
            if (jamb != null) jamb.localScale = new Vector3(1, 1, depth);
        }
    }
}
