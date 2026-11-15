using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyVineMotion
{
    static readonly List<string> checks = new();
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add("PASS: " + label); }
    static async Task Tick(int ms) { int frame = Time.frameCount; await Task.Delay(ms); Check(Time.frameCount > frame, "Player loop advances"); }
    public static async Task<object> Main()
    {
        if (!Application.isPlaying) throw new Exception("Play mode required");
        var scene = SceneManager.CreateScene("Vine motion verification");
        var root = new GameObject("Vine fixture"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = new Vector3(5000, 0, 5000);
        var playerObject = new GameObject("Fixture vitality"); playerObject.transform.SetParent(root.transform, false);
        var vitality = playerObject.AddComponent<PlayerVitality>();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform, false);
        floor.transform.localPosition = new Vector3(0, -.5f, 0); floor.transform.localScale = new Vector3(30, 1, 30);
        var areaObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RedVineArea.prefab"), root.transform);
        areaObject.transform.localPosition = Vector3.zero;
        var area = areaObject.GetComponent<MonsterVineArea>();
        var motion = areaObject.AddComponent<MonsterVineAreaMotion>();
        motion.startPosition = Vector3.zero; motion.endPosition = Vector3.right * 2; motion.speed = 2;
        var audio = areaObject.GetComponent<MonsterAudioEmitter>();
        var listener = new GameObject("Fixture ears"); listener.transform.SetParent(areaObject.transform, false);
        listener.transform.localPosition = new Vector3(.5f, .8f, 0); audio.listener = listener.transform;
        int noiseCount = 0;
        Action<GameplayNoiseEvent> observe = _ => noiseCount++;
        try
        {
            root.SetActive(true); Physics.SyncTransforms();
            GameplayNoise.Emitted += observe;
            await Tick(400);
            Check(motion.transform.localPosition.x > .2f && motion.transform.localPosition.x < 2, "Area moves from configured start towards end");
            Check(area.GetComponent<MonsterVines>().PointCount > 15, "Moving area retains and grows surface vines");
            AudioSource Voice(string clip) => areaObject.GetComponentsInChildren<AudioSource>().FirstOrDefault(s => s.clip != null && s.clip.name == clip);
            Check(Voice("crawl") != null && Voice("crawl").isPlaying && Voice("crawl").volume > 0, "Moving vine area plays the same crawl audio");
            Check(Voice("hum") != null && Voice("hum").isPlaying && Voice("shout") != null && Voice("shout").isPlaying, "Nearby vine area plays monster hum and shout layers");
            await Tick(800);
            float nearEnd = motion.transform.localPosition.x;
            await Tick(350);
            Check(motion.transform.localPosition.x < nearEnd, "Area reverses at the endpoint");
            Check(vitality.TakeLethalDamage() && vitality.IsDead, "Real PlayerVitality death event fires");
            Check(Vector3.Distance(motion.transform.localPosition, motion.startPosition) < .001f
                && area.GetComponent<MonsterVines>().PointCount == 0, "Death immediately resets position and clears old vines");
            await Tick(350);
            Check(Vector3.Distance(motion.transform.localPosition, motion.startPosition) < .001f, "Area waits at start while the player is dead");
            Check(!Voice("crawl").isPlaying || Voice("crawl").volume < .001f, "Stationary area stops crawling audio");
            vitality.RestoreAfterRespawn(); await Tick(350);
            Check(motion.transform.localPosition.x > .2f, "Respawn restarts the route from its beginning");
            motion.pingPong = false; motion.speed = 10; motion.ResetToStart(); await Tick(400);
            Check(Vector3.Distance(motion.transform.localPosition, motion.endPosition) < .001f, "One-way mode reaches the endpoint");
            await Tick(150);
            Check(Vector3.Distance(motion.transform.localPosition, motion.endPosition) < .001f, "One-way mode remains at endpoint");
            areaObject.SetActive(false); areaObject.SetActive(true);
            Check(Vector3.Distance(motion.transform.localPosition, motion.startPosition) < .001f, "Reactivating a level restarts the moving area");
            // Count only events from this fixture; audio must never lure enemies by itself.
            Check(areaObject.GetComponent<PatrolMonster>() == null && areaObject.GetComponent<MonsterScanEmitter>() == null, "Area audio needs no monster AI or scanning component");
            return new { passed = true, checks, observedWorldNoises = noiseCount };
        }
        finally
        {
            GameplayNoise.Emitted -= observe;
            root.SetActive(false); Object.Destroy(root); await SceneManager.UnloadSceneAsync(scene);
        }
    }
}
