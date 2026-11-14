using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.AI;

public static class VerifyBaiYuTest
{
    static async Task Tick(int ms=120) {int frame=Time.frameCount;await Task.Delay(ms);if(Time.frameCount<=frame)throw new Exception("Player loop stopped");}
    public static async Task<object> Main()
    {
        var checks=new List<string>();var errors=new List<string>();
        Action<bool,string> check=(ok,label)=>{checks.Add((ok?"PASS: ":"FAIL: ")+label);};
        Application.LogCallback log=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);};
        Application.logMessageReceived+=log;
        var setup=UnityEngine.Object.FindAnyObjectByType<BaiYuTestScene>();
        var p=setup.player;var controller=p.GetComponent<PlayerController>();var capsule=p.GetComponent<CharacterController>();
        var wallet=p.GetComponent<PlayerWallet>();var vitality=p.GetComponent<PlayerVitality>();
        var interaction=p.GetComponent<PlayerInteraction>();var sonar=p.GetComponentInChildren<SphericalSonar>();
        Action<Vector3> teleport=pos=>{capsule.enabled=false;p.transform.position=pos;capsule.enabled=true;Physics.SyncTransforms();};
        try
        {
            controller.SetInputCaptured(true);await Tick();
            check(p.CurrentFloor==0 && setup.map.Floors.Count()==2,"Starts on F0 with F1 ready below the shaft");
            check(UnityEngine.Object.FindObjectsByType<Camera>().Length==1 && UnityEngine.Object.FindObjectsByType<SonarPulseField>().Length==1 && UnityEngine.Object.FindObjectsByType<BlackBoxProximitySource>().Length==1,"One player camera and one shared point/sonar field");
            check(controller.UsesGravity && p.GetComponent<PlayerCameraMotion>().enabled,"Gravity and camera motion enabled");
            check(!(bool)typeof(SphericalSonar).GetField("hud",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(sonar),"Developer panel hidden by default");
            check(wallet==null,"No wallet or resource inventory is attached");
            float initialRange=sonar.range;
            for(int f=0;f<=3;f++)
            {
                await Tick(250);
                var floor=setup.map.FindFloor(f);var content=setup.ActiveContent;
                var monster=content.GetComponentInChildren<PatrolMonster>();var hazard=content.GetComponentInChildren<BallLightning>();
                check(setup.PopulatedFloor==f && setup.Navigation.Ready && monster.NavigationReady && hazard.NavigationReady,"F"+f+": generated geometry and both actors have valid navigation");
                check(Mathf.Abs(hazard.Center.y-floor.transform.position.y-hazard.hoverHeight)<.12f,"F"+f+": lightning stays above its own floor");
                check(content.GetComponentsInChildren<CoinPickup>().Length==6+f*3,"F"+f+": coin count grows with floor seed");
                check(floor.Plan.Rooms.Count==5+f*2 && floor.GetComponentsInChildren<MapResource>(true).Length==6+f*3,"F"+f+": rooms and interactable resources grow");
                check(UnityEngine.Object.FindObjectsByType<RuntimePatrolNavigation>().Length==1 && UnityEngine.Object.FindObjectsByType<PatrolMonster>().Length==1 && BallLightning.Instances.Count==1,"F"+f+": previous floor AI and hazards are inactive");
                check(monster.GetComponent<MonsterScanEmitter>().EmitNow(),"F"+f+": monster emits red scan into shared field");
                var coin=content.GetComponentsInChildren<CoinPickup>().First(c=>c.name=="Coin 1");
                teleport(new Vector3(coin.transform.position.x,floor.transform.position.y+1.03f,coin.transform.position.z));await Tick(180);
                check(coin!=null && coin.gameObject.activeInHierarchy && coin.GetComponent<PickupItem>()!=null,"F"+f+": nearby coin stays as a physical prop");
                var resource=floor.GetComponentsInChildren<MapResource>().First();
                teleport(new Vector3(resource.transform.position.x,floor.transform.position.y+1.03f,resource.transform.position.z));
                check(resource.GetComponent<PickupItem>()!=null && resource.GetComponent<Rigidbody>()!=null,"F"+f+": crystal uses physical pickup");
                var pickup=p.GetComponent<PlayerPickup>();var ball=content.GetComponentsInChildren<PickupItem>().First(i=>i.name.StartsWith("可投掷球"));
                // The physical ball may have rolled since scene start; approach its actual position.
                teleport(new Vector3(ball.transform.position.x, floor.transform.position.y+1.03f, ball.transform.position.z-.5f));
                check(pickup.TryPickUp(ball) && pickup.HeldItem==ball && Mathf.Abs(pickup.holdPoint.localPosition.x)<.001f,"F"+f+": item can be held at view centre");
                check(pickup.TryThrow(),"F"+f+": held item throws using production physics");
                var relay=content.GetComponentsInChildren<PickupItem>().First(x=>x.name=="可搬运方块");
                check(relay!=null && hazard.autoCycle && hazard.GetComponentsInChildren<MeshRenderer>().Length==0,"F"+f+": carryable cube and autonomous mesh-free lightning");
                hazard.SetState(LightningState.Calm);
                check(content.GetComponentInChildren<HallHumEmitter>().IsPlaying,"F"+f+": shaft spatial audio beacon plays");
                if(f==3)break;
                setup.MoveToShaft();
                for(int i=0;i<70 && p.CurrentFloor==f;i++)await Tick(80);
                check(p.CurrentFloor==f+1,"Actual gravity fall crosses shaft into F"+(f+1));
                if(p.CurrentFloor!=f+1)throw new Exception("Shaft transition stalled");
                for(int i=0;i<30 && !capsule.isGrounded;i++)await Tick(80);
                check(capsule.isGrounded,"Player lands on F"+(f+1));
                check(sonar.range<initialRange && p.Difficulty>1 && p.GetComponent<PlayerSubtitles>().FloorText.text=="F"+(f+1),"Floor changes update difficulty, sonar and floor UI");
            }
            var active=setup.map.FindFloor(p.CurrentFloor);
            check(setup.map.Floors.Count()==3 && setup.map.FindFloor(0)==null && setup.map.FindFloor(1)==null,"Old floors retire while previous/current/next are retained");
            var lightning=setup.ActiveContent.GetComponentInChildren<BallLightning>();
            lightning.wanderSpeed=0;lightning.autoCycle=false;lightning.SetState(LightningState.Active);vitality.RestoreHealth();
            teleport(lightning.transform.position+new Vector3(1,1.03f,0));await Tick(1300);
            check(vitality.Health<vitality.maximumHealth,"Underground active lightning damages the player at the correct height");
            lightning.SetState(LightningState.Calm);vitality.RestoreHealth();teleport(active.transform.TransformPoint(new Vector3(0,1.03f,0)));await Tick();
            var label=p.GetComponentsInChildren<TMPro.TextMeshProUGUI>().First(x=>x.name=="Health");
            check(label.font==GameUiTheme.Current.textFont && label.text.Contains("生命") && !label.text.Contains("资源") && !label.text.Contains("金币"),"Status HUD uses game font and health only");
            var rects=new Vector4[PlayerUiPointMask.Capacity];var opts=new Vector4[PlayerUiPointMask.Capacity];
            check(PlayerUiPointMask.Collect(Camera.main,rects,opts)>=2,"Visible status and floor UI participate in point exclusion");
            ScreenCapture.CaptureScreenshot("Logs/BaiYuTest/F3.png");
        }
        catch(Exception e){errors.Add(e.ToString());}
        finally {Application.logMessageReceived-=log;}
        return new {passed=errors.Count==0 && checks.All(c=>c.StartsWith("PASS")),checks,errors,currentFloor=p.CurrentFloor};
    }
}
