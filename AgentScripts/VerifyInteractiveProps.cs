using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

public static class VerifyInteractiveProps
{
    static readonly string[] Names={"Television","Radio","TapeRecorder","ServiceBell","GameConsole","HeartRateMonitor","Ring"};
    static readonly List<string> checks=new();
    static GameObject Prefab(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+name+".prefab");
    static void Check(bool ok,string text){checks.Add((ok?"PASS: ":"FAIL: ")+text);if(!ok)throw new Exception(text);}
    static void Report(string name){Directory.CreateDirectory("Logs/InteractiveProps");File.WriteAllLines("Logs/InteractiveProps/"+name+".txt",checks);}
    public static object Catalog()
    {
        var windows=Resources.FindObjectsOfTypeAll<Sound2.LevelEditing.Editor.LevelDesignerWindow>();
        foreach(var window in windows)
        {
            window.CreateGUI();
            var dropdown=window.rootVisualElement.Q<PopupField<GameObject>>("prop-choice");
            Check(dropdown!=null&&Names.All(n=>dropdown.choices.Contains(Prefab(n))),"Level editor dropdown includes all seven new prefabs");
        }
        return new{windows=windows.Length,checkedPrefabs=Names};
    }
    public static object Edit()
    {
        checks.Clear();
        foreach(string name in Names)
        {
            var p=Prefab(name);Check(p!=null,name+": prefab exists");
            var action=p.GetComponent<InteractableObject>();Check(action!=null && action.interactionAnchor!=null && action.onInteract.GetPersistentEventCount()==0,name+": empty independent action with focus anchor");
            Check(p.GetComponent<PickupItem>()==null && p.GetComponent<Rigidbody>()==null,name+": no unintended pickup behaviour");
            var filters=p.GetComponentsInChildren<MeshFilter>();Check(filters.Length==1&&filters[0].sharedMesh!=null&&filters[0].sharedMesh.vertexCount>20,name+": combined authored model");
            Check(AssetDatabase.GetAssetPath(filters[0].sharedMesh)=="Assets/Models/InteractiveProps/"+name+".asset",name+": one mesh asset per prop");
            Check(p.GetComponents<Collider>().Length>0&&p.GetComponent<LevelProp>().gameplayMaterial!=null,name+": placement, collision and black point-cloud material configured");
        }
        var alarm=Prefab("PickupAlarmClock");Check(alarm.GetComponents<InteractableObject>().Length==2&&alarm.GetComponent<PickupAlarm>()!=null,"Alarm has separate action and pickup, retaining its timer");
        Check(typeof(InteractableObject).GetMethod("OnInteract",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).IsVirtual,"Object action is a virtual extension hook");
        Capture();Report("edit");return new {passed=true,checks};
    }
    static void Capture()
    {
        var oldScene=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var rt=new RenderTexture(500,400,24);var frame=new Texture2D(500,400,TextureFormat.RGB24,false);var atlas=new Texture2D(2000,800,TextureFormat.RGB24,false);
        var oldRT=RenderTexture.active;
        try
        {
            SceneManager.SetActiveScene(scene);var centre=new Vector3(10000,0,10000);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=centre+Vector3.down*.025f;floor.transform.localScale=new Vector3(20,.05f,20);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Floor.mat");
            var light=new GameObject("Preview light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
            var cam=new GameObject("Preview camera",typeof(Camera)).GetComponent<Camera>();cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.065f,.08f,.10f);cam.fieldOfView=32;cam.targetTexture=rt;cam.farClipPlane=30;
            for(int i=0;i<Names.Length;i++)
            {
                var model=Object.Instantiate(Prefab(Names[i]),centre,Quaternion.identity);
                var bounds=model.GetComponentInChildren<Renderer>().bounds;float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
                cam.nearClipPlane=.001f;cam.transform.position=bounds.center+new Vector3(1,.65f,-1.8f).normalized*size*2.4f;cam.transform.LookAt(bounds.center);
                cam.Render();RenderTexture.active=rt;frame.ReadPixels(new Rect(0,0,500,400),0,0);frame.Apply();atlas.SetPixels(i%4*500,(1-i/4)*400,500,400,frame.GetPixels());Object.DestroyImmediate(model);
            }
            atlas.Apply();Directory.CreateDirectory("Logs/InteractiveProps");File.WriteAllBytes("Logs/InteractiveProps/models.png",atlas.EncodeToPNG());
        }
        finally{RenderTexture.active=oldRT;Object.DestroyImmediate(rt);Object.DestroyImmediate(frame);Object.DestroyImmediate(atlas);SceneManager.SetActiveScene(oldScene);EditorSceneManager.CloseScene(scene,true);}
    }
    static async Task Wait(int ms=100){int f=Time.frameCount;await Task.Delay(ms);Check(Time.frameCount>f,"Player loop advances");}
    public static async Task<object> Play()
    {
        checks.Clear();Check(Application.isPlaying,"Play mode active");
        var players=Object.FindObjectsByType<PlayerController>();var suspended=players.Select(p=>p.InputSuspended).ToArray();foreach(var p in players)p.InputSuspended=true;
        var previousMouse=Mouse.current;var mouse=InputSystem.AddDevice<Mouse>();var previousKeyboard=Keyboard.current;var keyboard=InputSystem.AddDevice<Keyboard>();
        var fixture=new GameObject("Interactive props verification");
        try
        {
            var player=Object.Instantiate(Prefab("Player"),new Vector3(6000,6000,6000),Quaternion.identity,fixture.transform);
            player.GetComponentInChildren<AudioListener>().enabled=false;
            var controller=player.GetComponent<PlayerController>();var so=new SerializedObject(controller);so.FindProperty("useGravity").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
            var interaction=player.GetComponent<PlayerInteraction>();var pickup=player.GetComponent<PlayerPickup>();await Wait();
            var origin=interaction.viewCamera.transform.position;var forward=interaction.viewCamera.transform.forward;
            async Task Click(){mouse.MakeCurrent();InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));await Wait(80);InputSystem.QueueStateEvent(mouse,new MouseState());await Wait(50);}
            async Task PressE(){keyboard.MakeCurrent();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));await Wait(80);InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Wait(50);}
            foreach(string name in Names)
            {
                var go=Object.Instantiate(Prefab(name),Vector3.zero,Quaternion.identity,fixture.transform);var action=go.GetComponent<InteractableObject>();
                go.transform.position=origin+forward*1.35f-action.InteractionPosition;Physics.SyncTransforms();int calls=0;action.onInteract.AddListener(_=>calls++);
                await Wait(350);Check(interaction.FocusedTarget==action,name+": aimed object receives focus");await PressE();Check(calls==0&&pickup.HeldItem==null,name+": E does nothing");
                var position=go.transform.position;await Click();Check(calls==1&&pickup.HeldItem==null&&go.transform.position==position,name+": left click invokes empty action once without pickup/effect");
                Check(go.GetComponentInChildren<Renderer>().sharedMaterials.All(m=>m==go.GetComponent<LevelProp>().gameplayMaterial),name+": runtime black material retained");
                action.interactionEnabled=false;await Click();Check(calls==1,name+": disabled action rejects click");action.interactionEnabled=true;
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(fixture.transform);wall.transform.position=origin+forward*.6f;wall.transform.localScale=new Vector3(2,2,.1f);Physics.SyncTransforms();
                await Click();Check(calls==1,name+": wall blocks action");Object.Destroy(wall);Object.Destroy(go);await Wait(80);
            }
            var alarm=Object.Instantiate(Prefab("PickupAlarmClock"),Vector3.zero,Quaternion.identity,fixture.transform);
            var item=alarm.GetComponent<PickupItem>();var actionAlarm=alarm.GetComponents<InteractableObject>().Single(c=>!(c is PickupItem));
            alarm.GetComponent<Rigidbody>().useGravity=false;alarm.transform.position=origin+forward*1.4f-actionAlarm.InteractionPosition;Physics.SyncTransforms();
            int interactions=0,pickups=0;bool emptyDuringAction=false;actionAlarm.onInteract.AddListener(_=>{interactions++;emptyDuringAction=pickup.HeldItem==null;});item.PickedUp+=()=>pickups++;
            await Wait(350);Check(interaction.FocusedTarget==actionAlarm,"Dual-component alarm focuses action regardless of component order");
            await PressE();Check(interactions==0&&pickup.HeldItem==null,"E cannot interact with or pick up alarm");
            await Click();Check(interactions==1&&pickups==1&&emptyDuringAction&&pickup.HeldItem==item,"One left click invokes action first, then picks up alarm once");
            await PressE();Check(pickup.HeldItem==item&&interactions==1,"E cannot throw held alarm");
            pickup.throwSpeed=pickup.throwUpwardSpeed=0;await Click();Check(pickup.HeldItem==null&&interactions==1&&pickups==1,"Throwing alarm NEVER invokes its action again");
            Object.Destroy(alarm);await Wait(80);
            var ball=Object.Instantiate(Prefab("PickupBouncyBall"),origin+forward*1.4f,Quaternion.identity,fixture.transform);var ballItem=ball.GetComponent<PickupItem>();ball.GetComponent<Rigidbody>().useGravity=false;Physics.SyncTransforms();
            await Wait(350);await Click();Check(pickup.HeldItem==ballItem,"Pickup-only object still uses left click");await Click();Check(pickup.HeldItem==null,"Pickup-only object still throws with left click");
            Report("play");return new{passed=true,count=checks.Count,checks};
        }
        finally
        {
            Object.Destroy(fixture);InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);
            if(previousMouse!=null&&previousMouse.added)previousMouse.MakeCurrent();if(previousKeyboard!=null&&previousKeyboard.added)previousKeyboard.MakeCurrent();
            for(int i=0;i<players.Length;i++)if(players[i]!=null)players[i].InputSuspended=suspended[i];Report("play");
        }
    }
}
