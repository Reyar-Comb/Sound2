using UnityEngine;using UnityEditor;using System.Linq;
public static class ConfigureBaiYuChinese {
 public static object Main(){
 var p=AssetDatabase.LoadAssetAtPath<FloorProgressionProfile>("Assets/Resources/BaiYuTestFloorProgression.asset");Undo.RecordObject(p,"Chinese BaiYu-Test subtitles");
 p.defaultEntryText="F{floor} · 循着回声，寻找通往下一层的竖井。";
 string[][] lines={new[]{"F0 · 综合测试区域","WASD 移动，鼠标转向；瞄准物品左键拿起，持物时左键抛出、右键放下。","普通球、棱晶、金币和方块均可拿起；左键抛出，右键轻放。","红色点阵提示怪物，蓝色提示闪电；循着低鸣找到竖井，进入下一层。"},new[]{"F1 · 更深的区域，更多物品，也有更多危险。"},new[]{"F2 · 回声正在减弱，继续前进。"}};
 for(int i=0;i<3;i++){var rule=p.overrides.First(x=>x.floor==i);rule.entrySubtitles.Clear();foreach(var line in lines[i])rule.entrySubtitles.Add(new SubtitleCue(line,5));}
 EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
 var font=GameUiTheme.Current.textFont;
 return new{profile=AssetDatabase.GetAssetPath(p),fonts=font.fallbackFontAssetTable.Select(f=>new{f.name,population=f.atlasPopulationMode.ToString(),source=f.sourceFontFile!=null?AssetDatabase.GetAssetPath(f.sourceFontFile):"none",fallbacks=f.fallbackFontAssetTable.Select(b=>b.name).ToArray()}).ToArray()};
 }
}
