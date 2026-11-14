using UnityEngine;

namespace Sound2.LevelEditing
{
    [DisallowMultipleComponent]
    public sealed class AdventureFloor : MonoBehaviour
    {
        [Range(1, 8)] public int number = 1;
        public string theme;
        public LevelLayout layout;
        public Vector3 spawn = new(-30, 1.1f, -36);
        public GameObject encounters;
        public Vector2 entryDock = new(-28, -47.5f);
        public Vector2 exitDock = new(28, 47.5f);
        public string combatRoomId;
        public Vector3 SpawnWorld => transform.TransformPoint(spawn);
    }
}
