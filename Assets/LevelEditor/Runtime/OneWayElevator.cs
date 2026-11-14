using System.Collections.Generic;
using UnityEngine;

namespace Sound2.LevelEditing
{
    [DefaultExecutionOrder(20)]
    public sealed class OneWayElevator : MonoBehaviour
    {
        public enum LiftState { Waiting, Boarding, Closing, Ascending, Opening, Arrived }
        public EightFloorAdventure adventure;
        public int sourceFloor = 1, destinationFloor = 2;
        public Transform cabin, lowerLeft, lowerRight, upperLeft, upperRight, cabinLeft, cabinRight;
        public float rise = 14, travelSeconds = 5, doorSeconds = 1;
        public AudioSource motor;
        public LiftState State { get; private set; }
        public float DoorOpen { get; private set; }
        public bool PermanentlyOpen => State == LiftState.Arrived && DoorOpen >= .999f;
        float elapsed;
        PlayerController passenger;
        Vector3 passengerOffset;
        readonly List<(Rigidbody body, Vector3 offset, bool kinematic, Transform parent)> cargo = new();

        void Awake() => ConfigureCabinEdges();

        void ConfigureCabinEdges()
        {
            if (cabin == null) return;
            var properties = new MaterialPropertyBlock();
            foreach (string part in new[] { "Floor", "Roof", "Back", "Left", "Right", "Left jamb", "Right jamb", "Lintel" })
            {
                var panel = cabin.Find(part);
                if (panel == null || !panel.TryGetComponent<Renderer>(out var renderer)) continue;
                properties.SetMatrix("_PanelToCabin", Matrix4x4.TRS(panel.localPosition, panel.localRotation, panel.localScale));
                properties.SetFloat("_CabinEdges", 1);
                renderer.SetPropertyBlock(properties);
            }
        }

        public bool Contains(Vector3 world)
        {
            var p = cabin.InverseTransformPoint(world);
            return Mathf.Abs(p.x) < 1.65f && p.z > -2.15f && p.z < 1.7f && p.y > .25f && p.y < 2.8f;
        }
        public bool CanUse(PlayerInteraction player, bool inside)
        {
            if (player == null || adventure == null || adventure.player != player.GetComponent<PlayerFloorProgression>()
                || adventure.CurrentFloor != sourceFloor || SonicSpaceStartMenu.BlocksGameplay) return false;
            return inside ? State == LiftState.Boarding && DoorOpen >= .999f && Contains(player.transform.position)
                : State == LiftState.Waiting;
        }
        public bool Use(PlayerInteraction player, bool inside)
        {
            if (!CanUse(player, inside)) return false;
            if (!inside) { State = LiftState.Boarding; elapsed = 0; return true; }
            if (!adventure.PrepareDestination(this)) return false;
            passenger = player.GetComponent<PlayerController>();
            passengerOffset = cabin.InverseTransformPoint(passenger.transform.position);
            passenger.TransportLocked = true;
            cargo.Clear();
            foreach (var body in FindObjectsByType<Rigidbody>())
            {
                Vector3 local = cabin.InverseTransformPoint(body.worldCenterOfMass);
                if (Mathf.Abs(local.x)>1.8f || Mathf.Abs(local.z)>2.4f || local.y<0 || local.y>2.9f
                    || body.GetComponent<PickupItem>()?.IsHeld == true) continue;
                cargo.Add((body, cabin.InverseTransformPoint(body.position), body.isKinematic, body.transform.parent));
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = true;
            }
            State = LiftState.Closing; elapsed = 0;
            passenger.GetComponent<PlayerSubtitles>()?.ShowImmediate("电梯正在上行", 3);
            return true;
        }

        void Update()
        {
            if (SonicSpaceStartMenu.BlocksGameplay || Time.deltaTime <= 0) return;
            elapsed += Time.deltaTime;
            float doorT = Mathf.Clamp01(elapsed / Mathf.Max(.1f, doorSeconds));
            if (State == LiftState.Boarding) { DoorOpen = doorT; Doors(lowerLeft, lowerRight, DoorOpen); Doors(cabinLeft, cabinRight, DoorOpen); }
            else if (State == LiftState.Closing)
            {
                DoorOpen = 1 - doorT; Doors(lowerLeft, lowerRight, DoorOpen); Doors(cabinLeft, cabinRight, DoorOpen);
                if (doorT >= 1) { State = LiftState.Ascending; elapsed = 0; if (motor != null && motor.clip != null) motor.Play(); }
            }
            else if (State == LiftState.Ascending)
            {
                float t = Mathf.Clamp01(elapsed / Mathf.Max(.1f, travelSeconds));
                cabin.localPosition = Vector3.up * (rise * Mathf.SmoothStep(0, 1, t));
                if (t >= 1)
                {
                    // Authoritative floor change occurs exactly once, after the physical ascent.
                    if (adventure.player.TryAscend(this)) { State = LiftState.Opening; elapsed = 0; }
                    if (motor != null) motor.Stop();
                }
            }
            else if (State == LiftState.Opening)
            {
                DoorOpen = doorT; Doors(upperLeft, upperRight, DoorOpen); Doors(cabinLeft, cabinRight, DoorOpen);
                if (doorT >= 1) { State = LiftState.Arrived; ReleasePassenger(); }
            }
            if (passenger != null)
            {
                passenger.Teleport(cabin.TransformPoint(passengerOffset));
                foreach (var item in cargo) if (item.body != null) item.body.position = cabin.TransformPoint(item.offset);
            }
        }
        static void Doors(Transform left, Transform right, float open)
        {
            if (left != null) { var p = left.localPosition; p.x = -.65f - open * 1.4f; left.localPosition = p; }
            if (right != null) { var p = right.localPosition; p.x = .65f + open * 1.4f; right.localPosition = p; }
        }
        void ReleasePassenger()
        {
            if (passenger != null) passenger.TransportLocked = false;
            passenger = null;
            var floor = adventure != null ? adventure.FindFloor(destinationFloor) : null;
            foreach (var item in cargo) if (item.body != null)
            {
                item.body.transform.SetParent(floor != null ? floor.transform.Find("Props") : item.parent, true);
                item.body.isKinematic = item.kinematic;
            }
            cargo.Clear();
        }
        void OnDisable() => ReleasePassenger();
    }
}
