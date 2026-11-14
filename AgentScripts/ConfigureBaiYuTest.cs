using System;
using System.Linq;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ConfigureBaiYuTest
{
    public static object Main()
    {
        const string path = "Assets/Scenes/BaiYu-Test.unity";
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) throw new InvalidOperationException("BaiYu-Test already exists; preserve its edits.");
        if (Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Any(i=>UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Save open scene edits before creating the test scene.");
        SeededMapBuilder.CreateScene(path);
        var scene = EditorSceneManager.OpenScene(path);
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        var map = UnityEngine.Object.FindAnyObjectByType<LevelMap>();
        var profile = UnityEngine.Object.Instantiate(map.profile);
        profile.name = "BaiYu Test Floor Progression";
        profile.defaultEntryText = "F{floor} · 循着回声，寻找通往下一层的竖井。";
        profile.overrides.Clear();
        for (int floor = 0; floor < 3; floor++)
        {
            var rule = profile.Evaluate(floor);
            rule.entrySubtitles.Clear();
            rule.entrySubtitles.Add(new SubtitleCue("F"+floor+" - "+(floor==0?"综合测试区域":floor==1?"更深的区域，更多物品，也有更多危险。":"回声正在减弱，继续前进。"),4));
            if (floor==0)
            {
                rule.entrySubtitles.Add(new SubtitleCue("WASD 移动，鼠标转向；瞄准物品左键拿起，持物时左键抛出、右键放下。",5));
                rule.entrySubtitles.Add(new SubtitleCue("普通球、棱晶和方块均可拿起；左键抛出，右键轻放。",5));
                rule.entrySubtitles.Add(new SubtitleCue("红色点阵提示怪物，蓝色提示闪电；循着低鸣找到竖井，进入下一层。",5));
            }
            profile.overrides.Add(rule);
        }
        AssetDatabase.CreateAsset(profile,"Assets/Resources/BaiYuTestFloorProgression.asset");
        map.profile = profile;
        var sonar = player.GetComponentInChildren<SphericalSonar>();
        sonar.SetRevealModes(false, true);
        var camera = sonar.GetComponent<Camera>();
        camera.transform.localRotation = Quaternion.identity;
        camera.gameObject.AddComponent<SteamAudio.SteamAudioListener>();
        if (player.GetComponent<FootstepEmitter>() == null) player.gameObject.AddComponent<FootstepEmitter>();
        player.gameObject.AddComponent<PlayerVitality>();
        player.gameObject.AddComponent<PlayerStatusHUD>();
        player.GetComponent<PlayerFloorProgression>().profile = profile;
        PrefabUtility.RecordPrefabInstancePropertyModifications(camera.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.GetComponent<PlayerFloorProgression>());
        var setup = new GameObject("BaiYu-Test Systems").AddComponent<BaiYuTestScene>();
        setup.map = map; setup.player = player.GetComponent<PlayerFloorProgression>();
        setup.lightningPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BallLightning.prefab");
        setup.pickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab");
        setup.worldMaterial = map.geometryMaterial;
        var menu=new GameObject("SONICSPACE Entry").AddComponent<SonicSpaceStartMenu>();
        menu.player=player;menu.menuShader=Shader.Find("Sound2/SonicSpace Point Menu");
        menu.glyphMaskShader=Shader.Find("Sound2/Menu Glyph Mask");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = setup.gameObject;
        return new {scene=path, systems=player.GetComponentsInChildren<MonoBehaviour>(true).Where(x=>x!=null).Select(x=>x.GetType().Name).ToArray(), profile=AssetDatabase.GetAssetPath(profile)};
    }
}
