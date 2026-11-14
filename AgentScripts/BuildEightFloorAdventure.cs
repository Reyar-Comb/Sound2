using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEngine;
using System.Linq;
public static class BuildEightFloorAdventure
{
    public static object Main()
    {
        AdventureMapBuilder.RebuildBaiYuTest();
        var map=Object.FindAnyObjectByType<EightFloorAdventure>();
        return new {floors=map.floors.Select(f=>new{f.number,f.theme,rooms=f.layout.rooms.Count,corridors=f.layout.corridors.Count,props=f.transform.Find("Props").childCount}).ToArray(),elevators=map.elevators.Length};
    }
    public static object Architecture()
    {
        string Snow(EightFloorAdventure m)
        {
            var f=m.floors.First(x=>x.theme=="雪林");
            return string.Join("|",f.layout.rooms.Select(r=>$"{r.name}:{r.center}:{r.size}:{r.shape}")) +
                string.Join("|",f.layout.corridors.Select(c=>$"{c.name}:{c.width}:{c.curved}:"+string.Join(";",c.points))) +
                string.Join("|",f.transform.Find("Props").Cast<Transform>().Select(p=>$"{p.name}:{p.localPosition}:{p.localRotation}"));
        }
        var old=Object.FindAnyObjectByType<EightFloorAdventure>();string before=Snow(old);
        AdventureMapBuilder.RebuildArchitecture();
        var map=Object.FindAnyObjectByType<EightFloorAdventure>();
        bool preserved=before==Snow(map);
        System.IO.File.WriteAllText("Logs/EightFloorAdventure/snow-preservation.txt",preserved?"PASS Snow local geometry, corridors and furniture unchanged":"FAIL Snow changed");
        return new {snowPreserved=preserved,mode=map.generationMode.ToString(),floors=map.floors.Select(f=>new{f.number,f.theme,rooms=f.layout.rooms.Count,doors=f.layout.corridors.Count,props=f.transform.Find("Props").childCount}).ToArray()};
    }
}
