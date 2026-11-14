using UnityEngine;

namespace Sound2.LevelEditing
{
    public enum AdventureGenerationMode { DungeonLoops, Architectural }
    /// <summary>Authored procedural output: eight editable layouts, seven one-way physical lifts.</summary>
    [DefaultExecutionOrder(-110), DisallowMultipleComponent]
    public sealed class EightFloorAdventure : MonoBehaviour
    {
        public int seed = 20261114;
        public AdventureGenerationMode generationMode;
        public FloorProgressionProfile profile;
        public PlayerFloorProgression player;
        public AdventureFloor[] floors = new AdventureFloor[0];
        public OneWayElevator[] elevators = new OneWayElevator[0];
        public int CurrentFloor => player != null ? player.CurrentFloor : 1;
        public AdventureFloor FindFloor(int number) => System.Array.Find(floors, f => f != null && f.number == number);

        void Start()
        {
            gameObject.AddComponent<AdventureHudContrast>();
        }

        void Awake()
        {
            if (!Application.isPlaying) return;
            foreach (var floor in floors)
            {
                floor.gameObject.SetActive(floor.number == 1);
                if (floor.encounters != null) floor.encounters.SetActive(false);
            }
            var first = FindFloor(1);
            if (player != null && first != null)
            {
                var capsule = player.GetComponent<CharacterController>();
                bool enabled = capsule.enabled; capsule.enabled = false;
                player.transform.position = first.SpawnWorld;
                capsule.enabled = enabled;
            }
        }

        public bool PrepareDestination(OneWayElevator lift)
        {
            if (lift == null || lift.adventure != this || lift.sourceFloor != CurrentFloor
                || lift.destinationFloor != CurrentFloor + 1 || !System.Array.Exists(elevators, e => e == lift)) return false;
            var next = FindFloor(lift.destinationFloor);
            if (next == null) return false;
            next.gameObject.SetActive(true);
            Physics.SyncTransforms();
            return true;
        }

        public void EnterFloor(int number)
        {
            foreach (var floor in floors)
            {
                // Retain scene state, retire old geometry and actors only after the cabin arrives.
                if (floor.encounters != null) floor.encounters.SetActive(floor.number == number);
                floor.gameObject.SetActive(floor.number == number);
            }
        }
    }
}
