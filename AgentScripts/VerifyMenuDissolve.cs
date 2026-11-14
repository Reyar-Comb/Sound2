using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class VerifyMenuDissolve
{
    const int W=1280,H=720;
    public static object Main()
    {
        var menu=UnityEngine.Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(menu==null)throw new Exception("Scene menu component required; the shader preview does not change gameplay.");
        var material=new Material(menu.menuShader);var view=menu.player.GetComponentInChildren<Camera>();
        float pitch=BlackBoxPointCloudFeature.GetDotSpacing(view),radius=BlackBoxPointCloudFeature.GetDotDiameter(view)*.5f;
        material.SetVector("_MenuSize",new Vector4(W,H,W,H));material.SetFloat("_GridPitch",pitch);material.SetFloat("_DotRadius",radius);
        material.SetFloat("_TitleDensity",.5f);material.SetFloat("_TitleDotScale",1.65f);
        var rt=RenderTexture.GetTemporary(W,H,0,RenderTextureFormat.ARGB32);var texture=new Texture2D(W,H,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        var checks=new List<string>();void Check(bool ok,string name)=>checks.Add((ok?"PASS: ":"FAIL: ")+name);
        System.IO.Directory.CreateDirectory("Logs/MenuDissolve");
        Color32[] Draw(float t,float opening=0,string file=null)
        {
            material.SetFloat("_Transition",t);material.SetFloat("_OpeningTime",opening);
            RenderTexture.active=rt;GL.Clear(false,true,Color.black);Graphics.Blit(Texture2D.blackTexture,rt,material);
            texture.ReadPixels(new Rect(0,0,W,H),0,0);texture.Apply();
            if(file!=null)System.IO.File.WriteAllBytes("Logs/MenuDissolve/"+file+".png",texture.EncodeToPNG());
            return texture.GetPixels32();
        }
        int Index(float x,float y)=>Mathf.Clamp(H-1-Mathf.FloorToInt(y),0,H-1)*W+Mathf.Clamp(Mathf.FloorToInt(x),0,W-1);
        var cells=new List<Vector2>();var radii=new List<float>();
        for(float y=pitch*.5f;y<H-pitch;y+=pitch)for(float x=pitch*.5f;x<W-pitch;x+=pitch)
        {cells.Add(new Vector2(x,y));radii.Add(Vector2.Distance(new Vector2(x,y),new Vector2(W/2,H/2))/new Vector2(W/2,H/2).magnitude);}
        var sequences=cells.Select(_=>new List<byte>()).ToArray();
        int cuts=0,fades=0,blinks=0,centroids=0,offGrid=0;float largestOffset=0;
        try
        {
            var bars=Draw(0,3.4f,"spectrum");var later=Draw(0,4.9f,"spectrum-later");
            var heights=new List<int>();var columns=new List<int>();int changed=0,column=0;
            for(float x=pitch*.5f;x<W;x+=pitch,column++)
            {
                int count=0,other=0;
                for(float y=pitch*.5f;y<H;y+=pitch){if(bars[Index(x,y)].r<128)count++;if(later[Index(x,y)].r<128)other++;}
                if(count>1){columns.Add(column);heights.Add(count);}if(count!=other)changed++;
            }
            Check(columns.Count>30 && columns.Zip(columns.Skip(1),(a,b)=>b-a>=3).All(x=>x),"Spectrum consists of separated vertical stacks with empty columns between them");
            Check(heights.Distinct().Count()>8 && heights.Max()>10,"Individual spectrum columns have distinct multi-dot heights");
            Check(changed>20,"Spectrum bars keep changing independently over time");
            for(int frame=0;frame<=120;frame++)
            {
                float t=frame*.0075f;var pixels=Draw(t,0,frame==48?"dissolve-middle":frame==80?"dissolve-outer":null);
                for(int c=0;c<cells.Count;c++)sequences[c].Add(pixels[Index(cells[c].x,cells[c].y)].r);
                if(frame%8!=0)continue;
                // Measure actual rendered centroids, without duplicating the shader's random/timing formulas.
                for(int c=0;c<cells.Count;c+=73)
                {
                    var center=cells[c];float total=0,xsum=0,ysum=0,min=1,max=0;
                    // Exclude exact cell-boundary pixels: GPU interpolation can assign those ties to either neighbour.
                    for(int y=Mathf.CeilToInt(center.y-pitch*.5f-.499f);y<center.y+pitch*.5f-.501f;y++)
                    for(int x=Mathf.CeilToInt(center.x-pitch*.5f-.499f);x<center.x+pitch*.5f-.501f;x++)
                    {
                        if(x<0||x>=W||y<0||y>=H)continue;
                        float v=pixels[(H-1-y)*W+x].r/255f;min=Mathf.Min(min,v);max=Mathf.Max(max,v);
                        total+=v;xsum+=(x+.5f)*v;ysum+=(y+.5f)*v;
                    }
                    if(min>.02f || max<.2f || total<.2f)continue;
                    float offset=Vector2.Distance(center,new Vector2(xsum/total,ysum/total));
                    centroids++;largestOffset=Mathf.Max(largestOffset,offset);if(offset>.45f)offGrid++;
                }
            }
            var inner=new List<float>();var outer=new List<float>();var ring=new List<float>();
            for(int c=0;c<cells.Count;c++)
            {
                var s=sequences[c];int last=s.FindLastIndex(v=>v>20);float end=last*.0075f;
                if(radii[c]<.15f)inner.Add(end);if(radii[c]>.85f)outer.Add(end);if(radii[c]>.45f && radii[c]<.47f)ring.Add(end);
                if(radii[c]<.2f || radii[c]>.65f)continue;
                int partial=s.Count(v=>v>10 && v<245);bool rebound=s.Zip(s.Skip(1),(a,b)=>b-a>70).Any(v=>v);
                if(rebound)blinks++;else if(partial>=4)fades++;else if(partial<=1)cuts++;
            }
            Check(centroids>100 && offGrid==0,"Dots stay on fixed grid centres in every sampled transition frame (max offset="+largestOffset.ToString("F3")+"px)");
            Check(cuts>200 && fades>200 && blinks>200,"Rendered time series contain cuts, smooth fades and blinking ("+cuts+" / "+fades+" / "+blinks+")");
            Check(outer.Average()>inner.Average()+.3f,"Central cells disappear well before outer cells");
            Check(ring.Max()-ring.Min()>.15f,"Cells at the same radial distance disappear at varied times");
            var finish=Draw(1,0,"finished");Check(finish.All(p=>p.r==0 && p.g==0 && p.b==0),"Transition finishes fully transparent over the black game frame");
            Check(!ShaderUtil.ShaderHasError(menu.menuShader),"Spectrum/dissolve shader compiles successfully");
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(material);}
        var report=new {passed=checks.All(c=>c.StartsWith("PASS")),checks,cuts,fades,blinks,centroids,offGrid,largestOffset};
        System.IO.File.WriteAllText("Logs/MenuDissolve/report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
