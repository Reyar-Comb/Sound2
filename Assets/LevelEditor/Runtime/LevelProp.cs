using UnityEngine;

namespace Sound2.LevelEditing
{
    public enum PropSurface { Floor, Wall }

    [DisallowMultipleComponent]
    public sealed class LevelProp : MonoBehaviour
    {
        public PropSurface surface = PropSurface.Floor;
        [Tooltip("Local point placed against the surface. Wall props face local +Z, with local +Y up.")]
        public Vector3 surfaceAnchor;
        [Min(0)] public float surfaceOffset = .01f;
        public Material gameplayMaterial;
        Renderer[] renderers;
        Material[][] previewMaterials;

        void OnEnable()
        {
            if (!Application.isPlaying || gameplayMaterial == null) return;
            renderers = GetComponentsInChildren<Renderer>(true);
            previewMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                previewMaterials[i] = renderers[i].sharedMaterials;
                var materials = new Material[previewMaterials[i].Length];
                for (int j = 0; j < materials.Length; j++) materials[j] = gameplayMaterial;
                renderers[i].sharedMaterials = materials;
            }
        }

        void OnDisable()
        {
            if (previewMaterials == null) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterials = previewMaterials[i];
            previewMaterials = null;
        }
    }
}
