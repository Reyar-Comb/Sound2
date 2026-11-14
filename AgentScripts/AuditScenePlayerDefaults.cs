using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AuditScenePlayerDefaults
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var results = new List<object>();
        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = scene.GetRootGameObjects();
                results.Add(new {
                    scene = path,
                    players = roots.SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).Select(p => new {
                        p.name, p.UsesGravity, prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p),
                        footsteps = p.GetComponents<FootstepEmitter>().Select(f => new {
                            f.enabled, f.emissionEnabled, f.soundEnabled, local = PrefabUtility.IsAddedComponentOverride(f),
                            audio = f.audioSource != null, clips = f.clips == null ? 0 : f.clips.Length,
                            landingClip = f.landingClip != null, f.landingMultiplier
                        }).ToArray(),
                        fields = p.GetComponentsInChildren<SonarPulseField>(true).Length,
                        camera = p.ViewTransform != null
                    }).ToArray(),
                    legacyPlayers = roots.SelectMany(r => r.GetComponentsInChildren<BouncePlaytest>(true))
                        .Where(b => b.enabled && b.GetComponentInParent<PlayerController>() == null)
                        .Select(b => new { b.name, footsteps = b.GetComponents<FootstepEmitter>().Select(f =>
                            new { f.enabled, f.emissionEnabled, f.soundEnabled }).ToArray() }).ToArray(),
                    proximity = roots.SelectMany(r => r.GetComponentsInChildren<BlackBoxProximitySource>(true)).Select(s => s.ProximityRendering).ToArray(),
                    sonarProximity = roots.SelectMany(r => r.GetComponentsInChildren<SphericalSonar>(true)).Select(s => s.proximityRendering).ToArray()
                });
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        return results;
    }
}
