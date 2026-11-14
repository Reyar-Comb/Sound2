using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

public static class VerifyVinesAndStars
{
    static List<string> checks=new();
    static void Check(bool ok,string text)=>checks.Add((ok?"PASS: ":"FAIL: ")+text);
    static Vector4[] Points(MonsterVines vines){var data=new Vector4[MonsterVines.Capacity];int count=vines.Collect(data);return data.Take(count).ToArray();}
    static async Task<Color32[]> Capture(string name)
    {
        string path="Logs/VinesStars/"+name+".png";ScreenCapture.CaptureScreenshot(path);await Task.Delay(150);
        var image=new Texture2D(2,2);image.LoadImage(System.IO.File.ReadAllBytes(path));var pixels=image.GetPixels32();UnityEngine.Object.Destroy(image);return pixels;
    }
    static int Red(Color32[] data)=>data.Count(p=>p.r>30 && p.g<5 && p.b<5);
    static Color32[] Stars(Material source,float age,Quaternion rotation,Vector3 position,bool mask=false)
    {
        var material=new Material(source);var target=RenderTexture.GetTemporary(1280,720,0,RenderTextureFormat.ARGB32);
        var grid=new Texture2D(256,144,TextureFormat.RGBA32,false);grid.SetPixels(new Color[256*144]);grid.Apply();
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;var command=new CommandBuffer();
        try
        {
            var props=new MaterialPropertyBlock();props.SetVector("_PointViewport",new Vector4(1280,720,1f/1280,1f/720));props.SetTexture("_PointGrid",grid);
            props.SetFloat("_PointAnimationTime",age);props.SetFloat("_ScreenDotSpacing",5.5f);props.SetFloat("_ScreenDotRadius",1.5f);
            var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(position,rotation,Vector3.one).inverse;
            props.SetMatrix("_StarViewProjection",GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(80,1280f/720,.1f,100),true)*view);
            props.SetVector("_CrosshairClearZone",Vector4.zero);props.SetInt("_PlayerUiCount",mask?1:0);
            props.SetVectorArray("_PlayerUiRects",new[]{new Vector4(640,360,640,360)});props.SetVectorArray("_PlayerUiOptions",new[]{new Vector4(0,1,0,1)});
            command.SetRenderTarget(target);command.ClearRenderTarget(false,true,Color.black);
            command.SetGlobalFloat("_BlackBoxPlayerActive",1);command.SetGlobalVector("_BlackBoxStyleVisibility",Vector4.one);
            command.SetGlobalVector("_BlackBoxRevealModes",Vector4.zero);
            command.DrawProcedural(Matrix4x4.identity,material,3,MeshTopology.Triangles,6,384,props);Graphics.ExecuteCommandBuffer(command);
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();if(age==4 && rotation==Quaternion.identity && position==Vector3.zero)System.IO.File.WriteAllBytes("Logs/VinesStars/stars-"+mask+".png",image.EncodeToPNG());return image.GetPixels32();
        }
        finally{RenderTexture.active=previous;command.Release();RenderTexture.ReleaseTemporary(target);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(grid);UnityEngine.Object.Destroy(image);}
    }
    public static async Task<object> Main()
    {
        if(SonicSpaceStartMenu.BlocksGameplay)throw new Exception("Enter gameplay before testing");
        checks.Clear();System.IO.Directory.CreateDirectory("Logs/VinesStars");
        var player=UnityEngine.Object.FindAnyObjectByType<PlayerController>();var camera=player.GetComponentInChildren<Camera>();var sonar=camera.GetComponent<SphericalSonar>();
        var source=player.GetComponent<BlackBoxProximitySource>();if(source==null)source=UnityEngine.Object.FindAnyObjectByType<BlackBoxProximitySource>();
        var originals=UnityEngine.Object.FindObjectsByType<PatrolMonster>();Check(originals.Length>0 && originals.All(m=>m.GetComponent<MonsterVines>()!=null),"Existing scene monsters automatically acquire surface vines");
        var stars=Stars(sonar.renderingSettings,4,Quaternion.identity,Vector3.zero);var later=Stars(sonar.renderingSettings,8,Quaternion.identity,Vector3.zero);
        var turned=Stars(sonar.renderingSettings,4,Quaternion.Euler(15,60,0),Vector3.zero);var translated=Stars(sonar.renderingSettings,4,Quaternion.identity,new Vector3(27,3,18));
        Check(stars.Count(p=>p.r>8)>20 && stars.All(p=>p.r==p.g && p.g==p.b),"Gameplay stars render white with both proximity and sonar reveal disabled");
        Check(!stars.SequenceEqual(later),"Gameplay stars independently fade in and out");Check(!stars.SequenceEqual(turned),"Camera rotation moves spatial star directions");
        Check(stars.SequenceEqual(translated),"Camera translation does not drag the distant star field");
        int off=0,offFlipped=0;for(int y=0;y<720;y++)for(int x=0;x<1280;x++)if(stars[y*1280+x].r>8)
        {var p=new Vector2(x+.5f,719.5f-y);var c=new Vector2((Mathf.Floor(p.x/5.5f)+.5f)*5.5f,(Mathf.Floor(p.y/5.5f)+.5f)*5.5f);if(Vector2.Distance(p,c)>2.22f)off++;p.y=720-p.y;c.y=(Mathf.Floor(p.y/5.5f)+.5f)*5.5f;if(Vector2.Distance(p,c)>2.22f)offFlipped++;}
        Check(Math.Min(off,offFlipped)==0,"Offscreen star centers align to the target lattice (accounting for render-texture Y orientation)");
        Check(Stars(sonar.renderingSettings,4,Quaternion.identity,Vector3.zero,true).All(p=>p.r==0),"Player UI excludes nearby ambient stars");source.PublishPosition();
        var fixture=new GameObject("Vine verification fixture");var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(fixture.transform);floor.transform.position=new Vector3(100,99.9f,100);floor.transform.localScale=new Vector3(12,.2f,12);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(fixture.transform);wall.transform.position=new Vector3(100,101.5f,102.1f);wall.transform.localScale=new Vector3(12,3,.2f);
        var black=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");floor.GetComponent<Renderer>().sharedMaterial=black;wall.GetComponent<Renderer>().sharedMaterial=black;
        var actor=new GameObject("Vine actor");actor.transform.SetParent(fixture.transform);actor.transform.position=new Vector3(100,100,100);var vines=actor.AddComponent<MonsterVines>();
        var cc=player.GetComponent<CharacterController>();var motion=player.GetComponent<PlayerCameraMotion>();var hud=player.GetComponent<PlayerStatusHUD>();
        bool wasPlayer=player.enabled,wasCC=cc.enabled,wasMotion=motion!=null && motion.enabled,wasHud=hud.visible;
        var pos=camera.transform.position;var rot=camera.transform.rotation;bool near=sonar.proximityRendering,scan=sonar.sonarRendering,auto=sonar.automatic;
        bool oldStars=source.IsStyleEnabled(BlackBoxProximitySource.PointStyle.Stars);
        try
        {
            player.enabled=false;cc.enabled=false;if(motion!=null)motion.enabled=false;hud.visible=false;
            camera.transform.SetPositionAndRotation(new Vector3(100,102.2f,96),Quaternion.Euler(20,0,0));sonar.automatic=false;sonar.SetRevealModes(false,false);source.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars,true);
            var sky=await Capture("gameplay-stars");int gridMiss=0,starPixels=0;float spacing=sonar.renderingSettings.GetFloat("_ScreenDotSpacing"),dotRadius=sonar.dotRadius,scale=720f/Screen.height;
            for(int y=Screen.height/2+35;y<Screen.height;y++)for(int x=0;x<Screen.width;x++)
            {var color=sky[y*Screen.width+x];if(color.r<8 || Math.Abs(color.r-color.g)>2 || Math.Abs(color.r-color.b)>2)continue;starPixels++;
            var p=new Vector2((x+.5f)*scale,(Screen.height-y-.5f)*scale);var c=new Vector2((Mathf.Floor(p.x/spacing)+.5f)*spacing,(Mathf.Floor(p.y/spacing)+.5f)*spacing);if(Vector2.Distance(p,c)>dotRadius+.72f*scale)gridMiss++;}
            Check(starPixels>0 && gridMiss==0,"Actual gameplay star grid alignment: pixels="+starPixels+", off="+gridMiss);
            source.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars,false);
            Physics.SyncTransforms();int frame=Time.frameCount;await Task.Delay(1500);Check(Time.frameCount>frame+10,"Actual gameplay frames advance during verification");
            var first=Points(vines);Check(first.Length>60,"Vines grow multiple branching traces");
            Check(first.Count(p=>Mathf.Abs(p.y-100.025f)<.035f)>20,"Vines attach to the actual floor");
            Check(first.Count(p=>Mathf.Abs(p.z-101.975f)<.04f && p.y>100.1f)>5,"Advancing floor vines climb the adjacent wall");
            Check(first.All(p=>Mathf.Abs(p.y-100.025f)<.04f || Mathf.Abs(p.z-101.975f)<.04f),"Every vine sample stays attached to a real surface");
            var visible=await Capture("vines-floor-wall");Check(Red(visible)>100,"Runtime render graph draws visible red vine dots ("+Red(visible)+")");
            PlayerUiPointMask.SetLegacyPanel(vines,new Rect(0,0,Screen.width,Screen.height),true);var masked=await Capture("vines-ui-masked");Check(Red(masked)==0,"Player UI clears red vine dots as well as other scene dots");PlayerUiPointMask.SetLegacyPanel(vines,default,false);
            var occluder=GameObject.CreatePrimitive(PrimitiveType.Cube);occluder.transform.SetParent(fixture.transform);occluder.transform.position=new Vector3(100,102,97.5f);occluder.transform.localScale=new Vector3(15,10,.3f);occluder.GetComponent<Renderer>().sharedMaterial=black;
            var hidden=await Capture("vines-occluded");Check(Red(hidden)==0,"Solid intervening geometry completely occludes vines");UnityEngine.Object.Destroy(occluder);await Task.Delay(50);
            typeof(MonsterVines).GetField("nextGrowth",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(vines,Time.time+100);
            var before=Points(vines);for(int i=0;i<12;i++){actor.transform.position+=Vector3.right*.08f;await Task.Delay(20);}
            var after=Points(vines);Check(after.Any(a=>before.Any(b=>Vector3.Distance((Vector3)a,(Vector3)b)<.001f)),"Existing vine samples stay world-anchored while the actor moves");
            Check(Vector3.Dot(vines.TravelDirection,Vector3.right)>.65f,"Growth direction follows actual actor travel");
            await Task.Delay(3600);Check(Points(vines).Length==0,"Old rear trails fade away completely without accumulating");
            actor.transform.position+=Vector3.forward*20;await Task.Delay(100);Check(Points(vines).Length==0,"Teleport across empty space leaves no floating old vines");
            vines.enabled=false;Check(!MonsterVines.Instances.Contains(vines) && vines.PointCount==0,"Disable/floor unload removes the vine registry and trail");
            Check(!ShaderUtil.ShaderHasError(sonar.renderingSettings.shader),"Point shader compiles successfully including vines and stars");
        }
        finally
        {
            PlayerUiPointMask.SetLegacyPanel(vines,default,false);UnityEngine.Object.Destroy(fixture);
            camera.transform.SetPositionAndRotation(pos,rot);player.enabled=wasPlayer;cc.enabled=wasCC;if(motion!=null)motion.enabled=wasMotion;hud.visible=wasHud;
            sonar.automatic=auto;sonar.SetRevealModes(near,scan);source.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars,oldStars);
        }
        var report=new{passed=checks.All(c=>c.StartsWith("PASS")),checks};System.IO.File.WriteAllText("Logs/VinesStars/report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
