using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sound2.LevelEditing.Editor
{
    public static class RingTableMeshPersistence
    {
        public static void Persist(LevelLayout layout)
        {
            var tables = layout.GetComponentsInChildren<DrapedRingTable>(true);
            for (int i = 0; i < tables.Length; i++)
            {
                var table = tables[i]; table.Rebuild();
                var filter = table.GetComponent<MeshFilter>(); var collider = table.GetComponent<MeshCollider>();
                var visual = Store(layout.geometryAsset, "RingTable_" + i + "_Cloth", filter.sharedMesh);
                var collision = Store(layout.geometryAsset, "RingTable_" + i + "_Collision", collider.sharedMesh);
                table.UseStoredMeshes(visual, collision);
                EditorUtility.SetDirty(filter); EditorUtility.SetDirty(collider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
        }
        static Mesh Store(string path, string name, Mesh source)
        {
            if (source == null) return null;
            var stored = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => m.name == name);
            if (stored == null) { stored = Object.Instantiate(source); stored.name = name; AssetDatabase.AddObjectToAsset(stored, path); }
            else
            {
                stored.Clear(); stored.indexFormat = source.indexFormat; stored.vertices = source.vertices;
                stored.triangles = source.triangles; stored.normals = source.normals; stored.uv = source.uv; stored.bounds = source.bounds;
            }
            EditorUtility.SetDirty(stored); return stored;
        }
    }
}
