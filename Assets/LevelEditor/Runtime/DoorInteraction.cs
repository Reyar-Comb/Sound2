using System;
using UnityEngine;

namespace Sound2.LevelEditing
{
    public enum DoorOpeningSide
    {
        [InspectorName("双向")] Both,
        [InspectorName("仅正面（本地 +Z）")] FrontOnly,
        [InspectorName("仅背面（本地 -Z）")] BackOnly
    }

    [DisallowMultipleComponent]
    public sealed class DoorInteraction : InteractableObject
    {
        public Transform hinge;
        public BoxCollider leafCollider;
        [Range(60, 100)] public float openAngle = 90;
        [Min(20)] public float degreesPerSecond = 150;
        public LayerMask obstructionLayers = ~0;
        [Header("Door behaviour")]
        public DoorOpeningSide openingSide;
        [Tooltip("成功开门的同一帧传送到此门，保持门前相对位置与视角。留空为普通门；目标须已激活、竖直且与此门等比例。")]
        public DoorInteraction teleportDestination;
        [Tooltip("完全打开后等待的秒数；0 表示不自动关闭。")]
        [Min(0)] public float autoCloseDelay;
        [Range(.1f, 3)] public float lockedShakeAngle = 1.5f;
        [Min(.1f)] public float lockedShakeDuration = .3f;
        [Header("门交互音效与声波")]
        [SerializeField, InspectorName("触发音效"), Tooltip("成功切换开关门时，从门把手播放一次并发出一道声波。")]
        AudioClip interactionSound;
        [SerializeField, Range(0, 1)] float interactionVolume = .65f;
        [SerializeField, Min(1)] float interactionPulseRange = 8;
        [SerializeField, Min(.1f)] float interactionPulseSpeed = 8;
        [SerializeField, Min(.05f)] float interactionPulseAfterglow = .6f;
        public bool TargetOpen { get; private set; }
        public bool Blocked { get; private set; }
        public float CurrentAngle { get; private set; }
        public bool IsOpen => Mathf.Abs(CurrentAngle) >= EffectiveOpenAngle - .1f;
        public bool LockedFeedback => shakeRemaining > 0;
        public bool TeleportBlocked { get; private set; }
        public override bool AllowWhileHolding => true;
        public override string InteractionHint => LockedFeedback ? "已上锁" :
            (TargetOpen ? "关门" : "开门") + (Blocked ? "（受阻）" : TeleportBlocked ? "（传送受阻）" : "");

        Quaternion closedRotation;
        Rigidbody hingeBody;
        SpatialAudioEmitter interactionAudio;
        float swingSign = 1;
        float shakeRemaining, openTime;
        float receivedOpenAngle = -1, receivedSpeed;
        float EffectiveOpenAngle => receivedOpenAngle > 0 ? receivedOpenAngle : openAngle;
        float EffectiveSpeed => receivedOpenAngle > 0 ? receivedSpeed : degreesPerSecond;
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
            if (!TargetOpen && !CanOpenFrom(player.transform.position))
            {
                // Repeated clicks do not reset the oscillation or toggle the door.
                if (!LockedFeedback) shakeRemaining = Mathf.Max(.1f, lockedShakeDuration);
                return;
            }
            shakeRemaining = 0;
            if (!TargetOpen && Mathf.Abs(CurrentAngle) < .1f)
                swingSign = Vector3.Dot(player.transform.position - hinge.position, transform.forward) >= 0 ? 1 : -1;
            TargetOpen = !TargetOpen;
            Blocked = false;
            TeleportBlocked = false;
            openTime = 0;
            receivedOpenAngle = -1;
            PlayInteractionSound(player);
            if (TargetOpen && teleportDestination != null)
                TeleportBlocked = !TryTransport(player);
        }

        void PlayInteractionSound(PlayerInteraction player)
        {
            if (interactionSound == null) return;
            var anchor = interactionAnchor != null ? interactionAnchor : hinge;
            if (interactionAudio == null && !anchor.TryGetComponent(out interactionAudio))
                interactionAudio = anchor.gameObject.AddComponent<SpatialAudioEmitter>();
            interactionAudio.PlayOneShot(interactionSound, interactionVolume);
            player.GetComponentInChildren<SonarPulseField>()?.EmitFrom(anchor.position, interactionPulseRange,
                interactionPulseSpeed, interactionPulseAfterglow, null, source: transform);
        }

        public bool CanOpenFrom(Vector3 position)
        {
            float side = Vector3.Dot(position - transform.position, transform.forward);
            return openingSide == DoorOpeningSide.Both ||
                (openingSide == DoorOpeningSide.FrontOnly ? side >= 0 : side < 0);
        }

        public string TeleportConfigurationIssue()
        {
            var destination = teleportDestination;
            if (destination == null) return null;
            if (destination == this) return "目标门不能是自身。";
            if (!destination.isActiveAndEnabled || destination.hinge == null || destination.leafCollider == null)
                return "目标门必须已激活，并配置门轴和门扇碰撞体。";
            if (Vector3.Dot(transform.up, Vector3.up) < .999f || Vector3.Dot(destination.transform.up, Vector3.up) < .999f)
                return "传送门必须保持竖直。";
            var scale = transform.lossyScale;
            if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0 ||
                (scale - destination.transform.lossyScale).sqrMagnitude > .0001f)
                return "两扇门必须使用相同的正数缩放。";
            return null;
        }

        bool TryTransport(PlayerInteraction player)
        {
            if (TeleportConfigurationIssue() != null) return false;
            var destination = teleportDestination;
            var controller = player.GetComponent<PlayerController>();
            var capsule = player.GetComponent<CharacterController>();
            if (capsule == null || !capsule.enabled) return false;
            float yawDelta = Mathf.DeltaAngle(transform.eulerAngles.y, destination.transform.eulerAngles.y);
            var rotation = Quaternion.Euler(0, yawDelta, 0);
            var position = destination.transform.position + rotation * (player.transform.position - transform.position);

            // Stage the matching leaf pose for a real clearance query, then roll back on failure.
            // Arrival never calls OnInteract, so reciprocal links cannot bounce the player back.
            if (destination.Obstructed(CurrentAngle)) return false;
            var oldRotation = destination.hinge.localRotation;
            destination.ApplyAngle(CurrentAngle, true);
            Physics.SyncTransforms();
            if (!ArrivalClear(capsule, position, rotation * player.transform.rotation))
            {
                destination.SetHingeRotation(oldRotation, true);
                Physics.SyncTransforms();
                return false;
            }
            destination.CurrentAngle = CurrentAngle;
            destination.swingSign = swingSign;
            destination.receivedOpenAngle = EffectiveOpenAngle;
            destination.receivedSpeed = EffectiveSpeed;
            destination.TargetOpen = true;
            destination.Blocked = destination.TeleportBlocked = false;
            destination.openTime = destination.shakeRemaining = 0;
            controller.TeleportSeamlessly(position, yawDelta);
            player.RemapDoorFocus(this, destination);
            return true;
        }

        bool ArrivalClear(CharacterController capsule, Vector3 position, Quaternion rotation)
        {
            var scale = capsule.transform.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfHeight = Mathf.Max(radius, capsule.height * Mathf.Abs(scale.y) * .5f);
            var center = position + rotation * Vector3.Scale(capsule.center, scale);
            var offset = Vector3.up * (halfHeight - radius);
            // Ignore only the skin shell, so a grounded capsule may arrive on the matching floor.
            float queryRadius = Mathf.Max(.01f, radius - capsule.skinWidth * Mathf.Max(scale.x, scale.z));
            int count;
            while ((count = Physics.OverlapCapsuleNonAlloc(center - offset, center + offset, queryRadius,
                obstacles, ~0, QueryTriggerInteraction.Ignore)) == obstacles.Length)
                Array.Resize(ref obstacles, obstacles.Length * 2);
            for (int i = 0; i < count; i++)
                if (!obstacles[i].transform.IsChildOf(capsule.transform) &&
                    !Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, obstacles[i].gameObject.layer) &&
                    !Physics.GetIgnoreCollision(capsule, obstacles[i])) return false;
            return true;
        }

        void FixedUpdate()
        {
            if (hinge == null || leafCollider == null || SonicSpaceStartMenu.BlocksGameplay) return;
            if (LockedFeedback)
            {
                shakeRemaining = Mathf.Max(0, shakeRemaining - Time.fixedDeltaTime);
                float phase = 1 - shakeRemaining / Mathf.Max(.1f, lockedShakeDuration);
                float shake = Mathf.Sin(phase * Mathf.PI * 6) * Mathf.Clamp(lockedShakeAngle, .1f, 3) * (1 - phase);
                ApplyAngle(Obstructed(CurrentAngle + shake) ? CurrentAngle : CurrentAngle + shake);
                return;
            }
            if (TargetOpen && IsOpen && autoCloseDelay > 0)
            {
                openTime += Time.fixedDeltaTime;
                if (openTime >= autoCloseDelay) { TargetOpen = false; openTime = 0; }
            }
            else openTime = 0;
            float target = TargetOpen ? swingSign * EffectiveOpenAngle : 0;
            float next = Mathf.MoveTowards(CurrentAngle, target, Mathf.Max(20, EffectiveSpeed) * Time.fixedDeltaTime);
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
            ApplyAngle(CurrentAngle);
        }

        void ApplyAngle(float angle, bool immediate = false) =>
            SetHingeRotation(closedRotation * Quaternion.Euler(0, angle, 0), immediate);

        void SetHingeRotation(Quaternion rotation, bool immediate)
        {
            if (hingeBody != null)
            {
                var worldRotation = (hinge.parent != null ? hinge.parent.rotation : Quaternion.identity) * rotation;
                if (immediate) hingeBody.rotation = worldRotation;
                else hingeBody.MoveRotation(worldRotation);
            }
            if (immediate || hingeBody == null) hinge.localRotation = rotation;
        }

        bool Obstructed(float angle)
        {
            Quaternion rotation = (hinge.parent != null ? hinge.parent.rotation : Quaternion.identity) * closedRotation * Quaternion.Euler(0, angle, 0);
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

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            var origin = transform.position + transform.up;
            if (teleportDestination != null)
                Gizmos.DrawLine(origin, teleportDestination.transform.position + teleportDestination.transform.up);
            if (openingSide == DoorOpeningSide.Both) return;
            var direction = transform.forward * (openingSide == DoorOpeningSide.FrontOnly ? 1 : -1);
            var tip = origin + direction;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, tip);
            Gizmos.DrawLine(tip, tip - direction * .25f + transform.right * .15f);
            Gizmos.DrawLine(tip, tip - direction * .25f - transform.right * .15f);
        }
    }
}
