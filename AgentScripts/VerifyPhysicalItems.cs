using System;using System.Linq;using System.Collections.Generic;using System.Threading.Tasks;
using UnityEditor;using UnityEngine;using UnityEngine.InputSystem;using UnityEngine.InputSystem.LowLevel;
using Sound2.LevelEditing;

public static class VerifyPhysicalItems
{
    static async Task Tick(int milliseconds=100){int frame=Time.frameCount;await Task.Delay(milliseconds);if(Time.frameCount<=frame)throw new Exception("Game loop stopped");}
    public static async Task<object> Main()
    {
        var checks=new List<string>();var failures=new List<string>();
        Action<bool,string> check=(ok,label)=>{checks.Add((ok?"PASS: ":"FAIL: ")+label);if(!ok)failures.Add(label);};
        var setup=UnityEngine.Object.FindAnyObjectByType<BaiYuTestScene>();var player=setup.player.GetComponent<PlayerController>();
        var pickup=player.GetComponent<PlayerPickup>();var motion=player.GetComponent<PlayerCameraMotion>();var capsule=player.GetComponent<CharacterController>();
        var location=player.transform.position;var rotation=pickup.viewTransform.rotation;
        bool moving=player.enabled,motionOn=motion.enabled,progressOn=setup.player.enabled,inputSuspended=player.InputSuspended;
        GameObject ground=null,wall=null,hazardObject=null;var fixtures=new List<GameObject>();
        try
        {
            check(UnityEngine.Object.FindObjectsByType<PlayerWallet>().Length==0,"No wallet/backpack component remains in the running scene");
            check(typeof(PlayerFloorProgression).GetProperty("CollectedResources")==null,"Progression has no resource inventory counter");
            check(!setup.ActiveContent.GetComponentsInChildren<InteractableObject>().Any(x=>x.name.Contains("switch") || x.InteractionHint.Contains("切换")),"No lightning interaction switch remains");
            check(setup.ActiveContent.GetComponentsInChildren<CoinPickup>().All(x=>x.GetComponent<PickupItem>()!=null)
                && setup.map.FindFloor(0).GetComponentsInChildren<MapResource>().All(x=>x.GetComponent<PickupItem>()!=null),"Coins and crystals use physical pickup items");
            player.enabled=false;motion.enabled=false;setup.player.enabled=false;player.Teleport(new Vector3(100,1.05f,100));pickup.viewTransform.rotation=Quaternion.identity;
            ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(100,-.5f,100);ground.transform.localScale=new Vector3(12,1,12);
            ground.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            var originals=new[]{AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Coin.prefab"),
                setup.map.FindFloor(0).GetComponentInChildren<MapResource>().gameObject,
                setup.ActiveContent.GetComponentsInChildren<PickupItem>().First(x=>x.name=="可搬运方块").gameObject};
            foreach(var original in originals)
            {
                var root=UnityEngine.Object.Instantiate(original);fixtures.Add(root);var item=root.GetComponent<PickupItem>();var body=root.GetComponent<Rigidbody>();
                foreach(var emitter in root.GetComponentsInChildren<SonarObjectEmitter>())emitter.automatic=false;
                item.pickupCooldown=.05f;body.isKinematic=true;body.useGravity=true;
                root.transform.SetPositionAndRotation(pickup.viewTransform.position+Vector3.forward*1.15f,Quaternion.identity);Physics.SyncTransforms();await Tick(80);
                check(pickup.TryPickUp(item),original.name+": picks up as an actual object");await Tick(100);
                check(pickup.TryThrow() && !body.isKinematic && body.linearVelocity.z>8,original.name+": left/throw path releases a moving body");
                await Tick(120);body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.isKinematic=true;
                body.position=pickup.viewTransform.position+Vector3.forward*1.15f;root.transform.position=body.position;Physics.SyncTransforms();await Tick(30);
                for(int repeat=0;repeat<3;repeat++)
                {
                    check(pickup.TryPickUp(item),original.name+": repeat pickup "+(repeat+1));await Tick(100);
                    bool placed=pickup.TryPlace();check(placed && pickup.HeldItem==null && !body.isKinematic && body.linearVelocity.sqrMagnitude<.00001f,
                        original.name+": placement "+(repeat+1)+" has zero throw impulse");
                    await Tick(220);check(root!=null && root.activeInHierarchy && body.position.y>-.05f,original.name+": remains in the world after placement");
                }
                root.SetActive(false);
            }
            var ball=fixtures[0];ball.SetActive(true);var ballItem=ball.GetComponent<PickupItem>();var ballBody=ball.GetComponent<Rigidbody>();
            ballBody.isKinematic=true;ball.transform.position=pickup.viewTransform.position+Vector3.forward*1.1f;ballBody.position=ball.transform.position;Physics.SyncTransforms();await Tick(70);
            check(pickup.TryPickUp(ballItem),"Inertia fixture held");await Tick(100);
            pickup.viewTransform.rotation=Quaternion.Euler(0,40,0);await Tick(90);
            float lag=Quaternion.Angle(ball.transform.rotation,pickup.holdPoint.rotation*Quaternion.Euler(ballItem.heldEulerAngles));
            float sway=Vector3.Distance(ball.transform.position,pickup.holdPoint.position);
            check(lag>.3f && lag<=pickup.maximumLagAngle+.1f && sway>.005f && sway<.3f,"Turning produces bounded, visible inertial lag (angle="+lag+", offset="+sway+")");
            await Tick(800);
            check(Vector3.Distance(ball.transform.position,pickup.holdPoint.position)<.004f && Quaternion.Angle(ball.transform.rotation,pickup.holdPoint.rotation)<.1f,"Inertia settles smoothly back to the hold anchor");
            check(Vector3.Distance(ballBody.position,ball.transform.position)<.001f,"Held visual and rigidbody poses stay synchronized");
            player.Teleport(player.transform.position+Vector3.right*3);pickup.viewTransform.rotation=Quaternion.identity;await Tick(50);
            check(Vector3.Distance(ball.transform.position,pickup.holdPoint.position)<.003f,"Teleport resets carry lag rather than dragging an item through the world");
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=ball.transform.position;wall.transform.localScale=Vector3.one*1.3f;Physics.SyncTransforms();
            check(!pickup.TryPlace() && pickup.HeldItem==ballItem,"Blocked placement keeps the item in hand");
            wall.SetActive(false);UnityEngine.Object.Destroy(wall);await Tick(80);

            var originalSettings=InputSystem.settings;var saved=UnityEngine.Object.Instantiate(originalSettings);var flags=originalSettings.hideFlags;
            var test=UnityEngine.Object.Instantiate(originalSettings);test.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            test.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings=test;var oldMouse=Mouse.current;var mouse=InputSystem.AddDevice<Mouse>();
            try
            {
                player.enabled=true;player.SetInputCaptured(true);await Tick(80);
                mouse.MakeCurrent();InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Right));await Tick(50);
                check(pickup.HeldItem==null && ball.activeInHierarchy && !ballBody.isKinematic && new Vector2(ballBody.linearVelocity.x,ballBody.linearVelocity.z).magnitude<.1f,"Actual right mouse input places the item without throwing or deleting it");
                InputSystem.QueueStateEvent(mouse,new MouseState());await Tick(50);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                if(originalSettings!=null){InputSystem.settings=originalSettings;UnityEngine.Object.Destroy(saved);}else{saved.hideFlags=flags;InputSystem.settings=saved;}
                UnityEngine.Object.Destroy(test);if(oldMouse!=null && oldMouse.added)oldMouse.MakeCurrent();player.enabled=false;
            }
            hazardObject=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BallLightning.prefab"));
            hazardObject.transform.position=new Vector3(100,0,104);var hazard=hazardObject.GetComponent<BallLightning>();
            hazard.damage=0;hazard.wanderSpeed=0;hazard.calmSeconds=.25f;hazard.activeSeconds=.25f;hazard.autoCycle=true;await Tick(50);hazard.SetState(LightningState.Calm);
            for(int i=0;i<10 && hazard.State!=LightningState.Active;i++)await Tick(60);
            check(hazard.State==LightningState.Active,"Lightning becomes active without any switch interaction");
            for(int i=0;i<10 && hazard.State!=LightningState.Calm;i++)await Tick(60);
            check(hazard.State==LightningState.Calm,"Lightning returns to calm automatically");
            check(hazard.GetComponentsInChildren<MeshRenderer>().Length==0 && hazard.GetComponentsInChildren<MeshFilter>().Length==0,"Lightning prefab has no solid render surface");
        }
        catch(Exception e){failures.Add(e.ToString());}
        finally
        {
            if(pickup.HeldItem!=null)pickup.HeldItem.Release(pickup.HeldItem.transform.position,Vector3.zero);
            foreach(var root in fixtures)if(root!=null)UnityEngine.Object.Destroy(root);
            if(ground!=null)UnityEngine.Object.Destroy(ground);if(wall!=null)UnityEngine.Object.Destroy(wall);if(hazardObject!=null)UnityEngine.Object.Destroy(hazardObject);
            player.Teleport(location);pickup.viewTransform.rotation=rotation;motion.enabled=motionOn;player.enabled=moving;setup.player.enabled=progressOn;player.SetInputCaptured(!inputSuspended);
        }
        var report=new {passed=failures.Count==0,checks,failures};System.IO.Directory.CreateDirectory("Logs/PhysicalItems");
        System.IO.File.WriteAllText("Logs/PhysicalItems/physical.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
