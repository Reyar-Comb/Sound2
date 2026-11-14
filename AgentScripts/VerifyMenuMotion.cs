using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public static class VerifyMenuMotion
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static SonicSpaceStartMenu menu;
    static List<string> checks;
    static List<object> frames;
    static void Check(bool ok,string label)=>checks.Add((ok?"PASS: ":"FAIL: ")+label);
    static object Field(string name)=>typeof(SonicSpaceStartMenu).GetField(name,Flags).GetValue(menu);
    static void Age(float age)
    {
        typeof(SonicSpaceStartMenu).GetField("introStartedAt",Flags).SetValue(menu,Time.unscaledTime-age);
        typeof(SonicSpaceStartMenu).GetMethod("RenderGlyphs",Flags).Invoke(menu,null);
    }
    sealed class Frame
    {
        public int ink,dots,minY=720,maxY,maxX,minX=1280;public float white,center,corners;
    }
    static Frame Draw(string name,float opening,float transition=0,float density=-1,float scale=-1,bool emptyMask=false)
    {
        var material=new Material((Material)Field("menuMaterial"));var view=menu.player.GetComponentInChildren<Camera>();
        float pitch=BlackBoxPointCloudFeature.GetDotSpacing(view);
        material.SetVector("_MenuSize",new Vector4(1280,720,1280,720));material.SetFloat("_GridPitch",pitch);
        material.SetFloat("_DotRadius",BlackBoxPointCloudFeature.GetDotDiameter(view)*.5f);
        material.SetFloat("_OpeningTime",opening);material.SetFloat("_Transition",transition);material.SetFloat("_Hover",0);
        material.SetFloat("_TitleDensity",density<0?menu.titlePointDensity:density);material.SetFloat("_TitleDotScale",scale<0?menu.titleDotScale:scale);
        var previous=RenderTexture.active;var rt=RenderTexture.GetTemporary(1280,720,0,RenderTextureFormat.ARGB32);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        var f=new Frame();int white=0,centerCount=0,cornerCount=0;
        try
        {
            RenderTexture.active=rt;GL.Clear(false,true,Color.black);
            Graphics.Blit(emptyMask?Texture2D.blackTexture:(Texture)Field("mask"),rt,material);
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();var pixels=image.GetPixels32();
            for(int y=0;y<720;y++)for(int x=0;x<1280;x++)
            {
                byte value=pixels[y*1280+x].r;if(value>240)white++;
                if(value<128){f.ink++;f.minX=Math.Min(f.minX,x);f.maxX=Math.Max(f.maxX,x);f.minY=Math.Min(f.minY,y);f.maxY=Math.Max(f.maxY,y);}
                if(Vector2.Distance(new Vector2(x,y),new Vector2(640,360))<65){f.center+=value/255f;centerCount++;}
                if((x<80 || x>1200) && (y<80 || y>640)){f.corners+=value/255f;cornerCount++;}
            }
            for(float y=pitch*.5f;y<720;y+=pitch)for(float x=pitch*.5f;x<1280;x+=pitch)
                if(pixels[Mathf.Clamp(719-(int)y,0,719)*1280+Mathf.Clamp((int)x,0,1279)].r<128)f.dots++;
            f.white=(float)white/pixels.Length;f.center/=centerCount;f.corners/=cornerCount;
            System.IO.File.WriteAllBytes("Logs/MenuMotion/"+name+".png",image.EncodeToPNG());
            frames.Add(new {name,f.ink,f.dots,f.white,f.center,f.corners,spanY=f.maxY-f.minY,spanX=f.maxX-f.minX});return f;
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(material);}
    }
    static float[] CharacterYs()
    {
        var text=(TextMeshProUGUI)Field("title");
        return Enumerable.Range(0,text.textInfo.characterCount).Select(i=>{
            var c=text.textInfo.characterInfo[i];return text.textInfo.meshInfo[c.materialReferenceIndex].vertices[c.vertexIndex].y;
        }).ToArray();
    }
    public static async Task<object> Main()
    {
        menu=UnityEngine.Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(menu==null || menu.HasStarted || menu.IsTransitioning)throw new Exception("A fresh menu is required.");
        System.IO.Directory.CreateDirectory("Logs/MenuMotion");checks=new();frames=new();
        var rebuild=typeof(SonicSpaceStartMenu).GetMethod("RebuildMask",Flags);
        try
        {
            rebuild.Invoke(menu,new object[]{1280,720});Array.Clear((Array)Field("characterPositioned"),0,10);
            Age(0);var blank=Draw("00-void",0);Check(blank.ink==0 && blank.white>.999f,"Opening begins in a completely blank white void");
            Age(.25f);var line=Draw("01-line",.25f);
            Check(menu.VisibleTitleCharacters==0 && line.maxX-line.minX>1200 && line.maxY-line.minY<12,"A thin dotted line spans the screen before the title");
            menu.BeginGame();Check(!menu.IsTransitioning && !menu.IntroReady,"Invisible Start cannot skip the opening");
            int previousCount=0;
            foreach(float age in new[]{.58f,.77f,1.02f,1.30f,1.80f})
            {
                Age(age);Draw("type-"+age.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),age);
                int count=menu.VisibleTitleCharacters;var title=(TextMeshProUGUI)Field("title");
                Check(count>previousCount && title.text=="SONICSPACE".Substring(0,count),"Sequential centered title prefix: "+title.text);
                if(count==1)
                {
                    var mesh=title.textInfo.meshInfo[0].vertices;
                    Check(Mathf.Abs((mesh[0].x+mesh[2].x)*.5f)<8,"First S appears at the horizontal centre");
                }
                previousCount=count;
            }
            Age(3.4f);
            // Phase snapshots skip real frames: let the horizontal typing layout catch up before its final still.
            for(int frame=0;frame<120;frame++)typeof(SonicSpaceStartMenu).GetMethod("RenderGlyphs",Flags).Invoke(menu,null);
            var ready=Draw("02-ready",3.4f);
            Check(menu.IntroReady && menu.VisibleTitleCharacters==10,"Complete title and Start become available");
            var fine=Draw("baseline-fine-title",0,0,1,1);var bold=Draw("half-density-bold-title",0);
            float ratio=(float)bold.dots/fine.dots;
            Check(ratio>.46f && ratio<.54f,"Title point count is halved (ratio="+ratio.ToString("F3")+")");
            Check(bold.ink>fine.ink*1.1f,"Larger dots produce stronger black lettering despite half as many dots");
            var wave=Draw("wave-unmasked",3.4f,0,0,-1,true);var protectedWave=Draw("wave-text-clearance",3.4f,0,0);
            Check(wave.maxY-wave.minY>line.maxY-line.minY+35,"Soundwave amplitude grows from the opening line");
            Check(protectedWave.ink>0 && protectedWave.ink<wave.ink*.95f,"Animated glyphs suppress nearby waveform dots while preserving the rest");
            var waveLater=Draw("wave-later",4.9f,0,0,-1,true);
            Check(waveLater.ink!=wave.ink || waveLater.maxY!=wave.maxY,"Soundwave continues changing after the title is complete");
            var collapses=new List<Frame>();
            foreach(float phase in new[]{.16f,.36f,.56f,.76f,.96f})collapses.Add(Draw("collapse-"+phase.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),3.4f,phase));
            Check(collapses[1].center<.1f && collapses[1].corners>.9f,"The centre sinks into darkness while the outer space stays white");
            Check(collapses.Zip(collapses.Skip(1),(a,b)=>b.white<a.white).All(x=>x),"Collapsing region advances outward and eventually consumes the screen");
            Check(collapses[^1].white<.001f,"Final collapse has no white-screen flash before gameplay");
            Age(4);var before=CharacterYs();await Task.Delay(650);var after=CharacterYs();
            var movement=after.Zip(before,(a,b)=>a-b).ToArray();
            Check(movement.Any(d=>Mathf.Abs(d)>.15f) && movement.Max()-movement.Min()>.2f,"Letters float independently in actual unscaled-time frames");
            Check(Time.timeScale==0 && menu.player.InputSuspended,"Opening and collapse previews preserve paused gameplay");
            Check(!UnityEditor.ShaderUtil.ShaderHasError(menu.menuShader) && !UnityEditor.ShaderUtil.ShaderHasError(menu.glyphMaskShader),"Animated menu shaders compile successfully");
        }
        finally{Age(6);rebuild.Invoke(menu,new object[]{0,0});}
        var report=new {passed=checks.All(s=>s.StartsWith("PASS:")),checks,frames};
        System.IO.File.WriteAllText("Logs/MenuMotion/report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
