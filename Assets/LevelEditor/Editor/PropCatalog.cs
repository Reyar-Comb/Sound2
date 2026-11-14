using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Sound2.LevelEditing.Editor
{
    public static class PropCatalog
    {
        static readonly Dictionary<string,string> Names = new (string id,string label)[] {
            ("TickingClock", "钟表"),
            ("PickupBouncyBall", "球"),
            ("PickupAlarmClock", "闹钟"),
            ("PickupGlassMug", "玻璃杯"),
            ("ResizableTable", "桌子"),
            ("Chair", "椅子"),
            ("ShoppingCart", "购物车"),
            ("LibraryBookshelf", "图书架"),
            ("WritingDesk", "书桌"),
            ("Computer", "电脑"),
            ("PickupWaterCup", "水杯"),
            ("PickupDeskLamp", "台灯"),
            ("Refrigerator", "冰箱"),
            ("Stove", "灶台"),
            ("TrashBin", "垃圾桶"),
            ("PickupCoffeeMug", "咖啡杯"),
            ("PickupCeramicVase", "陶瓷花瓶"),
            ("Television", "电视"),
            ("Radio", "收音机"),
            ("TapeRecorder", "录音机"),
            ("ServiceBell", "餐铃"),
            ("GameConsole", "游戏机"),
            ("HeartRateMonitor", "心率仪"),
            ("Ring", "戒指"),
            ("WallMural", "壁画"),
            ("WallMirror", "镜子"),
            ("WallFlowerBasket", "花篮"),
            ("WallCabinet", "橱柜"),
            ("WoodenDoor", "门"),
            ("BarCounter", "吧台"),
            ("Bed", "床"),
            ("Sofa", "沙发"),
            ("Counter", "工作台"),
            ("Shelf", "超市货架"),
            ("Checkout", "收银台"),
            ("HospitalBed", "病床"),
            ("WardCurtain", "病房隔帘"),
            ("Bedside", "床头柜"),
            ("Wardrobe", "衣柜"),
            ("Sink", "洗手池"),
            ("Toilet", "马桶"),
            ("Washer", "洗衣机"),
            ("ProduceBin", "果蔬陈列台"),
            ("Pine", "雪松"),
            ("SnowRock", "雪石"),
            ("Bench", "长椅"),
            ("Pillar", "竞技场立柱"),
            ("LevelElevator", "电梯入口")
        }.ToDictionary(e=>e.id,e=>e.label);
        public static string Name(GameObject prefab) => prefab == null ? "选择道具" : Names.TryGetValue(prefab.name,out var title) ? title : prefab.name;
        public static List<GameObject> All() => AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Prefabs"})
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(p=>p!=null && p.GetComponent<LevelProp>()!=null && !p.name.EndsWith("Broken"))
            .OrderBy(Name).ToList();
    }
}
