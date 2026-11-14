using System;using System.Collections.Generic;using System.Linq;using System.Threading.Tasks;using UnityEngine;using Sound2.LevelEditing;
public static class VerifyPickupAndShaftRegression
{
 static void Check(bool ok,string label,List<string> checks){checks.Add((ok?"PASS: ":"FAIL: ")+label);if(!ok)throw new Exception(label);}
 static async Task Tick(int ms=120){int frame=Time.frameCount;await Task.Delay(ms);if(Time.frameCount<=frame)throw new Exception("Player loop stopped");}
 public static async Task<object> Main(){
 var checks=new List<string>();var errors=new List<string>();var setup=UnityEngine.Object.FindAnyObjectByType<BaiYuTestScene>();
 var progress=setup.player;var ctl=progress.GetComponent<PlayerController>();var pickup=progress.GetComponent<PlayerPickup>();
 var ball=setup.ActiveContent.GetComponentsInChildren<PickupItem>().First(i=>i.name.StartsWith("可投掷球"));var body=ball.GetComponent<Rigidbody>();
 float speed=pickup.throwSpeed,up=pickup.throwUpwardSpeed;GameObject wall=null;Action<FloorRule> monitor=null;
 try{
 Check(progress.CurrentFloor==0,"Fixture starts on F0",checks);ctl.SetInputCaptured(false);
 ctl.Teleport(setup.map.FindFloor(0).transform.TransformPoint(new Vector3(0,1.03f,0)));
 body.position=pickup.viewTransform.position+pickup.viewTransform.forward*1.2f;body.linearVelocity=Vector3.zero;Physics.SyncTransforms();await Tick(1300);
 pickup.throwSpeed=0;pickup.throwUpwardSpeed=0;
 for(int i=0;i<8;i++){
 Check(pickup.TryPickUp(ball),"Same floor pickup "+(i+1),checks);await Tick(150);
 Check(ball.gameObject.activeInHierarchy && pickup.HeldItem==ball && Vector3.Distance(ball.transform.position,pickup.holdPoint.position)<.002f && Vector3.Distance(body.position,ball.transform.position)<.002f,"Pickup "+(i+1)+": visible hierarchy, hold anchor and physics pose agree",checks);
 Check(Mathf.Abs(ball.CollisionRadius-.2f)<.002f,"Pickup "+(i+1)+": radius remains actual ball radius",checks);
 Check(pickup.TryThrow(),"Same floor throw "+(i+1),checks);await Tick(1250);
 Check(!body.isKinematic && ball.GetComponent<Collider>().enabled && ball.CanPickUp,"Throw "+(i+1)+": physics/collider/cooldown restore",checks);
 }
 Check(pickup.TryPickUp(ball),"Pick ball up for wall check",checks);
 wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=pickup.viewTransform.position+pickup.viewTransform.forward*.6f;wall.transform.rotation=pickup.viewTransform.rotation;wall.transform.localScale=new Vector3(2,2,.1f);Physics.SyncTransforms();await Tick();
 Check(Vector3.Distance(ball.transform.position,pickup.viewTransform.position)<.38f && !Physics.ComputePenetration(ball.GetComponent<Collider>(),ball.transform.position,ball.transform.rotation,wall.GetComponent<Collider>(),wall.transform.position,wall.transform.rotation,out _,out _),"Held ball retracts before a wall instead of disappearing inside it",checks);
 wall.SetActive(false);UnityEngine.Object.Destroy(wall);await Tick();
 int rejected=0;bool injectRecovery=true;
 monitor=rule=>{if(rule.floor==0)return;if(progress.AwaitingLanding && !progress.TryDescend(setup.map.FindFloor(rule.floor).Exit))rejected++;if(injectRecovery){injectRecovery=false;ctl.Teleport(setup.map.FindFloor(rule.floor).transform.position+Vector3.down);}};
 progress.FloorChanged+=monitor;
 for(int f=0;f<3;f++){
 setup.MoveToShaft();for(int t=0;t<80 && progress.CurrentFloor==f;t++)await Tick(70);
 Check(progress.CurrentFloor==f+1,"One shaft advances exactly one floor: F"+(f+1),checks);
 for(int t=0;t<60 && progress.AwaitingLanding;t++)await Tick(70);
 Check(!progress.AwaitingLanding && ctl.GetComponent<CharacterController>().isGrounded,"Destination F"+(f+1)+" lands before the next descent is armed",checks);
 await Tick(500);Check(progress.CurrentFloor==f+1,"No repeated floor change while staying at the arrival point",checks);
 Check(pickup.HeldItem==ball && ball.gameObject.activeInHierarchy,"Held ball survives floor retirement",checks);
 if(f==0){Check(pickup.TryThrow() && ball.gameObject.activeInHierarchy && ball.GetComponentInParent<SeededFloor>()==setup.map.FindFloor(1),"Carried ball releases under the active destination floor",checks);await Tick(1250);Check(pickup.TryPickUp(ball),"Carried ball can be collected again after release",checks);}
 }
 Check(rejected==3,"All three attempted airborne chain descents are rejected",checks);
 Check(ball!=null && ball.gameObject.activeInHierarchy && pickup.TryThrow(),"F0 ball remains usable after original F0 is destroyed",checks);
 Check(setup.map.Floors.All(f=>f.GetComponentsInChildren<BoxCollider>().Any(c=>c.name=="Arrival landing support")),"Every retained floor has solid arrival support",checks);
 }catch(Exception e){errors.Add(e.ToString());}
 finally{pickup.throwSpeed=speed;pickup.throwUpwardSpeed=up;if(monitor!=null)progress.FloorChanged-=monitor;if(wall!=null)UnityEngine.Object.Destroy(wall);ctl.SetInputCaptured(true);}
 return new{passed=errors.Count==0,checks,errors};}
}

