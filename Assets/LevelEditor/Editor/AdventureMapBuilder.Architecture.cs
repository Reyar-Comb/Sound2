using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public static partial class AdventureMapBuilder
    {
        // Dimensions are metres. Templates describe real circulation and zoning rather than isolated cells.
        static void BuildArchitecturalPlan(AdventureFloor f, System.Random random)
        {
            var l = f.layout; l.architecturalPartitions = true;
            int Room(string name, RoomUse use, float x0, float z0, float x1, float z1, bool open = false)
            {
                int index = l.rooms.Count;
                l.rooms.Add(new LevelRoom { id = $"f{f.number}-r{index}", name = name, use = use,
                    center = new Vector2((x0+x1)/2, (z0+z1)/2), size = new Vector2(x1-x0,z1-z0), openPlan = open });
                return index;
            }
            void Door(int a, int b, float width = 2.2f, float fraction = .5f) => SharedDoor(l,a,b,width,fraction);
            int entry = 0, exit = 0;
            switch (f.number)
            {
                case 1:
                    Room("玄关与鞋柜",RoomUse.Hall,-16,-14,-8,-8);
                    Room("客餐厅",RoomUse.Living,-8,-14,8,-2);
                    Room("厨房",RoomUse.Kitchen,8,-14,16,-6);
                    Room("洗衣家务间",RoomUse.Utility,8,-6,16,-2);
                    Room("过厅",RoomUse.Hall,-16,-8,-8,-2);
                    Room("家庭走廊",RoomUse.Hall,-16,-2,16,1);
                    Room("主卧",RoomUse.Bedroom,-16,1,-7,9);
                    Room("次卧",RoomUse.Bedroom,-7,1,0,9);
                    Room("家庭起居室",RoomUse.Living,0,1,9,9);
                    Room("书房",RoomUse.Study,9,1,16,9);
                    Room("主卧衣帽间",RoomUse.Utility,-16,9,-7,14);
                    Room("家庭卫浴",RoomUse.Bathroom,-7,9,0,14);
                    Room("阳光厅",RoomUse.Reading,0,9,9,14);
                    exit = Room("上行门厅",RoomUse.Hall,9,9,16,14);
                    foreach(var e in new[]{(0,1),(0,4),(4,5),(1,5),(1,2),(2,3),(3,5),(5,6),(5,7),(5,8),(5,9),(6,10),(7,11),(12,11),(8,12),(9,13),(12,13)}) Door(e.Item1,e.Item2,1.8f);
                    f.entryDock=new(-12,-19.5f);f.exitDock=new(12.5f,19.5f);
                    break;
                case 2: case 5: case 6:
                    bool hospital=f.number==6, library=f.number==5;
                    float half=hospital?28:library?24:20, length=hospital?32:library?30:28;
                    float inner=length-8, lane= hospital?2:library?3:2;
                    Room(hospital?"挂号与候诊大厅":library?"借还与检索大厅":"校园入口大厅",RoomUse.Hall,-half,-length,half,-inner);
                    int central=Room(hospital?"医疗主走廊":library?"中央检索走廊":"教学主走廊",RoomUse.Hall,-lane,-inner,lane,inner);
                    int west=Room("西侧疏散走廊",RoomUse.Hall,-half,-inner,-half+4,inner);
                    int east=Room("东侧疏散走廊",RoomUse.Hall,half-4,-inner,half,inner);
                    string[] left = hospital?new[]{"普通病房 A","普通病房 B","观察病房","重症监护病房"}:library?new[]{"文学书库","历史书库","社会科学书库","档案阅览室"}:new[]{"一年级教室","二年级教室","科学实验室","美术教室"};
                    string[] right = hospital?new[]{"门诊诊室","治疗与换药室","普通病房 C","普通病房 D"}:library?new[]{"儿童阅览室","期刊阅览室","安静自习室","研究阅览室"}:new[]{"教师办公室","音乐教室","三年级教室","自习教室"};
                    for(int row=0;row<4;row++)
                    {
                        float z0=-inner+row*inner/2,z1=z0+inner/2;
                        int a=Room(left[row],hospital?RoomUse.Ward:library?RoomUse.Shelves:RoomUse.Classroom,-half+4,z0,-lane,z1);
                        int b=Room(right[row],hospital?(row==0?RoomUse.Clinic:row==1?RoomUse.Treatment:RoomUse.Ward):library?RoomUse.Reading:row==0?RoomUse.Office:RoomUse.Classroom,lane,z0,half-4,z1);
                        Door(a,central,2.4f);Door(a,west,2.4f);Door(b,central,2.4f);Door(b,east,2.4f);
                    }
                    int rear=Room(hospital?"护士站与医护后厅":library?"文献服务后厅":"教务服务后厅",RoomUse.Hall,-half,inner,half-12,length);
                    exit=Room("上行门厅",RoomUse.Hall,half-12,inner,half,length);
                    Door(entry,central,3);Door(entry,west,2.6f);Door(entry,east,2.6f);
                    Door(rear,central,3);Door(rear,west,2.6f);Door(exit,east,2.6f);Door(rear,exit,2.6f);
                    f.entryDock=new(-half+6,-length-5.5f);f.exitDock=new(half-6,length+5.5f);
                    break;
                case 3:
                    Room("入口与购物车停放区",RoomUse.CartBay,-26,-30,-10,-22,true);
                    Room("收银结账区",RoomUse.Checkout,-10,-30,14,-22,true);
                    Room("服务台",RoomUse.Office,14,-30,26,-22,true);
                    Room("果蔬生鲜区",RoomUse.Produce,-26,-22,-10,18,true);
                    Room("食品与日用品货架区",RoomUse.Shelves,-10,-22,14,18,true);
                    Room("冷藏陈列区",RoomUse.ColdStore,14,-22,26,18,true);
                    Room("收货与仓储",RoomUse.Shelves,-26,18,-10,30);
                    Room("后场冷库",RoomUse.ColdStore,-10,18,0,30);
                    Room("员工休息室",RoomUse.Office,0,18,14,30);
                    exit=Room("上行门厅",RoomUse.Hall,14,18,26,30);
                    foreach(var e in new[]{(0,1),(1,2),(0,3),(1,4),(2,5),(3,4),(4,5),(3,6),(4,7),(4,8),(5,9),(6,7),(7,8),(8,9)})Door(e.Item1,e.Item2,3);
                    f.entryDock=new(-18,-35.5f);f.exitDock=new(20,35.5f);
                    break;
                case 4:
                    Room("迎宾与等候区",RoomUse.Hall,-22,-26,22,-18);
                    Room("西侧用餐区",RoomUse.Dining,-22,-18,-2,6);
                    Room("服务主通道",RoomUse.Hall,-2,-18,2,18);
                    Room("东侧用餐区",RoomUse.Dining,2,-18,22,6);
                    Room("家庭包间",RoomUse.Dining,-22,6,-2,18);
                    Room("宴会包间",RoomUse.Dining,2,6,22,18);
                    Room("后厨热菜区",RoomUse.Kitchen,-22,18,-10,30);
                    Room("洗消与备餐",RoomUse.Utility,-10,18,2,30);
                    Room("冷库与食材储藏",RoomUse.ColdStore,2,18,10,30);
                    exit=Room("后勤上行门厅",RoomUse.Hall,10,18,22,30);
                    foreach(var e in new[]{(0,1),(0,2),(0,3),(1,2),(2,3),(1,4),(3,5),(2,4),(2,5),(4,6),(4,7),(5,8),(5,9),(6,7),(7,8),(8,9)})Door(e.Item1,e.Item2,2.4f);
                    f.entryDock=new(-16,-31.5f);f.exitDock=new(16,35.5f);
                    break;
                case 8:
                    Room("终层准备大厅",RoomUse.Hall,-28,-32,28,-24);
                    int arena=Room("中央对战大厅",RoomUse.Arena,-16,-16,16,16);
                    f.combatRoomId=l.rooms[arena].id;
                    int south=Room("南侧环廊",RoomUse.Hall,-28,-24,28,-16);
                    int north=Room("北侧环廊",RoomUse.Hall,-28,16,28,24);
                    int w=Room("西侧环廊",RoomUse.Hall,-20,-16,-16,16);
                    int e2=Room("东侧环廊",RoomUse.Hall,16,-16,20,16);
                    var chase=new List<int>();
                    for(int row=0;row<2;row++)
                    {
                        float z0=-16+row*16;
                        chase.Add(Room("西翼追逐房 "+(row+1),RoomUse.Chase,-32,z0,-20,z0+16));
                        chase.Add(Room("东翼追逐房 "+(row+1),RoomUse.Chase,20,z0,32,z0+16));
                    }
                    chase.Add(Room("北翼追逐房 A",RoomUse.Chase,-28,24,-8,36));
                    chase.Add(Room("北翼追逐房 B",RoomUse.Chase,-8,24,8,36));
                    chase.Add(Room("北翼追逐房 C",RoomUse.Chase,8,24,28,36));
                    Door(0,south,3.2f);Door(south,arena,4);Door(north,arena,4);Door(w,arena,3.2f);Door(e2,arena,3.2f);
                    Door(south,w,2.6f);Door(south,e2,2.6f);Door(north,w,2.6f);Door(north,e2,2.6f);
                    for(int k=0;k<4;k++){Door(chase[k],k%2==0?w:e2,2.4f,.24f);Door(chase[k],k%2==0?w:e2,2.4f,.76f);}
                    Door(chase[0],chase[2],2.4f);Door(chase[1],chase[3],2.4f);
                    for(int k=4;k<7;k++){Door(chase[k],north,2.4f,.25f);Door(chase[k],north,2.4f,.75f);}
                    Door(chase[4],chase[5],2.4f);Door(chase[5],chase[6],2.4f);
                    f.entryDock=new(-22,-37.5f);f.exitDock=Vector2.zero;
                    break;
            }
            // Seeded dimensional variation keeps the construction zoning and shared-wall relationships intact.
            var scale=new Vector2(.96f+(float)random.NextDouble()*.08f,.96f+(float)random.NextDouble()*.08f);
            foreach(var room in l.rooms){room.center=Vector2.Scale(room.center,scale);room.size=Vector2.Scale(room.size,scale);}
            foreach(var corridor in l.corridors)for(int k=0;k<corridor.points.Count;k++)corridor.points[k]=Vector2.Scale(corridor.points[k],scale);
            // Keep the vestibule and cabin setback constant even when the building dimensions vary.
            f.entryDock=new Vector2(f.entryDock.x*scale.x,(f.entryDock.y+5.5f)*scale.y-5.5f);
            if(f.number<8)f.exitDock=new Vector2(f.exitDock.x*scale.x,(f.exitDock.y-5.5f)*scale.y+5.5f);
            var start=l.rooms[entry].center; f.spawn=new(start.x,1.1f,start.y);
            if(f.number>1)AddLiftThroat(l,entry,f.entryDock+Vector2.up*3);
            if(f.number<8)AddLiftThroat(l,exit,f.exitDock-Vector2.up*3);
        }

        static void SharedDoor(LevelLayout layout,int a,int b,float width,float fraction)
        {
            var ra=layout.rooms[a];var rb=layout.rooms[b];
            Vector2 amin=ra.center-ra.size/2,amax=ra.center+ra.size/2,bmin=rb.center-rb.size/2,bmax=rb.center+rb.size/2,point;
            float lo,hi;
            if(Mathf.Abs(amax.x-bmin.x)<.01f || Mathf.Abs(bmax.x-amin.x)<.01f)
            {lo=Mathf.Max(amin.y,bmin.y);hi=Mathf.Min(amax.y,bmax.y);point=new(Mathf.Abs(amax.x-bmin.x)<.01f?amax.x:amin.x,Mathf.Lerp(lo,hi,fraction));}
            else if(Mathf.Abs(amax.y-bmin.y)<.01f || Mathf.Abs(bmax.y-amin.y)<.01f)
            {lo=Mathf.Max(amin.x,bmin.x);hi=Mathf.Min(amax.x,bmax.x);point=new(Mathf.Lerp(lo,hi,fraction),Mathf.Abs(amax.y-bmin.y)<.01f?amax.y:amin.y);}
            else throw new InvalidOperationException("房间不共墙："+ra.name+" / "+rb.name);
            if(hi-lo<width+.2f)throw new InvalidOperationException("共墙长度不足："+ra.name+" / "+rb.name);
            layout.corridors.Add(new LevelCorridor { name=ra.name+" ↔ "+rb.name,doorOnly=true,width=width,
                start=new RoomConnection {roomId=ra.id,edge=ra.NearestEdge(point)},end=new RoomConnection {roomId=rb.id,edge=rb.NearestEdge(point)},
                points=new List<Vector2>{point,point} });
        }
    }
}
