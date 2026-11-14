using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Sound2.LevelEditing.Editor
{
    public static partial class AdventureMapBuilder
    {
        public const string ScenePath = "Assets/Scenes/BaiYu-Test.unity";
        public static readonly string[] Themes = { "家", "学校", "超市", "餐厅", "图书馆", "医院", "雪林", "Boss房" };
        static int Template(int floor) => floor == 6 ? 7 : floor == 7 ? 6 : floor;
        static readonly Vector2[] Centers = {
            new(-28,-36),new(0,-36),new(28,-36),new(28,-12),new(28,12),new(28,36),
            new(0,36),new(-28,36),new(-28,12),new(-28,-12),new(0,-12),new(0,12) };
        static readonly string[][] Names = {
            new[]{"玄关","客厅","餐厅","厨房","储藏室","上行门厅","洗衣房","客房","主卧","书房","家庭活动室","内庭"},
            new[]{"校门厅","一年级教室","美术教室","实验室","音乐教室","上行门厅","教师办公室","阅览室","自习室","器材室","中庭","多功能教室"},
            new[]{"入口大厅","果蔬区","冷藏区","饮料区","日用品区","上行门厅","仓储区","员工休息室","收银区","烘焙区","食品货架区","生活货架区"},
            new[]{"迎宾厅","散座区","双人用餐区","包间","备餐间","上行门厅","冷库","后厨","洗消间","员工休息室","中央餐厅","宴会厅"},
            new[]{"借阅大厅","文学书库","历史书库","哲学书库","档案室","上行门厅","研究室","期刊阅览室","儿童阅览室","自习室","开放书库","中央阅览厅"},
            new[]{"林口驿站","松林空地","积雪石径","林间营地","密林空地","山间电梯站","冻湖岸边","巡林小屋","倒木林地","避风营地","雪原","林心空地"},
            new[]{"门诊大厅","候诊区","诊室","治疗室","药房","上行门厅","护士站","普通病房","观察病房","医护休息室","康复区","重症病房"},
            new[]{"终层入口","回声前厅","东侧回廊","观测室","隔离室","封闭终点","记录室","西侧回廊","准备室","设备室","Boss竞技场","核心控制室"}
        };
        static Material ink, blackInk, preview;
        static readonly Dictionary<int,string> meshStores = new();

        [MenuItem("Tools/Sound2/八层上行地图/重构 BaiYu-Test")]
        public static void RebuildBaiYuTest()
            => Rebuild(AdventureGenerationMode.DungeonLoops);

        [MenuItem("Tools/Sound2/八层上行地图/建筑平面模式重建 BaiYu-Test")]
        public static void RebuildArchitecture() => Rebuild(AdventureGenerationMode.Architectural);

        public static void Rebuild(AdventureGenerationMode mode)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("请先打开 BaiYu-Test 场景。");
            // A complete scene backup protects scene-local hand edits when intentionally regenerating.
            Directory.CreateDirectory("Logs/AdventureBackups");
            EditorSceneManager.SaveScene(scene, "Logs/AdventureBackups/BaiYu-Test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var old = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EightFloorAdventure>(true)).FirstOrDefault();
            int seed = old != null ? old.seed : 20261114;
            meshStores.Clear();
            if(old!=null)foreach(var f in old.floors)if(f!=null)meshStores[f.number]=f.layout.geometryAsset;
            if (old != null) Object.DestroyImmediate(old.gameObject);
            foreach (var legacy in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelMap>(true)).ToArray()) Object.DestroyImmediate(legacy.gameObject);
            foreach (var fixture in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BaiYuTestScene>(true)).ToArray()) Object.DestroyImmediate(fixture.gameObject);
            var player = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerFloorProgression>(true)).First();
            var map = new GameObject("BaiYu · 八层上行探险", typeof(EightFloorAdventure)).GetComponent<EightFloorAdventure>();
            map.seed = seed; map.player = player; map.generationMode = mode;
            map.profile = AssetDatabase.LoadAssetAtPath<FloorProgressionProfile>("Assets/Resources/BaiYuTestFloorProgression.asset");
            ConfigureProfile(map.profile); player.profile = map.profile;
            EnsureMaterials(); AdventureFurniture.Ensure(preview);
            var floors = new List<AdventureFloor>();
            for (int n = 1; n <= 8; n++)
            {
                var floor = BuildFloor(map, n);
                if (n > 1)
                {
                    var previous = floors[n - 2];
                    Vector3 dock = previous.layout.World(previous.exitDock) + Vector3.up * 14;
                    floor.transform.position += dock - floor.layout.World(floor.entryDock);
                }
                floors.Add(floor);
            }
            map.floors = floors.ToArray();
            var lifts = new List<OneWayElevator>();
            for (int n = 1; n < 8; n++) lifts.Add(BuildLift(map, n));
            map.elevators = lifts.ToArray();
            player.transform.SetPositionAndRotation(map.floors[0].SpawnWorld, Quaternion.identity);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            foreach (var floor in floors) LevelBuilder.Persist(floor.layout, ScenePath);
            EditorUtility.SetDirty(map.profile); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            ShowFloor(map, 1);
        }

        static void ConfigureProfile(FloorProgressionProfile profile)
        {
            profile.overrides.Clear(); profile.defaultEntryText = "第 {floor} 层 · 寻找白色上行电梯。";
            for (int i = 1; i <= 8; i++)
            {
                var rule = new FloorRule { floor=i, difficulty=1+(i-1)*.15f, sonarRangeMultiplier=1, sonarAfterglowMultiplier=1 };
                rule.entrySubtitles.Add(new SubtitleCue("第 " + i + " 层 · " + Themes[i-1], 4));
                rule.entrySubtitles.Add(new SubtitleCue(i == 8 ? "已经到达终层。前往中央竞技场，留意红色回声。" : "探索房间与回环通道，寻找白色电梯。电梯只能上行，到站后无法返回。", 6));
                if (i == 1) rule.entrySubtitles.Add(new SubtitleCue("WASD 移动；左键拿起或交互，右键放下，Enter 发出声波。", 6));
                profile.overrides.Add(rule);
            }
        }
        static void EnsureMaterials()
        {
            Directory.CreateDirectory("Assets/LevelEditor/Materials/Adventure"); AssetDatabase.Refresh();
            ink = Material("ElevatorPaper", "Sound2/Elevator White Ink", Color.white);
            blackInk = Material("ElevatorInk", "Sound2/Elevator White Ink", Color.black);
            preview = Material("FurniturePreview", "Universal Render Pipeline/Lit", new Color(.55f,.64f,.67f));
        }
        static Material Material(string name, string shader, Color color)
        {
            string path="Assets/LevelEditor/Materials/Adventure/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",color); EditorUtility.SetDirty(mat); return mat;
        }

        static AdventureFloor BuildFloor(EightFloorAdventure map, int number)
        {
            var go = new GameObject("F"+number+" · "+Themes[number-1], typeof(LevelLayout), typeof(AdventureFloor));
            go.transform.SetParent(map.transform,false); go.transform.localPosition=Vector3.up*((number-1)*14);
            go.transform.localRotation=Quaternion.Euler(0,(number-1)%2*180,0);
            var layout=go.GetComponent<LevelLayout>();
            var floor=go.GetComponent<AdventureFloor>();floor.number=number;floor.theme=Themes[number-1];floor.layout=layout;
            int template = Template(number);
            floor.spawn=new Vector3(-28,1.1f,-36);
            layout.wallHeight=template==6?5:number==8?6:3.6f;layout.openSky=template==6;
            // Each floor owns its mesh store; mesh names are intentionally shared by the geometry builder.
            layout.geometryAsset=meshStores.TryGetValue(number,out var store)?store:"";
            var random=new System.Random(map.seed+template*7919);
            if (map.generationMode == AdventureGenerationMode.Architectural && number != 7)
            {
                BuildArchitecturalPlan(floor, random);
                LevelBuilder.Rebuild(layout);
                FurnishArchitecture(floor, random);
                BuildEncounters(map, floor);
                return floor;
            }
            for(int i=0;i<Centers.Length;i++)
            {
                var size=RoomSize(template,i,random);
                layout.rooms.Add(new LevelRoom { id="f"+number+"-r"+i,name=Names[template-1][i],center=Centers[i],size=size,
                    shape=template==6 && i!=0 && i!=5 && i!=7 ? RoomShape.Circle : RoomShape.Rectangle });
            }
            for(int i=0;i<10;i++) Connect(layout,i,(i+1)%10,template);
            foreach(var edge in new[]{(1,10),(10,11),(11,6),(9,10),(11,4)}) Connect(layout,edge.Item1,edge.Item2,template);
            // Retail aisles and institutional circulation have additional cross-routes; the house stays intimate.
            if(template==2 || template==3 || template==7)Connect(layout,10,3,template);
            if(template==3 || template==5 || template==8)Connect(layout,8,11,template);
            // Vestibule throats terminate at the cabin doorway, never cap it with a generated wall.
            // Their narrow end caps are removed by the dedicated doorway cuts in LevelGeometry.
            if(number>1) AddLiftThroat(layout,0,new Vector2(-28,-44.5f));
            if(number<8) AddLiftThroat(layout,5,new Vector2(28,44.5f));
            LevelBuilder.Rebuild(layout);
            Furnish(floor,random);
            BuildEncounters(map,floor);
            return floor;
        }
        static Vector2 RoomSize(int floor,int i,System.Random random)
        {
            if(i==0 || i==5)return new Vector2(12,12);
            if(floor==6)return Vector2.one*(i==10?23:17+random.Next(5));
            float x=floor switch {1=>10,2=>18,3=>20,4=>16,5=>18,7=>16,_=>19};
            float z=floor switch {1=>10,2=>14,3=>18,4=>15,5=>17,7=>14,_=>18};
            if(i>=10){x+=2;z+=2;}
            if((floor==1 && i==1)||(floor==4 && i>=10)){x=20;z=18;}
            return new Vector2(x+random.Next(3),z+random.Next(2));
        }
        static void Connect(LevelLayout layout,int a,int b,int floor)
        {
            var ra=layout.rooms[a];var rb=layout.rooms[b];
            var ca=ra.NearestEdge(rb.center);var cb=rb.NearestEdge(ra.center);
            Vector2 middle=(ra.center+rb.center)*.5f;
            if(floor==6){var direction=(rb.center-ra.center).normalized;middle+=new Vector2(-direction.y,direction.x)*2;}
            float width=floor==1?2:floor==3?4:floor==6?3.5f:floor==7?3.4f:3;
            if(floor==4 && a>=4 && b>=4 && a<10 && b<10)width=2;
            layout.corridors.Add(new LevelCorridor {name=ra.name+" ↔ "+rb.name,curved=floor==6,width=width,
                start=new RoomConnection{roomId=ra.id,edge=ca},end=new RoomConnection{roomId=rb.id,edge=cb},
                points=new List<Vector2>{ra.Edge(ca,out _),middle,rb.Edge(cb,out _)} });
        }
        static void AddLiftThroat(LevelLayout layout,int room,Vector2 end)
        {
            var r=layout.rooms[room];float edge=r.NearestEdge(end);
            layout.corridors.Add(new LevelCorridor{name="电梯专用门廊",width=2.6f,start=new RoomConnection{roomId=r.id,edge=edge},
                points=new List<Vector2>{r.Edge(edge,out _),end}});
            layout.doorways.Add(new LevelDoorway{center=end,size=new Vector2(2.65f,.8f)});
        }

        static void Furnish(AdventureFloor f,System.Random random)
        {
            var props=new GameObject("Props");props.transform.SetParent(f.transform,false);
            for(int i=0;i<f.layout.rooms.Count;i++)
            {
                var room=f.layout.rooms[i];
                if(i==0 || i==5)
                {
                    Put(f,i,"Chair",-4,2,90);Put(f,i,"WritingDesk",3,2,180);
                    Put(f,i,"TapeRecorder",3,2,180,DeskTop(f,i,3,2));
                    continue;
                }
                float x=room.size.x*.5f-2.1f,z=room.size.y*.5f-2;
                switch(Template(f.number))
                {
                    case 1:
                        if(i==3){Put(f,i,"Refrigerator",-x,z);Put(f,i,"Stove",x,z);Put(f,i,"AdventureCounter",x,1,90);}
                        else if(i==7||i==8){Put(f,i,"AdventureBed",-x,z-1);Put(f,i,"WritingDesk",x,z,180);Put(f,i,"PickupAlarmClock",x,z,0,DeskTop(f,i,x,z));}
                        else if(i==1||i==10){Put(f,i,"AdventureSofa",-x,2,90);Put(f,i,"Television",x,2,-90);TableSet(f,i,2,-3);}
                        else if(i==9){Put(f,i,"LibraryBookshelf",-x,z);Put(f,i,"WritingDesk",x,z,180);Put(f,i,"Computer",x,z,180,DeskTop(f,i,x,z));}
                        else {TableSet(f,i,-x,z-1);Put(f,i,"WallCabinet",x,z,180,1.5f);}
                        Put(f,i,"PickupWaterCup",x,-z);break;
                    case 2:
                        if(i==1||i==2||i==3||i==11)
                        {for(int row=0;row<3;row++)for(int col=0;col<2;col++){float px=col==0?-4:4,pz=-4+row*4;Put(f,i,"WritingDesk",px,pz);Put(f,i,"Chair",px,pz-1.2f);}}
                        else {TableSet(f,i,-x,z-1);Put(f,i,"LibraryBookshelf",x,z,180);Put(f,i,"Computer",-x,z-1,0,DeskTop(f,i,-x,z-1));}
                        Put(f,i,"PickupBouncyBall",x,-z);Put(f,i,"Radio",-x,-z);break;
                    case 3:
                        if(i==8){for(int k=0;k<3;k++){Put(f,i,"AdventureCheckout",-6+k*6,2);Put(f,i,"ShoppingCart",-6+k*6,-3);}}
                        else if(i==2){for(int k=0;k<4;k++)Put(f,i,"Refrigerator",-6+k*4,z,180);Put(f,i,"ShoppingCart",x,-z);}
                        else {for(int k=0;k<3;k++)Put(f,i,"AdventureShelf",-6+k*6,2);Put(f,i,"ShoppingCart",-x,-z);}
                        for(int k=0;k<3;k++)Put(f,i,"PickupWaterCup",-6+k*6,1.5f,0,i==2?0:i==8?1.1f:1.32f);break;
                    case 4:
                        if(i==4||i==7||i==8){Put(f,i,"Stove",-x,z);Put(f,i,"Refrigerator",x,z);Put(f,i,"AdventureCounter",-x,-z);Put(f,i,"PickupGlassMug",-x,-z,0,1.1f);}
                        else if(i==6){for(int k=0;k<3;k++)Put(f,i,"Refrigerator",-4+k*4,z);}
                        else {foreach(float px in new[]{-4f,4f})foreach(float pz in new[]{-4f,4f})TableSet(f,i,px,pz);}
                        Put(f,i,"ServiceBell",x,-z);break;
                    case 5:
                        if(i==7||i==8||i==9||i==11){TableSet(f,i,-4,3);TableSet(f,i,4,3);Put(f,i,"LibraryBookshelf",-x,-z);Put(f,i,"LibraryBookshelf",x,-z);}
                        else {for(int row=0;row<2;row++)for(int col=0;col<3;col++)Put(f,i,"LibraryBookshelf",-6+col*6,-4+row*8);}
                        Put(f,i,"PickupDeskLamp",x,z);Put(f,i,"TapeRecorder",-x,z);break;
                    case 6:
                        if(i==7){Put(f,i,"AdventureBed",-3,3);Put(f,i,"WritingDesk",3,3);Put(f,i,"Radio",3,3,0,DeskTop(f,i,3,3));}
                        else
                        {
                            for(int k=0;k<10;k++)
                            {
                                float a=(k+.5f)*Mathf.PI*2/10;float radius=room.size.x*.5f-2;
                                // Cardinal door approaches remain clear; scenery occupies the diagonal banks.
                                if(Mathf.Abs(Mathf.Sin(a))<.3f||Mathf.Abs(Mathf.Cos(a))<.3f)continue;
                                Put(f,i,k%3==0?"AdventureSnowRock":"AdventurePine",Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,random.Next(360));
                            }
                            Put(f,i,"AdventureBench",-4,3,20);Put(f,i,"Radio",-4,3,0,.7f);
                        }
                        Put(f,i,"PickupBouncyBall",3,3);break;
                    case 7:
                        if(i==7||i==8||i==11||i==3)
                        {foreach(float px in new[]{-4f,4f}){Put(f,i,"AdventureHospitalBed",px,3);Put(f,i,"HeartRateMonitor",px+1.4f,4);Put(f,i,"AdventureWardCurtain",px,-2);}}
                        else if(i==1){for(int row=0;row<3;row++)for(int col=0;col<2;col++)Put(f,i,"Chair",-4+col*8,-3+row*3);}
                        else {Put(f,i,"WritingDesk",-x,z);Put(f,i,"Computer",-x,z,0,DeskTop(f,i,-x,z));Put(f,i,"AdventureShelf",x,z-2,90);}
                        Put(f,i,"PickupWaterCup",x,-z);break;
                    case 8:
                        if(i==10){foreach(float px in new[]{-7f,7f})foreach(float pz in new[]{-6f,6f})Put(f,i,"AdventurePillar",px,pz);}
                        else {Put(f,i,"AdventureCounter",-x,z);Put(f,i,"HeartRateMonitor",x,z);Put(f,i,"Radio",-x,z,0,1.1f);}
                        Put(f,i,"PickupBouncyBall",x,-z);break;
                }
            }
        }
        static void TableSet(AdventureFloor f,int room,float x,float z)
        {
            Put(f,room,"ResizableTable",x,z);Put(f,room,"Chair",x,z-1.4f);Put(f,room,"Chair",x,z+1.4f,180);
            Put(f,room,"PickupCoffeeMug",x+.35f,z,0,DeskTop(f,room,x,z));
        }
        static float DeskTop(AdventureFloor f,int room,float x,float z)
        {
            Physics.SyncTransforms();float highest=.75f;
            Vector3 p=f.layout.World(f.layout.rooms[room].center+new Vector2(x,z),4);
            foreach(var hit in Physics.RaycastAll(p,Vector3.down,4))
                if(hit.collider.transform.IsChildOf(f.transform.Find("Props")))highest=Mathf.Max(highest,f.transform.InverseTransformPoint(hit.point).y);
            return highest+.03f;
        }
        static GameObject Put(AdventureFloor floor,int room,string prefab,float x,float z,float yaw=0,float height=0)
        {
            string path=prefab.StartsWith("Adventure")?"Assets/Prefabs/Adventure/"+prefab+".prefab":"Assets/Prefabs/"+prefab+".prefab";
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(asset==null)throw new InvalidOperationException("Missing furniture: "+path);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,floor.gameObject.scene);
            go.transform.SetParent(floor.transform.Find("Props"),false);
            go.name=floor.layout.rooms[room].name+" · "+asset.name;
            go.transform.localRotation=Quaternion.Euler(0,yaw,0);
            Vector2 pos=floor.layout.rooms[room].center+new Vector2(x,z);
            go.transform.localPosition=new Vector3(pos.x,height,pos.y);
            // Use actual renderer bottoms so centered pickup pivots and grounded furniture both rest on their support.
            float min=float.PositiveInfinity;
            foreach(var renderer in go.GetComponentsInChildren<Renderer>())min=Mathf.Min(min,renderer.bounds.min.y);
            if(!float.IsInfinity(min))go.transform.position+=Vector3.up*(floor.transform.position.y+height+.02f-min);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            return go;
        }

        static void BuildEncounters(EightFloorAdventure map,AdventureFloor f)
        {
            var root=new GameObject("Encounters");root.transform.SetParent(f.transform,false);root.SetActive(false);f.encounters=root;
            var nav=root.AddComponent<RuntimePatrolNavigation>();nav.geometryRoot=f.transform;
            var route=new GameObject("Patrol route");route.transform.SetParent(root.transform,false);
            var points=new List<Transform>();
            foreach(int i in Enumerable.Range(1, f.layout.rooms.Count - 1))
            {var p=new GameObject(f.layout.rooms[i].name);p.transform.SetParent(route.transform,false);var center=f.layout.rooms[i].center;p.transform.localPosition=new Vector3(center.x,.1f,center.y);points.Add(p.transform);}
            var actor=new GameObject(f.number==8?"Boss · 回声守望者":"Invisible patrol monster");actor.transform.SetParent(root.transform,false);
            var spawn=(f.layout.FindRoom(f.combatRoomId) ?? f.layout.rooms[Mathf.Min(3,f.layout.rooms.Count-1)]).center;actor.transform.localPosition=new Vector3(spawn.x,.1f,spawn.y);
            actor.AddComponent<NavMeshAgent>().enabled=false;
            var monster=actor.AddComponent<PatrolMonster>();monster.navigation=nav;monster.patrolPoints=points.ToArray();
            monster.patrolSpeed=f.number==8?2:1+(f.number-1)*.08f;monster.investigateSpeed=f.number==8?3.2f:1.8f;
            actor.AddComponent<MonsterScanEmitter>().player=map.player.transform;
            if(f.number>1)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BallLightning.prefab");
                var lightning=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
                var p=f.layout.rooms[7].center;lightning.transform.localPosition=new Vector3(p.x,0,p.y);
                var hazard=lightning.GetComponent<BallLightning>();hazard.navigation=nav;hazard.heightRelativeToSpawn=true;
                hazard.player=map.player.GetComponent<PlayerVitality>();hazard.wanderRadius=3;
                PrefabUtility.RecordPrefabInstancePropertyModifications(hazard);PrefabUtility.RecordPrefabInstancePropertyModifications(lightning.transform);
            }
        }

        static OneWayElevator BuildLift(EightFloorAdventure map,int source)
        {
            var floor=map.FindFloor(source);
            var go=new GameObject("电梯 "+source+" → "+(source+1),typeof(OneWayElevator));go.transform.SetParent(map.transform,false);
            go.transform.SetPositionAndRotation(floor.layout.World(floor.exitDock),floor.transform.rotation*Quaternion.Euler(0,180,0));
            var lift=go.GetComponent<OneWayElevator>();lift.adventure=map;lift.sourceFloor=source;lift.destinationFloor=source+1;lift.rise=14;
            var cabin=new GameObject("Cabin");cabin.transform.SetParent(go.transform,false);lift.cabin=cabin.transform;
            // +Z points out of the cabin. White panels render after the sonar composition with real depth testing.
            Cube(cabin.transform,"Floor",new(0,-.1f,0),new(4.4f,.2f,6));
            Cube(cabin.transform,"Roof",new(0,3.2f,0),new(4.4f,.2f,6));
            Cube(cabin.transform,"Back",new(0,1.55f,-2.9f),new(4.4f,3.3f,.2f));
            Cube(cabin.transform,"Left",new(-2.1f,1.55f,0),new(.2f,3.3f,6));
            Cube(cabin.transform,"Right",new(2.1f,1.55f,0),new(.2f,3.3f,6));
            Cube(cabin.transform,"Left jamb",new(-1.75f,1.5f,2.85f),new(.9f,3,.25f));
            Cube(cabin.transform,"Right jamb",new(1.75f,1.5f,2.85f),new(.9f,3,.25f));
            Cube(cabin.transform,"Lintel",new(0,2.9f,2.85f),new(3,.4f,.25f));
            Cube(cabin.transform,"Back handrail",new(0,1.05f,-2.73f),new(3.6f,.055f,.08f));
            foreach(float x in new[]{-1.985f,1.985f})
            {
                foreach(float y in new[]{.025f,1.05f,3.075f})Cube(cabin.transform,"Side ink rail",new(x,y,-.05f),new(.025f,.025f,5.5f)).GetComponent<Renderer>().sharedMaterial=blackInk;
                foreach(float z in new[]{-2.77f,-1f,1f})Cube(cabin.transform,"Side ink seam",new(x,1.55f,z),new(.025f,3.1f,.025f)).GetComponent<Renderer>().sharedMaterial=blackInk;
            }
            foreach(float y in new[]{.025f,3.075f})Cube(cabin.transform,"Back ink edge",new(0,y,-2.77f),new(4,.025f,.025f)).GetComponent<Renderer>().sharedMaterial=blackInk;
            foreach(float x in new[]{-1f,0,1f})Cube(cabin.transform,"Floor ink seam",new(x,.006f,0),new(.018f,.008f,5.5f)).GetComponent<Renderer>().sharedMaterial=blackInk;
            for(int i=-1;i<=1;i++)Cube(cabin.transform,"Back panel "+i,new(i*1.3f,1.8f,-2.785f),new(1.27f,1.8f,.02f));
            lift.cabinLeft=Cube(cabin.transform,"Cabin door L",new(-.65f,1.35f,2.79f),new(1.3f,2.7f,.1f)).transform;
            lift.cabinRight=Cube(cabin.transform,"Cabin door R",new(.65f,1.35f,2.79f),new(1.3f,2.7f,.1f)).transform;
            var lower=new GameObject("Lower landing");lower.transform.SetParent(go.transform,false);
            var upper=new GameObject("Upper landing");upper.transform.SetParent(go.transform,false);upper.transform.localPosition=Vector3.up*14;
            Landing(lower.transform,out lift.lowerLeft,out lift.lowerRight);Landing(upper.transform,out lift.upperLeft,out lift.upperRight);
            var outside=Cube(lower.transform,"CALL · "+source+" → "+(source+1),new(1.05f,1.25f,3.18f),new(.2f,.4f,.12f)).AddComponent<ElevatorButton>();outside.elevator=lift;
            var inside=Cube(cabin.transform,"UP · "+(source+1),new(1.97f,1.25f,1.15f),new(.12f,.4f,.3f)).AddComponent<ElevatorButton>();inside.elevator=lift;inside.inside=true;
            AddIndicator(lower.transform,source+1,new Vector3(0,2.98f,3.23f),Quaternion.identity);
            AddIndicator(upper.transform,source+1,new Vector3(0,2.98f,3.23f),Quaternion.identity);
            // A clearly printed black up-arrow identifies both buttons against the white panel.
            ButtonArrow(outside.transform,false);ButtonArrow(inside.transform,true);
            // Closed, collision-backed shaft; no route can fall between floors after the lift departs.
            Cube(go.transform,"Shaft rear",new(0,8,-3.2f),new(4.8f,18,.25f));
            Cube(go.transform,"Shaft left",new(-2.4f,8,0),new(.25f,18,6.6f));
            Cube(go.transform,"Shaft right",new(2.4f,8,0),new(.25f,18,6.6f));
            Cube(go.transform,"Shaft front between landings",new(0,8.5f,3.25f),new(4.8f,11,.25f));
            foreach(Transform part in go.transform)if(part.name.StartsWith("Shaft"))part.GetComponent<Renderer>().sharedMaterial=blackInk;
            lift.motor=go.AddComponent<AudioSource>();lift.motor.spatialBlend=1;lift.motor.loop=true;lift.motor.playOnAwake=false;
            var beacon=new GameObject("电梯声源");beacon.transform.SetParent(lower.transform,false);beacon.transform.localPosition=new Vector3(0,1,3.5f);beacon.AddComponent<HallHumEmitter>().volume=.08f;
            return lift;
        }
        static void Landing(Transform root,out Transform left,out Transform right)
        {
            Cube(root,"门框 L",new(-1.75f,1.5f,3.05f),new(.9f,3.2f,.3f));
            Cube(root,"门框 R",new(1.75f,1.5f,3.05f),new(.9f,3.2f,.3f));
            Cube(root,"门楣",new(0,2.95f,3.05f),new(4.4f,.5f,.3f));
            Cube(root,"门槛",new(0,-.1f,3.05f),new(4.4f,.2f,.4f));
            left=Cube(root,"门 L",new(-.65f,1.35f,3.06f),new(1.3f,2.7f,.1f)).transform;
            right=Cube(root,"门 R",new(.65f,1.35f,3.06f),new(1.3f,2.7f,.1f)).transform;
        }
        static GameObject Cube(Transform parent,string name,Vector3 p,Vector3 scale)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=ink;return go;
        }
        static void ButtonArrow(Transform button,bool sideways)
        {
            var icon=new GameObject("UP arrow");icon.transform.SetParent(button.parent,false);
            icon.transform.localPosition=button.localPosition+(sideways?Vector3.left:Vector3.forward)*.075f;
            icon.transform.localRotation=sideways?Quaternion.Euler(0,-90,0):Quaternion.identity;
            var a=Cube(icon.transform,"Arrow L",new(-.035f,.02f,0),new(.018f,.11f,.015f));a.transform.localRotation=Quaternion.Euler(0,0,-40);a.GetComponent<Renderer>().sharedMaterial=blackInk;
            var b=Cube(icon.transform,"Arrow R",new(.035f,.02f,0),new(.018f,.11f,.015f));b.transform.localRotation=Quaternion.Euler(0,0,40);b.GetComponent<Renderer>().sharedMaterial=blackInk;
            Cube(icon.transform,"Stem",new(0,-.04f,0),new(.018f,.12f,.015f)).GetComponent<Renderer>().sharedMaterial=blackInk;
            foreach(var col in icon.GetComponentsInChildren<Collider>())Object.DestroyImmediate(col);
        }
        static void AddIndicator(Transform parent,int digit,Vector3 position,Quaternion rotation)
        {
            var root=new GameObject("Floor "+digit+" indicator");root.transform.SetParent(parent,false);root.transform.localPosition=position;root.transform.localRotation=rotation;
            Cube(root.transform,"Paper label",Vector3.zero,new(.7f,.38f,.025f));
            int[] masks={63,6,91,79,102,109,125,7,127,111};
            Vector3[] centers={new(0,.12f,.02f),new(.09f,.06f,.02f),new(.09f,-.06f,.02f),new(0,-.12f,.02f),new(-.09f,-.06f,.02f),new(-.09f,.06f,.02f),new(0,0,.02f)};
            for(int i=0;i<7;i++)if((masks[digit%10]&(1<<i))!=0)
                Cube(root.transform,"Ink segment",new Vector3(-centers[i].x,centers[i].y,centers[i].z),i==0||i==3||i==6?new(.16f,.024f,.012f):new(.024f,.10f,.012f)).GetComponent<Renderer>().sharedMaterial=blackInk;
            foreach(var col in root.GetComponentsInChildren<Collider>())Object.DestroyImmediate(col);
        }
        public static void ShowFloor(EightFloorAdventure map,int number)
        {
            if(Application.isPlaying)return;
            foreach(var f in map.floors) {SceneVisibilityManager.instance.Show(f.gameObject,true);if(f.number!=number)SceneVisibilityManager.instance.Hide(f.gameObject,true);}
            foreach(var lift in map.elevators){SceneVisibilityManager.instance.Show(lift.gameObject,true);if(lift.sourceFloor!=number && lift.destinationFloor!=number)SceneVisibilityManager.instance.Hide(lift.gameObject,true);}
            var floor=map.FindFloor(number);Selection.activeGameObject=floor.gameObject;
            EditorWindow.GetWindow<LevelDesignerWindow>("关卡编辑器").Bind(floor.layout);
        }
    }
}
