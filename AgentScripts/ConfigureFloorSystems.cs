using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class ConfigureFloorSystems
{
    public static object Main(string fontPath = "")
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode before configuring assets.");
        Directory.CreateDirectory("Assets/Resources");
        AssetDatabase.Refresh();
        const string profilePath = "Assets/Resources/FloorProgression.asset";
        var profile = AssetDatabase.LoadAssetAtPath<FloorProgressionProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<FloorProgressionProfile>();
            profile.overrides.Add(new FloorRule { floor = 0, entrySubtitles = new() {
                new("F0 · 起点。"), new("靠近物体，观察白色圆环。按鼠标左键与它交互。", 5), new("找到向下的竖井。每次下行，都会进入更深的一层。", 5) } });
            profile.overrides.Add(new FloorRule { floor = 1, difficulty = 1.15f, sonarRangeMultiplier = .96f, sonarAfterglowMultiplier = .94f,
                entrySubtitles = new() { new("F1 · 回声开始变短。"), new("放慢脚步，听清前方，再继续向下。", 5) } });
            profile.overrides.Add(new FloorRule { floor = 2, difficulty = 1.3f, sonarRangeMultiplier = .9216f, sonarAfterglowMultiplier = .8836f,
                entrySubtitles = new() { new("F2 · 更深处。"), new("每一次声音，都可能给出新的方向。", 5) } });
            AssetDatabase.CreateAsset(profile, profilePath);
        }
        const string themePath = "Assets/Resources/GameUiTheme.asset";
        var theme = AssetDatabase.LoadAssetAtPath<GameUiTheme>(themePath);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<GameUiTheme>();
            theme.textFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/PointCloudChinese SDF.asset");
            theme.legacyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/PointCloudChinese.ttf");
            AssetDatabase.CreateAsset(theme, themePath);
        }
        if (!string.IsNullOrEmpty(fontPath))
        {
            if (!File.Exists(fontPath)) throw new FileNotFoundException("Font file is not accessible.", fontPath);
            const string importedPath = "Assets/UI/SECONDERSIGN.ttf";
            if (Path.GetFullPath(fontPath) != Path.GetFullPath(importedPath)) File.Copy(fontPath, importedPath, true);
            AssetDatabase.ImportAsset(importedPath, ImportAssetOptions.ForceSynchronousImport);
            var font = AssetDatabase.LoadAssetAtPath<Font>(importedPath);
            const string sdfPath = "Assets/UI/SECONDERSIGN SDF.asset";
            var sdf = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfPath);
            if (sdf == null)
            {
                sdf = TMP_FontAsset.CreateFontAsset(font, 80, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                sdf.name = "SECONDERSIGN SDF";
                string characters = new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray());
                sdf.TryAddCharacters(characters, out string missing);
                sdf.atlasPopulationMode = AtlasPopulationMode.Static;
                AssetDatabase.CreateAsset(sdf, sdfPath);
                foreach (var texture in sdf.atlasTextures) { texture.name = "SECONDERSIGN Atlas"; AssetDatabase.AddObjectToAsset(texture, sdf); }
                AssetDatabase.AddObjectToAsset(sdf.material, sdf);
                // Use the same supplied font for future supported characters, including editable subtitles.
                var dynamic = TMP_FontAsset.CreateFontAsset(font, 80, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                dynamic.name = "SECONDERSIGN Dynamic";
                AssetDatabase.CreateAsset(dynamic, "Assets/UI/SECONDERSIGN Dynamic.asset");
                foreach (var texture in dynamic.atlasTextures) AssetDatabase.AddObjectToAsset(texture, dynamic);
                AssetDatabase.AddObjectToAsset(dynamic.material, dynamic);
                var so = new SerializedObject(dynamic);
                var clear = so.FindProperty("m_ClearDynamicDataOnBuild");
                if (clear != null) { clear.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
                sdf.fallbackFontAssetTable ??= new();
                sdf.fallbackFontAssetTable.Add(dynamic);
                // Chinese text remains readable if the supplied display font contains Latin glyphs only.
                var chinese = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/PointCloudChinese SDF.asset");
                if (chinese != null) sdf.fallbackFontAssetTable.Add(chinese);
            }
            sdf.fallbackFontAssetTable ??= new();
            var existingDynamic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/SECONDERSIGN Dynamic.asset");
            if (existingDynamic != null && !sdf.fallbackFontAssetTable.Contains(existingDynamic)) sdf.fallbackFontAssetTable.Add(existingDynamic);
            var existingChinese = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/PointCloudChinese SDF.asset");
            if (existingChinese != null && !sdf.fallbackFontAssetTable.Contains(existingChinese)) sdf.fallbackFontAssetTable.Add(existingChinese);
            theme.textFont = sdf; theme.legacyFont = font;
            EditorUtility.SetDirty(sdf);
        }
        EditorUtility.SetDirty(theme);
        const string playerPath = "Assets/Prefabs/Player.prefab";
        var player = PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            if (player.GetComponent<PlayerSubtitles>() == null) player.AddComponent<PlayerSubtitles>();
            var progression = player.GetComponent<PlayerFloorProgression>();
            if (progression == null) progression = player.AddComponent<PlayerFloorProgression>();
            progression.profile = profile;
            PrefabUtility.SaveAsPrefabAsset(player, playerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets();
        return new { profile = profilePath, theme = themePath, font = theme.textFont.name, requestedFontInstalled = File.Exists("Assets/UI/SECONDERSIGN.ttf") };
    }
}
