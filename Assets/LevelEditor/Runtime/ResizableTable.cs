using UnityEngine;

namespace Sound2.LevelEditing
{
    /// <summary>Dimensions are in local metres; the origin stays at the centre of the feet.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ResizableTable : MonoBehaviour
    {
        [Min(.2f)] public float width = 2;
        [Min(.2f)] public float depth = 1;
        [Min(.2f)] public float height = .8f;
        [Min(.01f)] public float topThickness = .08f;
        [Min(.01f)] public float legThickness = .08f;
        [Min(0)] public float legInset = .06f;
        [SerializeField, HideInInspector] Transform tabletop;
        [SerializeField, HideInInspector] Transform[] legs = new Transform[4];
        bool needsRebuild;

        public void SetParts(Transform top, Transform[] supports)
        {
            tabletop = top; legs = supports; Rebuild();
        }

        void OnEnable() => Rebuild();
        // OnValidate can run on the loading thread. Defer Transform changes to Update.
        void OnValidate() => needsRebuild = true;
        void Update() { if (needsRebuild) Rebuild(); }

        public void Rebuild()
        {
            needsRebuild = false;
            width = Mathf.Max(.2f, width); depth = Mathf.Max(.2f, depth); height = Mathf.Max(.2f, height);
            topThickness = Mathf.Clamp(topThickness, .01f, height - .05f);
            legThickness = Mathf.Clamp(legThickness, .01f, Mathf.Min(width, depth) * .4f);
            legInset = Mathf.Clamp(legInset, 0, Mathf.Min(width, depth) * .5f - legThickness);
            if (tabletop != null)
            {
                tabletop.localPosition = new Vector3(0, height - topThickness * .5f, 0);
                tabletop.localScale = new Vector3(width, topThickness, depth);
            }
            float x = width * .5f - legInset - legThickness * .5f;
            float z = depth * .5f - legInset - legThickness * .5f;
            float length = height - topThickness;
            for (int i = 0; legs != null && i < Mathf.Min(4, legs.Length); i++)
                if (legs[i] != null)
                {
                    legs[i].localPosition = new Vector3((i % 2 == 0 ? -1 : 1) * x, length * .5f, (i < 2 ? -1 : 1) * z);
                    legs[i].localScale = new Vector3(legThickness, length, legThickness);
                }
        }
    }
}
