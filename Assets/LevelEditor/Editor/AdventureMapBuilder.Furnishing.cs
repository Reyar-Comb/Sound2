using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public static partial class AdventureMapBuilder
    {
        static void FurnishArchitecture(AdventureFloor f, System.Random random)
        {
            new GameObject("Props").transform.SetParent(f.transform,false);
            for(int i=0;i<f.layout.rooms.Count;i++)
            {
                var r=f.layout.rooms[i];float hx=r.size.x/2,hz=r.size.y/2;
                void P(string name,float x,float z,float yaw=0,float height=0) => Put(f,i,name,x,z,yaw,height);
                void Desk(float x,float z,bool computer=false)
                {P("WritingDesk",x,z);P("Chair",x,z-1.15f);P(computer?"Computer":"PickupDeskLamp",x,z,0,DeskTop(f,i,x,z));}
                switch(r.use)
                {
                    case RoomUse.Hall:
                        // Narrow circulation remains completely clear. Wide lobbies have furniture at their banks.
                        if(r.size.x<7 || r.size.y<5)break;
                        P("AdventureBench",-hx+2, hz-1,180);P("AdventureBench",hx-2,-hz+1);
                        if(r.size.x>18)
                        {Desk(hx-4,hz-2,true);P("ServiceBell",hx-4.7f,hz-2,0,DeskTop(f,i,hx-4,hz-2));}
                        else{P("AdventureBedside",-hx+1,-hz+1);P("TapeRecorder",-hx+1,-hz+1,0,.78f);}
                        if(f.number==6 && i==0)
                            foreach(float x in new[]{-14f,-10,-6,6,10,14}){P("Chair",x,-2);P("Chair",x,2,180);}
                        break;
                    case RoomUse.Living:
                        P("AdventureSofa",-hx+1,0,-90);P("Television",hx-1,0,90);
                        P("AdventureBedside",-hx+1,hz-1);P("PickupCeramicVase",-hx+1,hz-1,0,.78f);
                        if(r.size.x>12){TableSet(f,i,3,-hz+3);P("AdventureSofa",-3,-hz+1,180);}
                        break;
                    case RoomUse.Bedroom:
                        P("AdventureBed",-hx+2,hz-1.6f);P("AdventureBedside",-hx+3.25f,hz-1.1f);
                        P("PickupAlarmClock",-hx+3.25f,hz-1.1f,0,.78f);P("AdventureWardrobe",hx-1.4f,hz-.6f);
                        Desk(hx-1.8f,-hz+2);break;
                    case RoomUse.Kitchen:
                        P("Refrigerator",-hx+1.2f,hz-1.2f);P("Stove",hx-1.5f,hz-1);
                        for(float x=-hx+4;x<hx-2;x+=3)P("AdventureCounter",x,hz-.8f);
                        P("AdventureSink",-hx+1,-hz+1.2f);P("TrashBin",-hx+1,-hz+2.4f);
                        P("AdventureCounter",hx-2,-hz+1);P("PickupCoffeeMug",hx-2,-hz+1,0,1.12f);break;
                    case RoomUse.Utility:
                        P("AdventureWasher",-hx+1,hz-1);P("AdventureSink",hx-1,hz-1);
                        if(hx>4){P("AdventureCounter",-hx+2,-hz+1);P("PickupWaterCup",-hx+2,-hz+1,0,1.12f);}
                        else P("AdventureWardrobe",-hx+1.4f,-hz+.6f,180);break;
                    case RoomUse.Bathroom:
                        P("AdventureToilet",-hx+1,hz-1);P("AdventureSink",hx-1,hz-.8f);
                        P("TrashBin",-hx+1,-hz+1);break;
                    case RoomUse.Study: case RoomUse.Office: case RoomUse.Clinic:
                        Desk(-hx+2,hz-2,true);P("LibraryBookshelf",hx-1.8f,hz-.8f);
                        P("AdventureWardrobe",hx-1.5f,-hz+.6f,180);
                        if(r.use==RoomUse.Clinic){P("AdventureHospitalBed",hx-3,0,90);P("HeartRateMonitor",hx-2,2);}
                        break;
                    case RoomUse.Classroom:
                        // Two banks of desks with a central aisle and a transverse route between both exits.
                        for(float x=-hx+2;x<=hx-1.8f;x+=3.1f)
                        for(float z=-hz+2;z<=hz-2;z+=2.7f)
                        {if(Mathf.Abs(x)<1.6f || Mathf.Abs(z)<1.7f)continue;P("WritingDesk",x,z);P("Chair",x,z-1.05f);}
                        Desk(0,hz-1.4f);P("LibraryBookshelf",-hx+2,hz-.8f);P("Radio",hx-2,hz-1);
                        break;
                    case RoomUse.Ward:
                        foreach(float x in new[]{-8f,-3f,3f,8f})foreach(float sign in new[]{-1f,1f})
                        {
                            float z=sign*(hz-1.6f), yaw=sign>0?0:180;
                            P("AdventureHospitalBed",x,z,yaw);P("AdventureBedside",x+1.15f,sign*(hz-.8f),yaw);
                            P("PickupWaterCup",x+1.15f,sign*(hz-.8f),0,.78f);
                            if(random.Next(3)==0 || r.name.Contains("重症"))P("HeartRateMonitor",x-1.2f,sign*(hz-1));
                        }
                        foreach(float x in new[]{-5.5f,5.5f})foreach(float sign in new[]{-1f,1f})P("AdventureWardCurtain",x,sign*(hz-1.6f),90);
                        break;
                    case RoomUse.Treatment:
                        foreach(float x in new[]{-6f,6f}){P("AdventureHospitalBed",x,hz-1.6f);P("HeartRateMonitor",x+1.3f,hz-1);P("AdventureWardCurtain",x,-hz+2);}
                        P("AdventureSink",-hx+1,-hz+1);Desk(hx-3,-hz+2,true);break;
                    case RoomUse.CartBay:
                        // Four parking lanes: 16 carts, no cart in the door approach.
                        foreach(float x in new[]{-6f,-4.5f,4.5f,6f})foreach(float z in new[]{-2.7f,-.9f,.9f,2.7f})P("ShoppingCart",x,z);
                        break;
                    case RoomUse.Checkout:
                        foreach(float x in new[]{-9f,-5f,3f,7f}){P("AdventureCheckout",x,1);P("ShoppingCart",x,-1.8f);P("ServiceBell",x-.6f,1,0,1.16f);}
                        break;
                    case RoomUse.Produce:
                        foreach(float x in new[]{-4.5f,4.5f})for(float z=-hz+4;z<hz-2;z+=6)P("AdventureProduceBin",x,z);
                        for(int k=0;k<6;k++)P("ShoppingCart",k%2==0?-hx+1:hx-1,-hz+3+(k/2)*6);
                        break;
                    case RoomUse.Shelves:
                        bool books=f.number==5;
                        for(float x=-hx+2.5f;x<=hx-2;x+=5)
                        for(float z=-hz+3;z<=hz-2;z+=5)
                        {
                            if(Mathf.Abs(x)<2 || Mathf.Abs(z)<2.2f)continue;
                            P(books?"LibraryBookshelf":"AdventureShelf",x,z);
                        }
                        if(!books && f.number==3){P("ShoppingCart",-hx+1.1f,-hz+1.6f);P("ShoppingCart",hx-1.1f,hz-1.6f);}
                        break;
                    case RoomUse.ColdStore:
                        for(float z=-hz+2;z<hz-1;z+=3)
                        {if(Mathf.Abs(z)<2)continue;P("Refrigerator",-hx+1,z,-90);P("Refrigerator",hx-1,z,90);}
                        break;
                    case RoomUse.Dining: case RoomUse.Reading:
                        if(hx<5 || hz<4){Desk(-hx+2,0);P("LibraryBookshelf",hx-1.5f,hz-.6f);break;}
                        for(float x=-hx+3;x<=hx-2.5f;x+=5.5f)
                        for(float z=-hz+3;z<=hz-2.5f;z+=5.5f)
                        {if(Mathf.Abs(x)<2 || Mathf.Abs(z)<2.6f)continue;TableSet(f,i,x,z);}
                        if(r.use==RoomUse.Reading)P("LibraryBookshelf",-hx+2,hz-.8f);
                        break;
                    case RoomUse.Arena:
                        foreach(float x in new[]{-11f,11f})foreach(float z in new[]{-11f,11f})P("AdventurePillar",x,z);
                        break;
                    case RoomUse.Chase:
                        // Cover islands support a loop around the room, and leave both wall openings clear.
                        P("AdventureCounter",-2,2,90);P("AdventureCounter",2,-2,90);
                        P("HeartRateMonitor",-hx+1.5f,hz-1.5f);P("Radio",2,-2,0,1.12f);break;
                }
                if(!r.openPlan)DecorateWalls(f,i);
                // Loose sound makers are deliberate interaction opportunities, kept away from passage centres.
                if(hx>=3 && hz>=2.5f && r.use!=RoomUse.Ward && r.use!=RoomUse.Bathroom)
                    P(f.number==2||f.number==8?"PickupBouncyBall":"PickupWaterCup",hx-1.1f,-hz+1.1f);
            }
        }

        static void DecorateWalls(AdventureFloor f,int index)
        {
            var r=f.layout.rooms[index];
            int count=0;
            foreach(float edge in new[]{.32f,.43f,.82f,.93f,.07f,.18f,.57f,.68f})
            {
                Vector2 p=r.Edge(edge,out var normal);
                float sideLength=(Mathf.Abs(normal.x)>.5f?r.size.y:r.size.x);
                if(sideLength<5)continue;
                bool doorway=f.layout.corridors.Any(c=>new[]{c.start,c.end}.Any(e=>e.roomId==r.id && Vector2.Distance(r.Edge(e.edge,out _),p)<c.width*.5f+1.6f));
                if(doorway)continue;
                string name=r.use==RoomUse.Bathroom?"WallMirror":r.use==RoomUse.Kitchen?"WallCabinet":"WallMural";
                if (!MountWallProp(f,index,name,p-r.center-normal*.25f,normal,1.9f)) continue;
                if(++count>=(r.size.x*r.size.y>150?3:1))break;
            }
        }
        static bool MountWallProp(AdventureFloor floor, int index, string name, Vector2 start, Vector2 outward, float height)
        {
            var layout = floor.layout;
            var room = layout.rooms[index];
            var wall = layout.generatedRoot.transform.Find("Walls").GetComponent<MeshCollider>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
            var prop = asset.GetComponent<LevelProp>();
            var origin = layout.World(room.center + start, height);
            var direction = layout.transform.TransformDirection(new Vector3(outward.x, 0, outward.y));
            Physics.SyncTransforms();
            if (!wall.Raycast(new Ray(origin, direction), out var hit, Mathf.Max(room.size.x, room.size.y))
                || Vector3.Dot(hit.normal, -direction) < .95f) return false;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, floor.gameObject.scene);
            go.transform.SetParent(floor.transform.Find("Props"), false);
            go.name = room.name + " · " + name;
            var rotation = Quaternion.LookRotation(hit.normal, layout.transform.up);
            var anchor = rotation * Vector3.Scale(prop.surfaceAnchor, go.transform.lossyScale);
            go.transform.SetPositionAndRotation(hit.point + hit.normal * prop.surfaceOffset - anchor, rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            return true;
        }
    }
}
