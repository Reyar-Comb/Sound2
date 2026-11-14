using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class VerifyWorldObjects
{
    static async Task Tick(int ms=150) {int frame=Time.frameCount;await Task.Delay(ms);if(Time.frameCount<=frame)throw new Exception("Player loop stopped");}
    public static async Task<object> Main()
    {
        var checks=new List<string>();var failures=new List<string>();
        Action<bool,string> check=(pass,label)=>{checks.Add((pass?"PASS: ":"FAIL: ")+label);if(!pass)failures.Add(label);};
        var player=UnityEngine.Object.FindAnyObjectByType<PlayerController>();var capsule=player.GetComponent<CharacterController>();
        var wallet=player.GetComponent<PlayerWallet>();var vitality=player.GetComponent<PlayerVitality>();
        var monster=UnityEngine.Object.FindAnyObjectByType<PatrolMonster>();var monsterAgent=monster.GetComponent<NavMeshAgent>();
        var monsterScan=monster.GetComponent<MonsterScanEmitter>();
        var existingHazards=UnityEngine.Object.FindObjectsByType<BallLightning>();
        var existingCoins=UnityEngine.Object.FindObjectsByType<CoinPickup>();
        var emitters=UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>();var automatic=Array.ConvertAll(emitters,e=>e.automatic);
        var hazardEnabled=Array.ConvertAll(existingHazards,e=>e.enabled);var coinEnabled=Array.ConvertAll(existingCoins,e=>e.enabled);
        bool suspended=player.InputSuspended,brain=monster.enabled,scan=monsterScan.enabled;
        var position=player.transform.position;var monsterPosition=monster.transform.position;
        var snapshot=wallet.Capture();float health=vitality.Health,priority=monster.soundPriorityMemory;
        GameObject coinRoot=null,lightRoot=null,wall=null;
        Action<Vector3> teleport=p=>{capsule.enabled=false;player.transform.position=p;capsule.enabled=true;Physics.SyncTransforms();};
        try
        {
            player.SetInputCaptured(false);monster.enabled=false;monsterScan.enabled=false;
            foreach(var e in emitters)e.automatic=false;foreach(var h in existingHazards)h.enabled=false;foreach(var c in existingCoins)c.enabled=false;
            teleport(new Vector3(50,0,50));
            coinRoot=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Coin.prefab"));coinRoot.transform.position=new Vector3(100,0,100);
            var coin=coinRoot.GetComponent<CoinPickup>();var cp=coinRoot.GetComponent<SonarObjectEmitter>();cp.automatic=false;
            coin.bobPeriod=.8f;coin.bobAmplitude=.2f;coin.bodyRevealSeconds=.12f;
            await Tick();check(!coin.BodyVisible,"Coin body stays hidden between its own pulses");
            cp.EmitNow();check(coin.BodyVisible,"Coin pulse immediately reveals the gold body");
            await Tick(180);check(!coin.BodyVisible,"Coin body disappears after the configured short reveal");
            float y0=coin.visual.position.y;var rotation=coin.visual.rotation;await Tick(120);float y1=coin.visual.position.y;await Tick(120);float y2=coin.visual.position.y;
            check(Mathf.Abs(y1-y0)>.005f && Mathf.Abs((y2-y1)-(y1-y0))>.005f,"Coin bobs along a nonlinear smooth curve");
            check(Quaternion.Angle(rotation,coin.visual.rotation)>1,"Coin continuously spins");
            cp.interval=.2f;cp.automatic=true;cp.RestartSchedule();int emitted=cp.Emissions;await Tick(650);
            check(cp.Emissions-emitted>=3,"Coin emits repeatedly at its fixed interval");cp.automatic=false;
            int balance=wallet.Balance;
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(99.75f,1,100);wall.transform.localScale=new Vector3(.1f,3,2);
            teleport(new Vector3(99,position.y,100));await Tick(80);
            check(coinRoot!=null && wallet.Balance==balance,"Coin proximity pickup cannot pass through a wall");
            UnityEngine.Object.Destroy(wall);await Tick(30);teleport(new Vector3(100,0,100));await Tick(80);
            check(coinRoot==null && wallet.Balance==balance+1,"Approaching a coin destroys it and credits exactly one coin");
            await Tick();check(wallet.Balance==balance+1,"Collected coin cannot credit the wallet twice");
            int transactions=0;Action<WalletTransaction> changed=_=>transactions++;wallet.Changed+=changed;
            check(wallet.TrySpend(1,"Test purchase") && wallet.Balance==balance,"Economy supports affordable spending");
            var before=wallet.Capture();check(!wallet.TrySpend(int.MaxValue) && wallet.Balance==before.balance,"Insufficient funds leave the balance unchanged");
            check(!wallet.TryCredit(-1) && !wallet.TrySpend(-1),"Negative transactions are rejected");
            wallet.TryCredit(2,"Test reward");check(transactions==2,"Only successful transactions publish balance events");wallet.Changed-=changed;
            wallet.Restore(snapshot);check(wallet.Balance==snapshot.balance,"Wallet snapshot restores session economy state");
            teleport(new Vector3(50,0,50));
            lightRoot=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BallLightning.prefab"));
            lightRoot.transform.position=new Vector3(0,0,0);var lightning=lightRoot.GetComponent<BallLightning>();
            lightning.navigation=monster.navigation;lightning.player=vitality;lightning.autoCycle=false;lightning.wanderSpeed=.6f;
            var lp=lightRoot.GetComponent<SonarObjectEmitter>();await Tick(180);
            var start=lightning.transform.position;float height0=lightning.Center.y;await Tick(700);float height1=lightning.Center.y;await Tick(700);
            check(lightning.NavigationReady && Vector3.Distance(start,lightning.transform.position)>.2f,"Lightning actually wanders on the map");
            check(Mathf.Abs(height0-lightning.hoverHeight)<.001f && Mathf.Abs(height1-height0)<.001f && Mathf.Abs(lightning.Center.y-height0)<.001f,"Lightning maintains constant world height while moving");
            var agent=lightning.GetComponent<NavMeshAgent>();var destination=agent.destination;
            teleport(new Vector3(-50,0,-50));await Tick(80);
            check(Vector3.Distance(destination,agent.destination)<.01f,"Player movement does not redirect lightning wandering");
            lightning.wanderSpeed=0;agent.ResetPath();agent.Warp(Vector3.zero);await Tick(30);
            lightning.calm.interval=.6f;lightning.active.interval=.2f;
            lightning.SetState(LightningState.Calm);int baseline=lp.Emissions;await Tick(750);int calmCount=lp.Emissions-baseline;
            lightning.SetState(LightningState.Active);baseline=lp.Emissions;await Tick(750);int activeCount=lp.Emissions-baseline;
            check(activeCount>calmCount && lp.range>lightning.calm.range,"Active lightning has faster real emissions and a larger finite range");
            lightning.SetState(LightningState.Calm);vitality.RestoreHealth();teleport(new Vector3(-1,position.y,0));
            var collision=capsule.Move(Vector3.right*2);await Tick(80);
            check(player.transform.position.x>.8f && vitality.Health==vitality.maximumHealth,"Calm lightning is passable and causes no damage (x="+player.transform.position.x+", hp="+vitality.Health+")");
            teleport(new Vector3(1.5f,0,0));lightning.damage=10;lightning.damageInterval=.3f;lightning.SetState(LightningState.Active);
            await Tick(650);float loss=vitality.maximumHealth-vitality.Health;
            check(loss>=20 && loss<=30,"Active lightning applies damage on a timed cadence rather than every frame");
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(.7f,1.2f,0);wall.transform.localScale=new Vector3(.2f,3,3);Physics.SyncTransforms();
            health=vitality.Health;await Tick(380);check(vitality.Health==health,"Lightning damage does not pass through walls");
            UnityEngine.Object.Destroy(wall);await Tick(30);teleport(new Vector3(50,0,50));health=vitality.Health;await Tick(380);
            check(vitality.Health==health,"Active lightning does not damage distant players");
            lightning.calmSeconds=.3f;lightning.activeSeconds=.3f;lightning.SetState(LightningState.Calm);lightning.autoCycle=true;
            await Tick(380);check(lightning.State==LightningState.Active,"Lightning automatically enters its active state");
            await Tick(300);check(lightning.State==LightningState.Calm,"Lightning returns to calm after its active duration");lightning.autoCycle=false;
            lightning.SetState(LightningState.Active);agent.Warp(Vector3.zero);agent.ResetPath();lightning.wanderSpeed=0;
            monsterAgent.Warp(new Vector3(-1.5f,.05f,0));monsterAgent.ResetPath();monster.enabled=true;
            await Tick(120);check(monster.AvoidingLightning,"Nearby lightning puts the monster into avoidance");
            float separation=Vector3.Distance(monster.transform.position,lightning.transform.position);await Tick(1400);
            check(Vector3.Distance(monster.transform.position,lightning.transform.position)>separation+.5f,"Monster actually moves away from nearby lightning");
            monster.enabled=false;monsterAgent.Warp(new Vector3(-5,.05f,0));monsterAgent.ResetPath();monster.enabled=true;monster.soundPriorityMemory=0;
            GameplayNoise.Emit(new Vector3(5,.05f,0),30,GameplayNoiseKind.Impact,player);await Tick(100);
            bool detour=monster.AvoidingLightning;float closest=float.PositiveInfinity;
            for(int i=0;i<20;i++){await Tick(80);closest=Mathf.Min(closest,Vector2.Distance(new Vector2(monster.transform.position.x,monster.transform.position.z),Vector2.zero));}
            check(detour && closest>lightning.activeAvoidanceRadius,"Monster detours around lightning instead of following a sound through it");
            check(lightning.GetComponentsInChildren<Collider>()[0].isTrigger,"Lightning has no solid collider that blocks the player");
        }
        catch(Exception e){failures.Add(e.ToString());}
        finally
        {
            monster.enabled=false;
            if(coinRoot!=null)UnityEngine.Object.Destroy(coinRoot);if(lightRoot!=null)UnityEngine.Object.Destroy(lightRoot);if(wall!=null)UnityEngine.Object.Destroy(wall);
            teleport(position);wallet.Restore(snapshot);vitality.RestoreHealth();
            monster.soundPriorityMemory=priority;if(monsterAgent.enabled && monsterAgent.isOnNavMesh){monsterAgent.ResetPath();monsterAgent.Warp(monsterPosition);}
            monster.enabled=brain;monsterScan.enabled=scan;player.SetInputCaptured(!suspended);
            for(int i=0;i<emitters.Length;i++)if(emitters[i]!=null)emitters[i].automatic=automatic[i];
            for(int i=0;i<existingHazards.Length;i++)if(existingHazards[i]!=null)existingHazards[i].enabled=hazardEnabled[i];
            for(int i=0;i<existingCoins.Length;i++)if(existingCoins[i]!=null)existingCoins[i].enabled=coinEnabled[i];
        }
        return new {passed=failures.Count==0,checks,failures};
    }
}
