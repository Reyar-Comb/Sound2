using UnityEngine;

[CreateAssetMenu(menuName = "Sound2/Ending Credits", fileName = "WorldEndingCredits")]
public sealed class EndingCreditsAsset : ScriptableObject
{
    [TextArea(12, 40), Tooltip("第四结局滚动字幕。支持中文、空行和自动换行；末尾空行不计入停止位置。")]
    public string text = "";
    [Header("演出时间（秒）")]
    [Min(0)] public float whiteFadeSeconds = 3;
    [Min(.1f), Tooltip("标题出现完成后，字幕从起点滚到最后一行的总时间，自动计算速度。")]
    public float scrollSeconds = 60;
    [Min(0)] public float lastLineHoldSeconds = 10;
    [Min(0), Tooltip("停留后画面、BGM 和环境音一起淡到黑色/静音。")]
    public float fadeToBlackSeconds = 4;
    [Header("720p 参考排版")]
    [Min(16)] public float fontSize = 32;
    [Tooltip("正文的字体字重；真实 Light/Thin 字形需要字体资源配置对应变体。")]
    public TMPro.FontWeight fontWeight = TMPro.FontWeight.Light;
    [Range(-.15f, .15f), Tooltip("点云正文笔画粗细。负数更细，0 保持原样，正数更粗；无需额外字体变体。")]
    public float strokeWeight = -.04f;
    public float lineSpacing = 16;
    [Min(0)] public float sidePadding = 80;
    [Min(360), Tooltip("正文第一行相对居中标题向下的距离。")]
    public float titleBodyGap = 440;
    [Range(-250, 250), Tooltip("滚动结束时最后一行停留的垂直位置；0 为画面中央。")]
    public float finalLineY;
}
