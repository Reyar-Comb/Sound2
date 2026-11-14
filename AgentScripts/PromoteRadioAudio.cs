using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PromoteRadioAudio
{
    const string PrefabPath = "Assets/Prefabs/Radio.prefab";
    const string DemoPath = "Assets/Scenes/audiotest.unity";

    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode before promoting radio audio.");
        var original = SceneManager.GetActiveScene();
        var demo = SceneManager.GetSceneByPath(DemoPath);
        bool opened = !demo.IsValid() || !demo.isLoaded;
        if (opened) demo = EditorSceneManager.OpenScene(DemoPath, OpenSceneMode.Additive);
        else if (demo.isDirty) throw new Exception("Save audiotest edits before migration.");
        try
        {
            var emitter = demo.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AudioReactiveSonarEmitter>(true))
                .Single(e => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(e.gameObject) == PrefabPath);
            if (emitter.analysis == null || !emitter.analysis.IsReady) throw new Exception("Radio recording must be baked.");
            var pool = emitter.GetComponent<ImpactSoundPool>();
            var action = emitter.GetComponent<InteractableObject>();
            bool autoStart = emitter.playOnStart;
            if (emitter.emissionOrigin != null && PrefabUtility.IsAddedGameObjectOverride(emitter.emissionOrigin.gameObject))
                PrefabUtility.ApplyAddedGameObject(emitter.emissionOrigin.gameObject, PrefabPath, InteractionMode.AutomatedAction);
            if (PrefabUtility.IsAddedComponentOverride(pool))
                PrefabUtility.ApplyAddedComponent(pool, PrefabPath, InteractionMode.AutomatedAction);
            if (PrefabUtility.IsAddedComponentOverride(emitter))
                PrefabUtility.ApplyAddedComponent(emitter, PrefabPath, InteractionMode.AutomatedAction);
            // Apply after the emitter/origin so Unity remaps the event's scene reference into the prefab.
            PrefabUtility.ApplyObjectOverride(action, PrefabPath, InteractionMode.AutomatedAction);
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var savedEmitter = contents.GetComponent<AudioReactiveSonarEmitter>();
                var savedAction = contents.GetComponent<InteractableObject>();
                if (savedAction.onInteract.GetPersistentEventCount() != 1
                    || savedAction.onInteract.GetPersistentTarget(0) != savedEmitter
                    || savedAction.onInteract.GetPersistentMethodName(0) != "Toggle")
                    throw new Exception("Radio power event was not mapped into the prefab.");
                savedEmitter.playOnStart = false;
                savedEmitter.loop = true;
                savedEmitter.field = null;
                savedAction.interactionHint = "收音机电源";
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            // Keep the demo's intentional automatic playback as a scene override.
            emitter.playOnStart = autoStart;
            PrefabUtility.RecordPrefabInstancePropertyModifications(emitter);
            EditorSceneManager.MarkSceneDirty(demo);
            EditorSceneManager.SaveScene(demo);
            return new { prefab = PrefabPath, defaultPower = "off", demoAutoStart = emitter.playOnStart,
                emitters = emitter.GetComponents<AudioReactiveSonarEmitter>().Length,
                pools = emitter.GetComponents<ImpactSoundPool>().Length,
                clip = emitter.analysis.clip.name, origin = emitter.emissionOrigin.name };
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (opened) EditorSceneManager.CloseScene(demo, true);
        }
    }
}
