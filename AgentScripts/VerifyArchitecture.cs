using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
public static class VerifyArchitecture
{
    public static object Seeds()
    {
        if(Application.isPlaying)throw new Exception("Edit mode only");
        var method=typeof(AdventureMapBuilder).GetMethod("BuildArchitecturalPlan",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var signatures=new HashSet<string>();int count=0;
        foreach(int seed in new[]{0,1,42,20261114,987654321})foreach(int number in new[]{1,2,3,4,5,6,8})
        {
            var go=new GameObject("Temporary plan validation",typeof(LevelLayout),typeof(AdventureFloor));go.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var f=go.GetComponent<AdventureFloor>();f.number=number;f.layout=go.GetComponent<LevelLayout>();
                method.Invoke(null,new object[]{f,new System.Random(seed)});
                var meshes=LevelGeometry.Build(f.layout);foreach(var mesh in meshes.Values)UnityEngine.Object.DestroyImmediate(mesh);
                signatures.Add(number+":"+string.Join("|",f.layout.rooms.Select(r=>r.center.ToString("F5")+r.size.ToString("F5"))));count++;
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        var window=EditorWindow.GetWindow<LevelDesignerWindow>();
        bool modeFound=UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.PopupField<string>>(window.rootVisualElement).ToList().Any(p=>p.label=="生成模式" && p.choices.Contains("建筑平面 · 八层探险"));
        string result=$"{count} valid seeded plans / {signatures.Count} distinct plans; editor mode visible: {modeFound}";
        File.WriteAllText("Logs/EightFloorAdventure/seed-checks.txt",result);
        return new{passed=count==35 && signatures.Count==35 && modeFound,result};
    }
    public static object Main()
    {
        var map=UnityEngine.Object.FindAnyObjectByType<EightFloorAdventure>();var checks=new List<string>();var issues=new List<string>();
        void Check(bool ok,string text)=>checks.Add((ok?"PASS ":"FAIL ")+text);
        Check(map.generationMode==AdventureGenerationMode.Architectural,"Architectural generation mode persisted");
        Check(map.floors[5].theme=="医院" && map.floors[6].theme=="雪林","Hospital F6 / preserved snow F7");
        int Count(int floor,string prefab)=>map.FindFloor(floor).transform.Find("Props").Cast<Transform>().Count(t=>t.name.EndsWith(" · "+prefab));
        int beds=Count(6,"AdventureHospitalBed"),carts=Count(3,"ShoppingCart");
        Check(beds>=48,$"Hospital has {beds} beds in grouped wards and treatment rooms");
        Check(carts>=28,$"Supermarket has {carts} carts in parking, checkout and sales zones");
        var boss=map.FindFloor(8);var arena=boss.layout.FindRoom(boss.combatRoomId);var chase=boss.layout.rooms.Where(r=>r.use==RoomUse.Chase).ToArray();
        Check(arena!=null && arena.center==Vector2.zero && chase.Length>=6 && chase.All(r=>r.size.x*r.size.y<arena.size.x*arena.size.y*.3f),"One central large arena with seven smaller surrounding chase rooms");
        foreach(var f in map.floors)
        {
            if(f.number==7)continue;
            var l=f.layout;
            Check(l.architecturalPartitions && l.corridors.Any(c=>c.doorOnly),$"F{f.number}: actual internal partitions and shared-wall doors");
            bool overlap=false;
            for(int a=0;a<l.rooms.Count;a++)for(int b=a+1;b<l.rooms.Count;b++)
            {var r=l.rooms[a];var s=l.rooms[b];if(Mathf.Abs(r.center.x-s.center.x)<(r.size.x+s.size.x)/2-.01f && Mathf.Abs(r.center.y-s.center.y)<(r.size.y+s.size.y)/2-.01f)overlap=true;}
            Check(!overlap,$"F{f.number}: functional rooms do not overlap");
            var walls=l.generatedRoot.transform.Find("Walls").GetComponent<MeshCollider>();
            bool openings=true;
            foreach(var c in l.corridors.Where(c=>c.doorOnly))
            {
                var r=l.FindRoom(c.start.roomId);var p=r.Edge(c.start.edge,out var n);
                Vector3 from=l.World(p-n*.6f,1.1f),direction=l.transform.TransformDirection(new Vector3(n.x,0,n.y));
                if(walls.Raycast(new Ray(from,direction),out _,1.2f))openings=false;
            }
            Check(openings,$"F{f.number}: every shared-wall door is physically open at player height");
            int murals=Count(f.number,"WallMural");Check(murals>=5,$"F{f.number}: {murals} wall murals anchored beside doors");
            foreach(Transform prop in f.transform.Find("Props"))
            {
                string name=prop.name.Split(new[]{" · "},StringSplitOptions.None)[0];var r=l.rooms.Find(x=>x.name==name);if(r==null)continue;
                var renderers=prop.GetComponentsInChildren<MeshRenderer>();if(renderers.Length==0)continue;
                var bounds=renderers[0].bounds;foreach(var ren in renderers.Skip(1))bounds.Encapsulate(ren.bounds);
                Vector3 a=f.transform.InverseTransformPoint(bounds.min),b=f.transform.InverseTransformPoint(bounds.max);
                if(Mathf.Min(a.x,b.x)<r.center.x-r.size.x/2-.15f || Mathf.Max(a.x,b.x)>r.center.x+r.size.x/2+.15f ||
                    Mathf.Min(a.z,b.z)<r.center.y-r.size.y/2-.15f || Mathf.Max(a.z,b.z)>r.center.y+r.size.y/2+.15f)issues.Add($"F{f.number}: outside room {prop.name}");
            }
        }
        Check(map.elevators.All(e=>e.cabin.GetComponentsInChildren<Transform>(true).All(t=>!(t.name.StartsWith("Floor ") && t.name.EndsWith(" indicator")))),"No cabin floor-number geometry");
        Check(issues.Count==0,"Furniture footprints remain within their functional rooms");
        Directory.CreateDirectory("Logs/EightFloorAdventure");File.WriteAllLines("Logs/EightFloorAdventure/architecture-checks.txt",checks.Concat(issues));
        return new{passed=checks.All(x=>x.StartsWith("PASS")),beds,carts,props=map.floors.Sum(f=>f.transform.Find("Props").childCount),checks,issues};
    }
}
