using System;
using UnityEngine;

namespace Sound2.LevelEditing
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ResizableBar : MonoBehaviour
    {
        [Min(.8f), InspectorName("长度（米）")]
        public float length = 2.4f;

        [Serializable]
        public struct Part
        {
            public Transform target;
            public float positionRatio;
            public float positionOffset;
            public float lengthRatio;
            public float lengthOffset;
        }

        [SerializeField, HideInInspector] Part[] parts = Array.Empty<Part>();
        float appliedLength = float.NaN;
        bool needsRebuild;

        public void SetParts(Part[] value) { parts = value; Rebuild(); }
        void OnEnable() => Rebuild();
        void OnValidate() => needsRebuild = true;
        void Update() { if (needsRebuild || appliedLength != length) Rebuild(); }

        public void Rebuild()
        {
            length = float.IsNaN(length) || float.IsInfinity(length) ? 2.4f : Mathf.Max(.8f, length);
            needsRebuild = false;
            appliedLength = length;
            if (parts == null) return;
            foreach (var part in parts)
            {
                if (part.target == null) continue;
                var position = part.target.localPosition;
                position.x = length * part.positionRatio + part.positionOffset;
                part.target.localPosition = position;
                if (part.lengthRatio <= 0) continue;
                var scale = part.target.localScale;
                scale.x = Mathf.Max(.01f, length * part.lengthRatio + part.lengthOffset);
                part.target.localScale = scale;
            }
        }
    }
}
