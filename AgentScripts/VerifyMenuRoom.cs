using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class VerifyMenuRoom
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static SonicSpaceStartMenu menu;static List<string> checks;
    static object Field(string name)=>typeof(SonicSpaceStartMenu).GetField(name,Flags).GetValue(menu);
    static void Set(string name,object value)=>typeof(SonicSpaceStartMenu).GetField(name,Flags).SetValue(menu,value);
    static void Check(bool ok,string label)=>checks.Add((ok?"PASS: ":"FAIL: ")+label);
    static void Age(float age){Set("introStartedAt",Time.unscaledTime-age);typeof(SonicSpaceStartMenu).GetMethod("RenderGlyphs",Flags).Invoke(menu,null);}
    static Vector3[] Vertices()=>((TextMeshProUGUI)Field("title")).textInfo.meshInfo.SelectMany(m=>m.vertices).ToArray();
    static Vector2 Center()
    {
        var t=(TextMeshProUGUI)Field("title");var points=t.textInfo.characterInfo.Take(t.textInfo.characterCount).Where(c=>c.isVisible).SelectMany(c=>t.textInfo.meshInfo[c.materialReferenceIndex].vertices.Skip(c.vertexIndex).Take(4)).ToArray();
        return new Vector2((points.Min(p=>p.x)+points.Max(p=>p.x))*.5f,(points.Min(p=>p.y)+points.Max(p=>p.y))*.5f)+t.rectTransform.anchoredPosition;
    }
    static Color32[] Draw(float age,Quaternion rotation,bool empty=false,bool room=true,float shimmer=1,float density=.5f,string name=null,bool stars=false)
    {
        var mat=new Material((Material)Field("menuMaterial"));mat.SetVector("_MenuSize",new Vector4(1280,720,1280,720));
        var camera=menu.player.GetComponentInChildren<Camera>();mat.SetFloat("_GridPitch",BlackBoxPointCloudFeature.GetDotSpacing(camera));mat.SetFloat("_DotRadius",BlackBoxPointCloudFeature.GetDotDiameter(camera)*.5f);mat.SetFloat("_TitleDotScale",menu.titleDotScale);
        mat.SetFloat("_MenuStarsEnabled",stars?1:0);mat.SetFloat("_OpeningTime",age);mat.SetFloat("_Transition",0);mat.SetFloat("_Hover",0);mat.SetFloat("_TitleShimmer",shimmer);mat.SetFloat("_TitleDensity",density);
        mat.SetFloat("_RoomHalfSize",room?5:0);mat.SetFloat("_RoomTanHalfFov",Mathf.Tan(40*Mathf.Deg2Rad));mat.SetMatrix("_RoomView",Matrix4x4.Rotate(rotation));
        var rt=RenderTexture.GetTemporary(1280,720,0,RenderTextureFormat.ARGB32);var old=RenderTexture.active;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{RenderTexture.active=rt;GL.Clear(false,true,Color.black);Graphics.Blit(empty?Texture2D.blackTexture:(Texture)Field("mask"),rt,mat);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();if(name!=null)System.IO.File.WriteAllBytes("Logs/MenuRoom/"+name+".png",image.EncodeToPNG());return image.GetPixels32();}
        finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mat);}
    }
    static int Ink(Color32[] pixels)=>pixels.Count(p=>p.r<230);
    public static async Task<object> Main()
    {
        menu=UnityEngine.Object.FindAnyObjectByType<SonicSpaceStartMenu>();if(menu==null||menu.HasStarted)throw new Exception("Fresh paused menu required");
        checks=new();System.IO.Directory.CreateDirectory("Logs/MenuRoom");var rebuild=typeof(SonicSpaceStartMenu).GetMethod("RebuildMask",Flags);
        var target=(Vector2)Field("lookTarget");var angles=(Vector2)Field("lookAngles");var velocity=(Vector2)Field("lookVelocity");
        try
        {
            rebuild.Invoke(menu,new object[]{1280,720});
            Age(0);Check(Draw(0,Quaternion.identity).All(p=>p.r==255),"Opening starts as a white void without a spectrum performance");
            foreach(float age in new[]{.65f,.95f,1.35f,3.4f}){Age(age);Check(Center().magnitude<.001f,"Visible title geometry is horizontally and vertically centered at age "+age);}
            Age(4);var first=Vertices();Age(7);Check(first.SequenceEqual(Vertices()),"Title vertices never float, drift or bounce after appearing");
            Draw(4,Quaternion.identity,name:"room-title-front",stars:true);Draw(4,Quaternion.Euler(12,42,0),name:"room-title-turned",stars:true);
            var titleFront=Draw(4,Quaternion.identity,room:false,shimmer:0);var titleTurn=Draw(4,Quaternion.Euler(30,145,0),room:false,shimmer:0);
            Check(titleFront.SequenceEqual(titleTurn),"Looking around cannot move the centered title ");
            var front=Draw(4,Quaternion.identity,empty:true);var turned=Draw(4,Quaternion.Euler(12,42,0),empty:true);
            Check(front.Zip(turned,(a,b)=>Math.Abs(a.r-b.r)>16).Count(x=>x)>500,"Room edge and wall projections actually change with view rotation");
            float pitch=BlackBoxPointCloudFeature.GetDotSpacing(menu.player.GetComponentInChildren<Camera>()),radius=BlackBoxPointCloudFeature.GetDotDiameter(menu.player.GetComponentInChildren<Camera>())*.5f;int offGrid=0;
            foreach(var rotation in new[]{Quaternion.identity,Quaternion.Euler(0,90,0),Quaternion.Euler(0,180,0),Quaternion.Euler(0,270,0),Quaternion.Euler(-75,45,0),Quaternion.Euler(75,-45,0)})
            {
                var pixels=Draw(4,rotation,empty:true);Check(Ink(pixels)>200 && pixels.All(p=>p.r==p.g && p.g==p.b),"Closed room remains visible and monochrome looking "+rotation.eulerAngles);
                for(int y=0;y<720;y++)for(int x=0;x<1280;x++)if(pixels[y*1280+x].r<230)
                {var p=new Vector2(x+.5f,719.5f-y);var c=new Vector2((Mathf.Floor(p.x/pitch)+.5f)*pitch,(Mathf.Floor(p.y/pitch)+.5f)*pitch);if(Vector2.Distance(p,c)>radius+.72f)offGrid++;}
            }
            Check(offGrid==0,"Room edges and all surface waves use the unchanged screen-point lattice");
            var early=Draw(.6f,Quaternion.identity,empty:true);var waveLater=Draw(6.7f,Quaternion.identity,empty:true,name:"room-wave-later");
            Check(Ink(front)>Ink(early) && front.Zip(waveLater,(a,b)=>Math.Abs(a.r-b.r)>16).Count(v=>v)>1000,"Random surface waves evolve: early="+Ink(early)+", front="+Ink(front)+", changed="+front.Zip(waveLater,(a,b)=>Math.Abs(a.r-b.r)>16).Count(v=>v));
            var masked=Draw(4,Quaternion.identity,density:0);int cleared=front.Zip(masked,(a,b)=>b.r>a.r+30).Count(v=>v);
            Check(cleared>50 && Ink(masked)<Ink(front),"Title glyphs clear nearby room dots");
            var samples=new List<int>();
            for(float y=pitch*.5f;y<720;y+=pitch)for(float x=pitch*.5f;x<1280;x+=pitch)
            {int index=(719-(int)y)*1280+(int)x;if(y>290 && y<430 && titleFront[index].r<20)samples.Add(index);}
            var series=samples.Select(_=>new List<byte>()).ToArray();int dimmest=int.MaxValue;Color32[] last=null;
            for(int frame=0;frame<60;frame++)
            {last=Draw(4+frame*.09f,Quaternion.identity,room:false);int strong=0;for(int c=0;c<samples.Count;c++){byte v=last[samples[c]].r;series[c].Add(v);if(v<100)strong++;}dimmest=Math.Min(dimmest,strong);}
            int flashes=series.Count(s=>s.Zip(s.Skip(1),(a,b)=>Math.Abs(a-b)>170).Any(v=>v));int fades=series.Count(s=>s.Count(v=>v>30 && v<220)>=4);
            Check(flashes>15 && fades>50,"Title points independently blink and smoothly fade ("+flashes+" / "+fades+")");
            Check(samples.Count>200 && dimmest>samples.Count*.60,"Title remains legible throughout its random visibility cycles");
            Check(menu.GetComponentsInChildren<TextMeshProUGUI>(true).Length==1 && menu.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length==0,"Only the English title remains; no Chinese label or Start button exists");
            var starsFront=Draw(4,Quaternion.identity,empty:true,room:false,stars:true,name:"stars-front");
            var starsTurn=Draw(4,Quaternion.Euler(12,42,0),empty:true,room:false,stars:true,name:"stars-turned");
            var starsLater=Draw(8.3f,Quaternion.identity,empty:true,room:false,stars:true);
            Check(Ink(starsFront)>10 && starsFront.All(p=>p.r==p.g && p.g==p.b),"Menu stars render black monochrome dots on white");
            Check(starsFront.Zip(starsTurn,(a,b)=>Math.Abs(a.r-b.r)>16).Count(v=>v)>20,"Spatial stars move when the menu camera turns");
            Check(starsFront.Zip(starsLater,(a,b)=>Math.Abs(a.r-b.r)>16).Count(v=>v)>20,"Menu stars randomly appear and fade over time");
            int starOffGrid=0;for(int y=0;y<720;y++)for(int x=0;x<1280;x++)if(starsFront[y*1280+x].r<230)
            {var p=new Vector2(x+.5f,719.5f-y);var c=new Vector2((Mathf.Floor(p.x/pitch)+.5f)*pitch,(Mathf.Floor(p.y/pitch)+.5f)*pitch);if(Vector2.Distance(p,c)>radius+.72f)starOffGrid++;}
            Check(starOffGrid==0,"Menu stars share the existing dot grid");
            var settings=InputSystem.settings;var saved=UnityEngine.Object.Instantiate(settings);var flags=settings.hideFlags;var test=UnityEngine.Object.Instantiate(settings);
            test.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;test.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var oldMouse=Mouse.current;Mouse mouse=null;
            try
            {
                InputSystem.settings=test;mouse=InputSystem.AddDevice<Mouse>();mouse.MakeCurrent();
                var before=menu.RoomViewRotation;var playerView=menu.player.ViewTransform.rotation;var playerPosition=menu.player.transform.position;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f),delta=new Vector2(100,-45)});await Task.Delay(250);
                Check(Quaternion.Angle(before,menu.RoomViewRotation)>5,"Actual mouse delta turns the room in unscaled-time frames");
                Check(menu.player.ViewTransform.rotation==playerView && menu.player.transform.position==playerPosition && menu.player.InputSuspended && Time.timeScale==0,"Menu look never moves gameplay or releases the pause/input gate");
            }
            finally
            {
                if(mouse!=null)InputSystem.RemoveDevice(mouse);
                if(settings!=null){InputSystem.settings=settings;UnityEngine.Object.Destroy(saved);}else{saved.hideFlags=flags;InputSystem.settings=saved;}
                UnityEngine.Object.Destroy(test);if(oldMouse!=null && oldMouse.added)oldMouse.MakeCurrent();
            }
            Check(!UnityEditor.ShaderUtil.ShaderHasError(menu.menuShader),"Room and title shader compiles without errors");
        }
        finally{Set("lookTarget",target);Set("lookAngles",angles);Set("lookVelocity",velocity);Age(6);rebuild.Invoke(menu,new object[]{0,0});}
        var result=new{passed=checks.All(c=>c.StartsWith("PASS")),checks};System.IO.File.WriteAllText("Logs/MenuRoom/report.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
    }
}
