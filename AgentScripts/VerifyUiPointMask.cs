using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Off-centre real shader draws check UI coordinates, circle/rectangle masks and faded-out UI.
public static class VerifyUiPointMask
{
    public static object Main()
    {
        var material=new Material(Shader.Find("Sound2/Screen Grid Face And Corner Points"));
        var grid=new Texture2D(512,512,TextureFormat.RGBA32,false);
        grid.SetPixels32(new Color32[512*512]);grid.Apply();
        var previous=RenderTexture.active;
        var rows=new List<object>();
        try
        {
            foreach(int height in new[]{720,1080})
            {
                int width=height*16/9;
                var rt=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);
                var read=new Texture2D(width,height,TextureFormat.RGB24,false);
                try
                {
                    foreach(bool circle in new[]{false,true})
                    {
                        int[] lit=new int[3];
                        for(int state=0;state<3;state++)
                        {
                            var props=new MaterialPropertyBlock();
                            props.SetTexture("_PointGrid",grid);
                            props.SetVector("_PointViewport",new Vector4(width,height,1f/width,1f/height));
                            props.SetVector("_PointGridSize",new Vector4(512,512,1f/512,1f/512));
                            props.SetFloat("_ScreenDotSpacing",12);props.SetFloat("_ScreenDotRadius",3);
                            props.SetFloat("_BlackBoxPlayerActive",1);props.SetFloat("_StarsEnabled",1);
                            props.SetFloat("_StarChance",1);props.SetFloat("_StarBrightness",1);props.SetFloat("_TwinkleEnabled",0);
                            props.SetVector("_BlackBoxRevealModes",new Vector4(1,0,0,0));props.SetVector("_BlackBoxStyleVisibility",Vector4.one);
                            var matrix=new Matrix4x4();matrix.m32=1;matrix.m02=-.5f;matrix.m12=-.5f;
                            props.SetMatrix("_StarViewProjection",matrix);
                            var rects=new Vector4[PlayerUiPointMask.Capacity];var options=new Vector4[PlayerUiPointMask.Capacity];
                            rects[0]=new Vector4(320,180,circle?18:70,18);
                            options[0]=new Vector4(6,6,circle?18:0,state==2?0:1);
                            props.SetInt("_PlayerUiCount",state==0?0:1);
                            props.SetVectorArray("_PlayerUiRects",rects);props.SetVectorArray("_PlayerUiOptions",options);
                            using(var cmd=new CommandBuffer())
                            {
                                cmd.SetRenderTarget(rt);cmd.ClearRenderTarget(false,true,Color.black);
                                cmd.DrawProcedural(Matrix4x4.identity,material,3,MeshTopology.Triangles,6,128,props);
                                Graphics.ExecuteCommandBuffer(cmd);
                            }
                            RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,width,height),0,0);read.Apply();
                            foreach(var pixel in read.GetPixels32()) if(pixel.r>10) lit[state]++;
                        }
                        if(lit[0]==0 || lit[1]!=0 || lit[2]!=lit[0]) throw new InvalidOperationException($"UI GPU mask failed: {height}, circle={circle}, pixels={string.Join(",",lit)}");
                        rows.Add(new{height,circle,unmasked=lit[0],masked=lit[1],transparentUi=lit[2]});
                    }
                }
                finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(read);}
            }
            return new{passed=true,rows};
        }
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(grid);}
    }
}
