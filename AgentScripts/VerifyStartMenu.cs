using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class VerifyStartMenu
{
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static List<string> checks,frames;
    static void Check(bool ok,string label)=>checks.Add((ok?"PASS: ":"FAIL: ")+label);
    public static object Resolutions()
    {
        var menu=UnityEngine.Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(menu==null || menu.HasStarted)throw new Exception("Run with the menu visible.");
        System.IO.Directory.CreateDirectory("Logs/StartMenu");var rows=new List<object>();
        var rebuild=typeof(SonicSpaceStartMenu).GetMethod("RebuildMask",Fields);
        var material=(Material)typeof(SonicSpaceStartMenu).GetField("menuMaterial",Fields).GetValue(menu);
        var previous=RenderTexture.active;var camera=menu.player.GetComponentInChildren<Camera>();
        float pitch=BlackBoxPointCloudFeature.GetDotSpacing(camera),radius=BlackBoxPointCloudFeature.GetDotDiameter(camera)*.5f*menu.titleDotScale;
        try
        {
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(800,600),new Vector2Int(2560,1080)})
            {
                rebuild.Invoke(menu,new object[]{size.x,size.y});
                var mask=(RenderTexture)typeof(SonicSpaceStartMenu).GetField("mask",Fields).GetValue(menu);
                material.SetVector("_MenuSize",new Vector4(size.x,size.y,720f*size.x/size.y,720));
                material.SetFloat("_GridPitch",pitch);material.SetFloat("_DotRadius",radius/menu.titleDotScale);material.SetFloat("_OpeningTime",0);material.SetFloat("_Transition",0);material.SetFloat("_Hover",0);
                var rt=RenderTexture.GetTemporary(size.x,size.y,0,RenderTextureFormat.ARGB32);var image=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                int ink=0,offGrid=0,clipped=0;
                try
                {
                    Graphics.Blit(mask,rt,material);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();var pixels=image.GetPixels32();
                    float scale=720f/size.y;
                    for(int y=0;y<size.y;y++)for(int x=0;x<size.x;x++)
                    {
                        if(pixels[y*size.x+x].r>=128)continue;ink++;
                        var p=new Vector2((x+.5f)*scale,(size.y-y-.5f)*scale);
                        var center=new Vector2((Mathf.Floor(p.x/pitch)+.5f)*pitch,(Mathf.Floor(p.y/pitch)+.5f)*pitch);
                        if(Vector2.Distance(p,center)>radius+.72f*scale)offGrid++;
                        if(x<8 || x>=size.x-8 || y<8 || y>=size.y-8)clipped++;
                    }
                    System.IO.File.WriteAllBytes($"Logs/StartMenu/layout-{size.x}x{size.y}.png",image.EncodeToPNG());
                    rows.Add(new {size=size.ToString(),ink,offGrid,clipped,passed=ink>100 && offGrid==0 && clipped==0});
                }
                finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(image);}
            }
        }
        finally {rebuild.Invoke(menu,new object[]{0,0});}
        System.IO.File.WriteAllText("Logs/StartMenu/resolutions.json",Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));return rows;
    }
    static async Task<float> Capture(string name)
    {
        string path="Logs/StartMenu/"+name+".png";ScreenCapture.CaptureScreenshot(path);await Task.Delay(120);
        var image=new Texture2D(2,2);image.LoadImage(System.IO.File.ReadAllBytes(path));var pixels=image.GetPixels32();
        float white=(float)pixels.Count(p=>p.r>240 && p.g>240 && p.b>240)/pixels.Length;
        int dark=pixels.Count(p=>p.r<32 && p.g<32 && p.b<32);
        frames.Add($"{name}: {image.width}x{image.height}, white={white:P2}, blackPixels={dark}");
        UnityEngine.Object.Destroy(image);return white;
    }
    public static async Task<object> Main()
    {
        var menu=UnityEngine.Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(menu==null || menu.HasStarted)throw new Exception("Run in a fresh Play session with the start menu visible.");
        System.IO.Directory.CreateDirectory("Logs/StartMenu");checks=new();frames=new();
        var player=menu.player;var sonar=player.GetComponentInChildren<SphericalSonar>();var position=player.transform.position;
        int emissions=sonar.Emissions;float worldTime=Time.time;float duration=menu.transitionSeconds;
        var original=InputSystem.settings;var saved=UnityEngine.Object.Instantiate(original);var flags=original.hideFlags;
        var test=UnityEngine.Object.Instantiate(original);test.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        test.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=test;
        var oldMouse=Mouse.current;var oldKeyboard=Keyboard.current;var mouse=InputSystem.AddDevice<Mouse>();var keyboard=InputSystem.AddDevice<Keyboard>();
        try
        {
            Check(SonicSpaceStartMenu.BlocksGameplay && Time.timeScale==0 && AudioListener.pause && player.InputSuspended,"Menu pauses gameplay, audio and player input");
            var labels=menu.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            Check(labels.Length==1 && labels[0].text=="SONICSPACE" && labels.All(t=>t.font==GameUiTheme.Current.textFont),"Only the English title uses the SECONDERSIGN theme font");
            Check(menu.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length==0,"No Start button remains");
            float initial=await Capture("01-menu");Check(initial>.9f && initial<.999f,"Initial screen is white with visible black dotted lettering");
            mouse.MakeCurrent();keyboard.MakeCurrent();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.F1,Key.C));
            InputSystem.QueueStateEvent(mouse,new MouseState {position=new Vector2(10,10)});await Task.Delay(200);
            Check(!menu.IsTransitioning && Vector3.Distance(position,player.transform.position)<.001f && sonar.Emissions==emissions && Time.time==worldTime,"Movement / debug keys do not start or affect the game");
            InputSystem.QueueStateEvent(mouse,new MouseState {position=new Vector2(10,10)});await Task.Delay(60);
            var point=new Vector2(10,10);
            InputSystem.QueueStateEvent(mouse,new MouseState {position=point}.WithButton(MouseButton.Left));await Task.Delay(70);
            Check(menu.IsTransitioning,"A real mouse click in the screen corner begins the transition");
            InputSystem.QueueStateEvent(mouse,new MouseState {position=point});InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            menu.transitionSeconds=1000;
            var start=typeof(SonicSpaceStartMenu).GetField("startedAt",Fields);
            var whites=new List<float>();
            foreach(float p in new[]{.20f,.42f,.67f,.80f})
            {
                start.SetValue(menu,Time.unscaledTime-p*1000);
                await Task.Delay(40);whites.Add(await Capture("phase-"+p.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)));
            }
            Check(whites[0]>whites[1] && whites[1]>whites[2] && whites[2]>whites[3],"Central collapse progressively consumes the outer white space");
            Check(whites[2]>0 && whites[2]<.65f,"Expanding sink leaves only the outer white surface and falling point fragments");
            float progress=menu.TransitionProgress;menu.BeginGame();
            Check(menu.TransitionProgress==progress,"Repeated Start calls cannot restart an active transition");
            Check(Time.time==worldTime && player.InputSuspended,"Gameplay and opening narrative remain paused throughout transition");
            menu.transitionSeconds=duration;start.SetValue(menu,Time.unscaledTime);
            await Task.Delay(Mathf.CeilToInt(duration*1000)+150);
            Check(menu.HasStarted && !SonicSpaceStartMenu.BlocksGameplay && Time.timeScale==1 && !AudioListener.pause && !player.InputSuspended,"Real-time transition completes and restores game ownership");
            // Field.Emissions also includes object/landing pulses as physics resumes.
            Check(sonar.Emissions>emissions,"Handoff resumes sonar with the entrance wave (Start was consumed while paused)");
            Check(player.GetComponent<PlayerFloorProgression>().CurrentFloor==0 && !string.IsNullOrEmpty(player.GetComponent<PlayerSubtitles>().CurrentText),"Gameplay starts on F0 with opening Chinese subtitles intact");
            Check(!UnityEditor.ShaderUtil.ShaderHasError(menu.menuShader) && !UnityEditor.ShaderUtil.ShaderHasError(menu.glyphMaskShader),"Both menu shaders compile without errors");
            await Capture("06-game");
        }
        finally
        {
            menu.transitionSeconds=duration;
            InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);
            if(original!=null){InputSystem.settings=original;UnityEngine.Object.Destroy(saved);}else{saved.hideFlags=flags;InputSystem.settings=saved;}
            UnityEngine.Object.Destroy(test);if(oldMouse!=null && oldMouse.added)oldMouse.MakeCurrent();if(oldKeyboard!=null && oldKeyboard.added)oldKeyboard.MakeCurrent();
        }
        var report=new {passed=checks.All(s=>s.StartsWith("PASS:")),checks,frames};
        System.IO.File.WriteAllText("Logs/StartMenu/report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
