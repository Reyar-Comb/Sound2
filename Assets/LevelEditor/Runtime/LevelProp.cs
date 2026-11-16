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
        [HideInInspector] public Mesh combinedGameplayMesh;
        Mesh previewMesh;
        Renderer[] renderers;
        Material[][] previewMaterials;

        void OnEnable() => RefreshGameplayPresentation();

        internal void RefreshGameplayPresentation()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || gameplayMaterial == null) return;
            if (DeveloperMode.Enabled)
            {
                RestorePreview();
                return;
            }
            if (previewMaterials != null) return;
            var filter = GetComponent<MeshFilter>();
            if (combinedGameplayMesh != null && filter != null)
            {
                previewMesh = filter.sharedMesh;
                filter.sharedMesh = combinedGameplayMesh;
            }
            renderers = GetComponentsInChildren<Renderer>(true);
            previewMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                previewMaterials[i] = renderers[i].sharedMaterials;
                var materials = new Material[combinedGameplayMesh != null && renderers[i].gameObject == gameObject ? 1 : previewMaterials[i].Length];
                for (int j = 0; j < materials.Length; j++) materials[j] = gameplayMaterial;
                renderers[i].sharedMaterials = materials;
            }
        }

        void OnDisable() => RestorePreview();

        void RestorePreview()
        {
            if (previewMesh != null) { GetComponent<MeshFilter>().sharedMesh = previewMesh; previewMesh = null; }
            if (previewMaterials == null) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterials = previewMaterials[i];
            previewMaterials = null;
        }
    }
}
