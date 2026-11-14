using System;
using UnityEngine;

namespace Sound2.LevelEditing
{
    [DisallowMultipleComponent]
    public sealed class DoorInteraction : InteractableObject
    {
        public Transform hinge;
        public BoxCollider leafCollider;
        [Range(60, 100)] public float openAngle = 90;
        [Min(20)] public float degreesPerSecond = 150;
        public LayerMask obstructionLayers = ~0;
        public bool TargetOpen { get; private set; }
        public bool Blocked { get; private set; }
        public float CurrentAngle { get; private set; }
        public bool IsOpen => Mathf.Abs(CurrentAngle) >= openAngle - .1f;
        public override bool AllowWhileHolding => true;
        public override string InteractionHint => (TargetOpen ? "关门" : "开门") + (Blocked ? "（受阻）" : "");

        Quaternion closedRotation;
        Rigidbody hingeBody;
        float swingSign = 1;
        Collider[] obstacles = new Collider[16];

        void Awake()
        {
            if (hinge == null) return;
            closedRotation = hinge.localRotation;
            hingeBody = hinge.GetComponent<Rigidbody>();
        }

        public override bool CanInteract(PlayerInteraction player) => base.CanInteract(player)
            && player != null && hinge != null && leafCollider != null && !SonicSpaceStartMenu.BlocksGameplay
            && player.TryGetComponent<PlayerController>(out var controller) && !controller.InputSuspended && !controller.TransportLocked;

        protected override void OnInteract(PlayerInteraction player)
        {
            if (!TargetOpen && Mathf.Abs(CurrentAngle) < .1f)
                swingSign = Vector3.Dot(player.transform.position - hinge.position, transform.forward) >= 0 ? 1 : -1;
            TargetOpen = !TargetOpen;
            Blocked = false;
        }

        void FixedUpdate()
        {
            if (hinge == null || leafCollider == null || SonicSpaceStartMenu.BlocksGameplay) return;
            float target = TargetOpen ? swingSign * openAngle : 0;
            float next = Mathf.MoveTowards(CurrentAngle, target, Mathf.Max(20, degreesPerSecond) * Time.fixedDeltaTime);
            Blocked = false;
            if (Mathf.Abs(next - CurrentAngle) < .0001f) return;
            int steps = Mathf.CeilToInt(Mathf.Abs(next - CurrentAngle) / 2);
            float accepted = CurrentAngle;
            for (int i = 1; i <= steps; i++)
            {
                float angle = Mathf.Lerp(CurrentAngle, next, i / (float)steps);
                if (Obstructed(angle)) { Blocked = true; break; }
                accepted = angle;
            }
            CurrentAngle = accepted;
            var rotation = closedRotation * Quaternion.Euler(0, CurrentAngle, 0);
            if (hingeBody != null) hingeBody.MoveRotation(hinge.parent.rotation * rotation);
            else hinge.localRotation = rotation;
        }

        bool Obstructed(float angle)
        {
            Quaternion rotation = hinge.parent.rotation * closedRotation * Quaternion.Euler(0, angle, 0);
            Quaternion delta = rotation * Quaternion.Inverse(hinge.rotation);
            var center = hinge.position + delta * (leafCollider.transform.TransformPoint(leafCollider.center) - hinge.position);
            var scale = leafCollider.transform.lossyScale;
            var extents = Vector3.Scale(leafCollider.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            extents -= Vector3.one * .003f;
            int count;
            while ((count = Physics.OverlapBoxNonAlloc(center, extents, obstacles,
                delta * leafCollider.transform.rotation, obstructionLayers, QueryTriggerInteraction.Ignore)) == obstacles.Length)
                Array.Resize(ref obstacles, obstacles.Length * 2);
            for (int i = 0; i < count; i++)
                if (!obstacles[i].transform.IsChildOf(transform)) return true;
            return false;
        }
    }
}
