using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Promote scene-local camera components to Player.prefab; retain tuning and external component references.
public static class ConfigurePlayerPointCloudPrefab
{
    const string Path = "Assets/Prefabs/Player.prefab";
    sealed class Saved
    {
        public Behaviour old;
        public GameObject owner;
        public Type type;
        public string json;
        public bool enabled;
        public Behaviour Current => (Behaviour)owner.GetComponent(type);
    }
    sealed class Reference
    {
        public Component owner;
        public Saved ownerReplacement, target;
        public string property;
    }
    static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var original = SceneManager.GetActiveScene();
        var opened = new List<Scene>(); var scenes = new List<Scene>();
        var saved = new List<Saved>(); var references = new List<Reference>();
        var reports = new List<object>();
        var unsaved = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .Where(s => s.isDirty).Select(s => s.handle).ToHashSet();
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened.Add(scene); }
                scenes.Add(scene);
                foreach (var player in Find<PlayerController>(scene).Where(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p) == Path))
                {
                    var camera = player.GetComponentInChildren<Camera>(true);
                    foreach (Type type in new[] { typeof(SphericalSonar), typeof(BlackBoxProximitySource), typeof(SonarPulseField) })
                    {
                        var component = (Behaviour)camera.GetComponent(type);
                        if (component == null || !PrefabUtility.IsAddedComponentOverride(component)) continue;
                        saved.Add(new Saved { old = component, owner = camera.gameObject, type = type,
                            json = JsonUtility.ToJson(component), enabled = component.enabled });
                    }
                }
            }
            var byOld = saved.ToDictionary(s => (Component)s.old);
            // Inspect serialized references before removing anything; remap references inside migrated components too.
            foreach (var scene in scenes)
            foreach (var owner in Find<Component>(scene).Where(c => c != null))
            {
                var data = new SerializedObject(owner); var property = data.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference
                        || !(property.objectReferenceValue is Component target) || !byOld.TryGetValue(target, out var replacement)) continue;
                    byOld.TryGetValue(owner, out var ownerReplacement);
                    references.Add(new Reference { owner = owner, ownerReplacement = ownerReplacement,
                        target = replacement, property = property.propertyPath });
                }
            }
            // Dependent sonar must be removed before its required style component.
            foreach (var s in saved.OrderBy(s => s.type == typeof(SphericalSonar) ? 0 : 1)) Object.DestroyImmediate(s.old);

            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var camera = root.GetComponentInChildren<Camera>(true);
                var style = camera.GetComponent<BlackBoxProximitySource>();
                if (style == null) style = camera.gameObject.AddComponent<BlackBoxProximitySource>();
                var field = camera.GetComponent<SonarPulseField>();
                if (field == null) field = camera.gameObject.AddComponent<SonarPulseField>();
                var sonar = camera.GetComponent<SphericalSonar>();
                if (sonar == null) sonar = camera.gameObject.AddComponent<SphericalSonar>();
                style.TrackingPoint = camera.transform;
                style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars, false);
                style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Twinkle, false);
                sonar.renderingSettings = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/SonarDots.mat");
                sonar.SetRevealModes(false, true);
                root.GetComponent<FootstepEmitter>().pulseField = field;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                if (PrefabUtility.SaveAsPrefabAsset(root, Path) == null) throw new InvalidOperationException("Could not save Player prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            foreach (var s in saved)
            {
                if (s.Current == null) throw new InvalidOperationException("Prefab did not propagate " + s.type.Name);
                JsonUtility.FromJsonOverwrite(s.json, s.Current); s.Current.enabled = s.enabled;
                PrefabUtility.RecordPrefabInstancePropertyModifications(s.Current);
            }
            foreach (var reference in references)
            {
                var owner = reference.ownerReplacement != null ? reference.ownerReplacement.Current : reference.owner;
                if (owner == null) throw new InvalidOperationException("Reference owner lost: " + reference.property);
                var data = new SerializedObject(owner); var property = data.FindProperty(reference.property);
                property.objectReferenceValue = reference.target.Current;
                data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
            }
            foreach (var scene in scenes)
            {
                var players = Find<PlayerController>(scene).Where(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p) == Path).ToArray();
                if (players.Length == 0) continue;
                foreach (var player in players)
                {
                    var camera = player.GetComponentInChildren<Camera>(true);
                    // Original BlackBox uses a root field/style and a dedicated authored panel.
                    // Preserve that explicit setup without adding a second publisher or conflicting shortcut handler.
                    var rootStyle = player.GetComponent<BlackBoxProximitySource>();
                    var rootField = player.GetComponent<SonarPulseField>();
                    if (rootStyle != null && rootField != null && player.GetComponent<BlackBoxCenterControls>() != null)
                    {
                        foreach (var component in new Behaviour[] { camera.GetComponent<SphericalSonar>(), camera.GetComponent<BlackBoxProximitySource>(), camera.GetComponent<SonarPulseField>() })
                        { component.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
                        var foot = player.GetComponent<FootstepEmitter>(); foot.pulseField = rootField;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(foot);
                    }
                    if (player.GetComponentsInChildren<BlackBoxProximitySource>(true).Count(c => c.enabled) != 1
                        || player.GetComponentsInChildren<SonarPulseField>(true).Count(c => c.enabled) != 1)
                        throw new InvalidOperationException("Duplicate active point-cloud publisher in " + scene.path);
                }
                foreach (var reference in references.Where(r => r.ownerReplacement != null ? r.ownerReplacement.Current.gameObject.scene == scene : r.owner.gameObject.scene == scene))
                {
                    var owner = reference.ownerReplacement != null ? reference.ownerReplacement.Current : reference.owner;
                    if (new SerializedObject(owner).FindProperty(reference.property).objectReferenceValue != reference.target.Current)
                        throw new InvalidOperationException("Failed to remap " + reference.property);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                bool save = !unsaved.Contains(scene.handle);
                if (save && !EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + scene.path);
                reports.Add(new { scene = scene.path, players = players.Length, saved = save });
            }
            return new { prefab = Path, migratedComponents = saved.Count, preservedReferences = references.Count, scenes = reports };
        }
        finally
        {
            foreach (var scene in opened) if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        }
    }
}
