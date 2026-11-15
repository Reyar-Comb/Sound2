using System;
using UnityEngine;

public sealed class CreatorRoomCatalog : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string label;
        public GameObject prefab;
        public Mesh mesh;
        public bool modelOnly;
    }

    [HideInInspector] public Entry[] entries = Array.Empty<Entry>();
}
