using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sound2.LevelEditing.Editor
{
    public static class WardCurtainMeshPersistence
    {
        public static void Persist(LevelLayout layout)
        {
            var curtains = layout.GetComponentsInChildren<ResizableWardCurtain>(true);
            for (int i = 0; i < curtains.Length; i++)
            {
                var curtain = curtains[i]; curtain.Rebuild(); if (curtain.Model == null) continue;
                var source = curtain.Model.sharedMesh; if (source == null) continue;
                string name = "WardCurtain_" + i;
                var stored = AssetDatabase.LoadAllAssetsAtPath(layout.geometryAsset).OfType<Mesh>().FirstOrDefault(m => m.name == name);
                if (stored == null) { stored = Object.Instantiate(source); stored.name = name; AssetDatabase.AddObjectToAsset(stored, layout.geometryAsset); }
                else
                {
                    stored.Clear(); stored.indexFormat = source.indexFormat; stored.vertices = source.vertices; stored.normals = source.normals;
                    stored.uv = source.uv; stored.subMeshCount = source.subMeshCount;
                    for (int sub = 0; sub < source.subMeshCount; sub++) stored.SetTriangles(source.GetTriangles(sub), sub);
                    stored.bounds = source.bounds;
                }
                EditorUtility.SetDirty(stored); curtain.UseStoredMesh(stored);
                EditorUtility.SetDirty(curtain.Model); PrefabUtility.RecordPrefabInstancePropertyModifications(curtain.Model);
                var collision = curtain.GetComponent<BoxCollider>(); EditorUtility.SetDirty(collision); PrefabUtility.RecordPrefabInstancePropertyModifications(collision);
            }
        }
    }
}
