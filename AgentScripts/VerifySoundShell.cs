using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Sound2.LevelEditing;
using Object = UnityEngine.Object;

public static class VerifySoundShell
{
    static readonly List<string> checks=new();
    static void Check(bool condition,string message)
    { if(!condition)throw new Exception("FAILED: "+message); checks.Add(message); }
    static async Task Until(Func<bool> condition,int milliseconds=12000)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!condition()){if(timer.ElapsedMilliseconds>milliseconds)throw new Exception("Timed out waiting for runtime state");await Task.Delay(30);}
    }
    static async Task Capture(string name)
    { ScreenCapture.CaptureScreenshot("Logs/SoundShell-"+name+".png");await Task.Delay(200); }
    public static async Task<object> Opening()
    {
        checks.Clear();
        var opening=Object.FindAnyObjectByType<SoundShellOpening>();var p=opening.player;
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        Check(menu!=null&&!menu.HasStarted&&menu.usePlayerView,"Title uses the real Player camera");
        int frame=Time.frameCount;await Task.Delay(120);Check(Time.frameCount>frame,"Player loop advances while unfocused");
        Check(p.InputSuspended&&Time.timeScale==0,"Title blocks movement and pauses gameplay");
        Check(opening.startingRoom.generatedRoot==null,"No generated closed box overlaps the real window");
        Check(!UnityEditor.ShaderUtil.ShaderHasError(Shader.Find("Sound2/Starting Room Paper")),"Room shader compiles");
        var settings=InputSystem.settings;
        var background=settings.backgroundBehavior;var editorInput=settings.editorInputBehaviorInPlayMode;
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var oldMouse=Mouse.current;var oldKeyboard=Keyboard.current;
        var mouse=InputSystem.AddDevice<Mouse>();var keyboard=InputSystem.AddDevice<Keyboard>();
        try
        {
            mouse.MakeCurrent();keyboard.MakeCurrent();
            var pos=p.transform.position;var rotation=p.ViewTransform.rotation;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
            InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(90,45)});
            await Task.Delay(200);
            Check(Vector3.Distance(pos,p.transform.position)<.001f&&Quaternion.Angle(rotation,p.ViewTransform.rotation)<.01f,"WASD and mouse cannot move the title view");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(mouse,new MouseState());
            await Until(()=>menu.IntroReady);
            Check(menu.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t=>t.GetParsedText()=="SOUND & SHELL"),"SOUND & SHELL is fully displayed with its title symbol styling");
            await Capture("title");
            InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));await Task.Delay(70);
            InputSystem.QueueStateEvent(mouse,new MouseState());await Until(()=>menu.HasStarted);
            Check(!p.InputSuspended&&!SonicSpaceStartMenu.BlocksGameplay&&Time.timeScale==1,"Left click dismisses title and unlocks control");
            rotation=p.transform.rotation;
            InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(20,0)});await Task.Delay(120);
            Check(Quaternion.Angle(rotation,p.transform.rotation)>.01f,"Mouse look works after title");
            InputSystem.QueueStateEvent(mouse,new MouseState());
            p.Teleport(opening.spawn.position);p.FaceDirection(Vector3.back);
            await Capture("window");
            // Walk and jump through the physical sill using the real Input System and controller.
            p.Teleport(new Vector3(0,opening.spawn.position.y,-3.6f));await Task.Delay(120);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.Space));await Task.Delay(550);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Check(p.transform.position.z < -5.25f,"Player can jump through the window collider opening");
            await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.AwaitingReturn);
            Check(p.InputSuspended&&p.transform.position.y<-.5f,"Falling waits before the black/card sequence and locks control");
            await Capture("fool");
            InputSystem.QueueStateEvent(mouse,new MouseState());await Task.Delay(70);
            InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));await Task.Delay(70);
            Check(p.InputSuspended,"Return click cannot unlock movement during the fade");
            InputSystem.QueueStateEvent(mouse,new MouseState());await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.Room);
            Check(Vector3.Distance(p.transform.position,opening.spawn.position)<.15f&&!p.InputSuspended&&menu.HasStarted,"Second click fades back to spawn with title suppressed");
            await Capture("returned");
            // Repeat the ending to prove it is reusable and does not reset title state.
            p.Teleport(new Vector3(0,-1,-7));await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.AwaitingReturn);
            opening.ReturnToRoom();await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.Room);
            Check(menu.HasStarted&&!p.InputSuspended,"Repeated falls also return without replaying the title");
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);
            if(settings!=null){settings.backgroundBehavior=background;settings.editorInputBehaviorInPlayMode=editorInput;}
            System.IO.File.WriteAllLines("Logs/SoundShell-opening-checks.txt",checks);
            if(oldMouse!=null&&oldMouse.added)oldMouse.MakeCurrent();if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();
        }
        var result=new{passed=true,checks=checks.ToArray()};
        System.IO.File.WriteAllText("Logs/SoundShell-opening-report.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
    }

    public static async Task<object> Elevators()
    {
        checks.Clear();var world=Object.FindAnyObjectByType<LevelWorld>();var p=world.player;
        Check(!SonicSpaceStartMenu.BlocksGameplay,"Title has been dismissed before transit");
        Check(world.levels.Count==8&&world.connections.Count==7,"Eight ordered levels and seven connections");
        string[] labels={"学校","图书馆","家","超市","婚礼","医院","Boss"};
        int first=Mathf.Max(0,world.levels.IndexOf(world.CurrentLevel));
        for(int i=first;i<world.connections.Count;i++)
        {
            var link=world.connections[i];var from=link.source;var to=link.destination;
            p.SetInputCaptured(true);
            Check(from.Level==world.levels[i]&&to.Level==world.levels[i+1]&&!link.bidirectional,"Connection order: "+labels[i]);
            var interaction=p.GetComponent<PlayerInteraction>();
            if(from.DoorOpen<.001f)from.Use(interaction,false);
            await Until(()=>from.DoorOpen>.999f);
            var capsule=p.GetComponent<CharacterController>();
            var local=from.interiorCenter;local.y=capsule.height*.5f-capsule.center.y+capsule.skinWidth+.02f;
            p.Teleport(from.transform.TransformPoint(local));p.FaceDirection(from.transform.forward);
            Physics.SyncTransforms();
            Check(from.CanUse(interaction,true),"Cabin button usable: "+labels[i]);
            from.Use(interaction,true);
            Check(world.Travelling,"Transport begins: "+labels[i]);
            await Until(()=>!world.Travelling,20000);
            Check(world.CurrentLevel==to.Level&&to.Contains(p.transform.position),"Transport arrives inside cabin: "+labels[i]);
            Check(world.levels.Count(l=>l.gameObject.activeSelf)==1,"Only destination level remains active: "+labels[i]);
            // Editor focus loss releases cursor input independently of transportation.
            p.SetInputCaptured(true);
            Check(!p.InputSuspended&&!p.TransportLocked,"Player input available on arrival: "+labels[i]);
            // The doorway must be physically passable, including authored wall openings.
            var start=p.transform.position;
            var controller=p.GetComponent<CharacterController>();
            controller.Move(to.transform.forward*3.55f);Physics.SyncTransforms();
            Check(!to.Contains(p.transform.position),"Arrival cabin has a passable exit: "+labels[i]);
            p.Teleport(start);
        }
        Check(!world.TryDestination(world.ArrivalCheckpoint,out _,out _),"Boss is the final destination");
        var result=new{passed=true,checks=checks.ToArray()};
        System.IO.File.WriteAllText("Logs/SoundShell-elevators-report.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
    }

    public static async Task<object> EntryButtons()
    {
        checks.Clear();var w=Object.FindAnyObjectByType<LevelWorld>();var p=w.player;
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        await Until(()=>menu.IntroReady);menu.BeginGame();await Until(()=>menu.HasStarted);
        var settings=InputSystem.settings;var background=settings.backgroundBehavior;var editorInput=settings.editorInputBehaviorInPlayMode;
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var oldMouse=Mouse.current;var mouse=InputSystem.AddDevice<Mouse>();mouse.MakeCurrent();
        try
        {
            var lift=w.connections[0].source;var interaction=p.GetComponent<PlayerInteraction>();
            var button=lift.GetComponentsInChildren<LevelElevatorButton>().Single(b=>!b.inside);
            float height=p.GetComponent<CharacterController>().height*.5f-p.GetComponent<CharacterController>().center.y+.05f;
            var position=button.transform.position+lift.transform.forward*1.3f;position.y=height;
            p.Teleport(position);p.SetInputCaptured(true);
            var face=typeof(PlayerController).GetMethod("FacePoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            face.Invoke(p,new object[]{button.InteractionPosition});await Task.Delay(300);interaction.RefreshFocus();
            Check(interaction.FocusedTarget==button,"External elevator button is visible and targetable from the room");
            InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));await Task.Delay(70);InputSystem.QueueStateEvent(mouse,new MouseState());
            await Until(()=>lift.DoorOpen>.999f);Check(true,"Real left click opens starting elevator");
            p.Teleport(lift.transform.TransformPoint(new Vector3(0,height,4.1f)));
            p.GetComponent<CharacterController>().Move(-lift.transform.forward*3.5f);
            Check(lift.Contains(p.transform.position),"Player can walk through the starting elevator doorway");
            button=lift.GetComponentsInChildren<LevelElevatorButton>().Single(b=>b.inside);
            face.Invoke(p,new object[]{button.InteractionPosition});await Task.Delay(300);interaction.RefreshFocus();
            Check(interaction.FocusedTarget==button,"Internal travel button is visible and targetable");
            InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));await Task.Delay(70);InputSystem.QueueStateEvent(mouse,new MouseState());
            Check(w.Travelling,"Real left click on internal button starts travel");
            await Until(()=>!w.Travelling,20000);
            Check(w.CurrentLevel==w.levels[1],"Starting room button delivers player to school");
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);if(oldMouse!=null&&oldMouse.added)oldMouse.MakeCurrent();
            settings.backgroundBehavior=background;settings.editorInputBehaviorInPlayMode=editorInput;
        }
        var result=new{passed=true,checks=checks.ToArray()};
        System.IO.File.WriteAllText("Logs/SoundShell-entry-report.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
    }
}
