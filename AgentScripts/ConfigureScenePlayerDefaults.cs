using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-time migration through Unity serialization. Preserves each scene's step tuning and events.
public static class ConfigureScenePlayerDefaults
{
    const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
    static T[] Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Open scenes have unsaved edits; preserve them before migration.");
        var scenes = new List<Scene>();
        var savedSteps = new Dictionary<GameObject, string>();
        var changed = new HashSet<string>();
        var results = new List<object>();
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                scenes.Add(scene);
            }

            // Move scene-added footsteps to the shared prefab without leaving duplicate emitters.
            foreach (var scene in scenes)
                foreach (var player in Components<PlayerController>(scene))
                {
                    var foot = player.GetComponent<FootstepEmitter>();
                    if (foot == null || !PrefabUtility.IsAddedComponentOverride(foot)
                        || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player) != PlayerPrefab) continue;
                    savedSteps[player.gameObject] = JsonUtility.ToJson(foot);
                    UnityEngine.Object.DestroyImmediate(foot);
                    changed.Add(scene.path);
                }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var controller = new SerializedObject(root.GetComponent<PlayerController>());
                controller.FindProperty("useGravity").boolValue = true;
                controller.ApplyModifiedPropertiesWithoutUndo();
                var foot = root.GetComponent<FootstepEmitter>();
                if (foot == null) foot = root.AddComponent<FootstepEmitter>();
                foot.enabled = foot.emissionEnabled = foot.soundEnabled = true;
                if (PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab) == null)
                    throw new InvalidOperationException("Could not save Player prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            foreach (var scene in scenes)
            {
                var players = Components<PlayerController>(scene);
                var legacy = Components<BouncePlaytest>(scene).Where(b => b.enabled && b.GetComponentInParent<PlayerController>() == null).ToArray();
                foreach (var actor in players.Select(p => p.gameObject).Concat(legacy.Select(b => b.gameObject)))
                {
                    var foot = actor.GetComponent<FootstepEmitter>();
                    if (foot == null) { foot = actor.AddComponent<FootstepEmitter>(); changed.Add(scene.path); }
                    if (savedSteps.TryGetValue(actor, out string json)) JsonUtility.FromJsonOverwrite(json, foot);
                    if (!foot.enabled || !foot.emissionEnabled || !foot.soundEnabled) changed.Add(scene.path);
                    foot.enabled = foot.emissionEnabled = foot.soundEnabled = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(foot);
                    if (actor.GetComponents<FootstepEmitter>().Length != 1)
                        throw new InvalidOperationException("Duplicate footsteps in " + scene.path);
                }
                foreach (var sonar in Components<SphericalSonar>(scene))
                {
                    if (sonar.proximityRendering) changed.Add(scene.path);
                    sonar.SetRevealModes(false, sonar.sonarRendering);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(sonar);
                }
                foreach (var source in Components<BlackBoxProximitySource>(scene))
                {
                    // SphericalSonar may already have updated the source above; persist both owners.
                    source.SetRevealModes(false, source.SonarRendering);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                    changed.Add(scene.path);
                    if (source.GetComponentInParent<PlayerController>() != null
                        && source.GetComponent<SphericalSonar>() == null && source.GetComponent<SonarPulseField>() == null)
                        source.gameObject.AddComponent<SonarPulseField>();
                }
                if (changed.Contains(scene.path))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + scene.path);
                }
                results.Add(new { scene = scene.path, players = players.Length, legacyPlayers = legacy.Length,
                    footsteps = Components<FootstepEmitter>(scene).Length, saved = changed.Contains(scene.path) });
            }
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        return results;
    }
}
