using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>Scene-local entry screen. Owns only its pause, input gate and temporary UI resources.</summary>
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class SonicSpaceStartMenu : MonoBehaviour
{
    public PlayerController player;
    public Shader menuShader, glyphMaskShader;
    [Min(.5f)] public float transitionSeconds=2.6f;
    [Header("Opening animation")]
    [Min(0)] public float titleDelay=.55f;
    [Min(.04f)] public float characterInterval=.13f;
    [Range(1,2.5f)] public float titleDotScale=1.65f;
    [Range(.1f,1)] public float titlePointDensity=.5f;
    [Header("Menu room")]
    [Min(2)] public float roomSize=10f;
    [Range(50,110)] public float roomFieldOfView=80f;
    [Min(0)] public float mouseSensitivity=.18f;
    [Min(.01f)] public float lookSmoothTime=.065f;
    [Range(0,1)] public float titleShimmer=1f;
    public Quaternion RoomViewRotation {get;private set;}=Quaternion.identity;
    public float OpeningElapsed => Mathf.Max(0,Time.unscaledTime-introStartedAt);
    public int VisibleTitleCharacters {get;private set;}
    public bool IntroReady => OpeningElapsed>=titleDelay+Title.Length*characterInterval+.55f;
    const string Title="SONICSPACE";
    public bool IsTransitioning {get;private set;}
    public bool HasStarted {get;private set;}
    public float TransitionProgress {get;private set;}
    public static bool BlocksGameplay => active!=null && !active.HasStarted;
    static SonicSpaceStartMenu active;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()=>active=null;

    Canvas canvas,maskCanvas;
    UnityEngine.UI.RawImage image;
    TextMeshProUGUI title;
    Material menuMaterial,glyphMaterial;
    RenderTexture mask;
    Camera view;
    float previousTimeScale,startedAt,introStartedAt;
    Vector2 lookTarget,lookAngles,lookVelocity;
    TextMeshProUGUI[] labels;
    CommandBuffer maskCommands;
    MaterialPropertyBlock glyphProperties;
    bool previousAudioPause,previousInputSuspended,ownsPause,finishing,inputArmed,openingStarted;
    int width,height,openedFrame;

    void Awake()
    {
        if(active!=null && active!=this){enabled=false;return;}
        if(player==null)player=FindAnyObjectByType<PlayerController>();
        if(player==null || menuShader==null || glyphMaskShader==null || GameUiTheme.Current?.textFont==null)
        {Debug.LogError("Start menu requires the player, menu shaders and GameUiTheme font.",this);enabled=false;return;}
        active=this;openedFrame=Time.frameCount;introStartedAt=Time.unscaledTime;view=player.GetComponentInChildren<Camera>();
        previousTimeScale=Time.timeScale;previousAudioPause=AudioListener.pause;previousInputSuspended=player.InputSuspended;
        ownsPause=true;Time.timeScale=0;AudioListener.pause=true;player.SetInputCaptured(false);
        BuildUI();
    }

    void BuildUI()
    {
        var go=new GameObject("SONICSPACE Start Menu",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));
        go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32000;
        var scaler=go.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=1;
        var screen=new GameObject("Point Screen",typeof(RectTransform),typeof(UnityEngine.UI.RawImage));screen.transform.SetParent(go.transform,false);
        image=screen.GetComponent<UnityEngine.UI.RawImage>();image.raycastTarget=false;
        var rect=image.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        menuMaterial=new Material(menuShader){name="SONICSPACE menu (runtime)"};glyphMaterial=new Material(glyphMaskShader){name="Menu glyph rasterizer (runtime)"};image.material=menuMaterial;

        // Never shown directly: TMP supplies the real SECONDERSIGN glyphs (including fallback submeshes).
        var templates=new GameObject("Menu glyph templates",typeof(RectTransform),typeof(Canvas));templates.transform.SetParent(transform,false);
        maskCanvas=templates.GetComponent<Canvas>();maskCanvas.renderMode=RenderMode.WorldSpace;maskCanvas.enabled=false;
        title=MakeLabel("Main title","",118,0,Color.red);
        labels=new[]{title};
        glyphProperties=new MaterialPropertyBlock();
        maskCommands=new CommandBuffer {name="Rasterize centered SECONDERSIGN menu glyphs"};
        RebuildMask();
    }

    TextMeshProUGUI MakeLabel(string name,string value,float size,float y,Color channel)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(maskCanvas.transform,false);
        var text=go.GetComponent<TextMeshProUGUI>();text.font=GameUiTheme.Current.textFont;text.text=value;text.fontSize=size;
        text.enableAutoSizing=true;text.fontSizeMin=24;text.fontSizeMax=size;text.textWrappingMode=TextWrappingModes.NoWrap;
        text.alignment=TextAlignmentOptions.Center;text.color=channel;text.raycastTarget=false;text.richText=false;
        text.rectTransform.anchoredPosition=new Vector2(0,y);return text;
    }

    void RebuildMask(int pixelWidth=0,int pixelHeight=0)
    {
        width=Mathf.Max(1,pixelWidth>0?pixelWidth:Screen.width);height=Mathf.Max(1,pixelHeight>0?pixelHeight:Screen.height);
        float referenceWidth=720f*width/height;
        if(mask!=null){mask.Release();Destroy(mask);}
        // 720p layout with an oversampled font mask; the point grid itself remains resolution independent.
        mask=new RenderTexture(Mathf.Clamp(Mathf.RoundToInt(referenceWidth*2),64,4096),1440,0,RenderTextureFormat.ARGB32)
            {name="SONICSPACE title mask",filterMode=FilterMode.Bilinear};mask.Create();image.texture=mask;
        foreach(var text in labels)text.rectTransform.sizeDelta=new Vector2(Mathf.Max(100,referenceWidth-64),160);
        RenderGlyphs();
    }

    static float Smooth01(float t) {t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}

    void RenderGlyphs()
    {
        float age=OpeningElapsed;
        int count=Mathf.Clamp(Mathf.FloorToInt((age-titleDelay)/Mathf.Max(.04f,characterInterval))+1,0,Title.Length);
        if(count!=VisibleTitleCharacters){title.text=Title.Substring(0,count);VisibleTitleCharacters=count;}
        float referenceWidth=720f*width/height;
        var command=maskCommands;command.Clear();
        command.SetRenderTarget(mask);command.ClearRenderTarget(false,true,Color.clear);
        command.SetGlobalMatrix("_MaskProjection",GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-referenceWidth*.5f,referenceWidth*.5f,-360,360,-1,1),true));
        foreach(var text in labels)
        {
            text.ForceMeshUpdate(true,true);
            // Center the visible glyph geometry, not the font's asymmetric ascender/descender box.
            Vector3 offset=Vector3.zero;
            if(text==title && text.textInfo.characterCount>0)
            {
                Vector2 min=new Vector2(float.PositiveInfinity,float.PositiveInfinity),max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
                for(int c=0;c<text.textInfo.characterCount;c++)
                {
                    var glyph=text.textInfo.characterInfo[c];if(!glyph.isVisible)continue;
                    var vertices=text.textInfo.meshInfo[glyph.materialReferenceIndex].vertices;
                    for(int k=0;k<4;k++){Vector2 v=vertices[glyph.vertexIndex+k];min=Vector2.Min(min,v);max=Vector2.Max(max,v);}
                }
                if(!float.IsInfinity(min.x))offset=-(Vector3)((min+max)*.5f);
            }
            for(int c=0;c<text.textInfo.characterCount;c++)
            {
                var character=text.textInfo.characterInfo[c];if(!character.isVisible)continue;
                var meshInfo=text.textInfo.meshInfo[character.materialReferenceIndex];int v=character.vertexIndex;
                float born=titleDelay+c*characterInterval;
                float appear=Smooth01((age-born)/.26f);
                for(int corner=0;corner<4;corner++)
                {
                    meshInfo.vertices[v+corner]+=offset;
                    var color=meshInfo.colors32[v+corner];color.a=(byte)(255*appear);meshInfo.colors32[v+corner]=color;
                }
            }
            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices|TMP_VertexDataUpdateFlags.Colors32);
            var materials=text.fontSharedMaterials;
            for(int i=0;i<text.textInfo.materialCount;i++)
            {
                var mesh=text.textInfo.meshInfo[i];if(mesh.vertexCount==0)continue;
                glyphProperties.SetTexture("_MainTex",materials[i].mainTexture);
                command.DrawMesh(mesh.mesh,Matrix4x4.Translate(text.rectTransform.anchoredPosition),glyphMaterial,0,0,glyphProperties);
            }
        }
        Graphics.ExecuteCommandBuffer(command);
    }

    void Update()
    {
        if(!ownsPause || HasStarted)return;
        // Start after scene initialization; a loading hitch must not skip the white void or typed letters.
        if(!openingStarted){introStartedAt=Time.unscaledTime;openingStarted=true;}
        else if(!IsTransitioning)introStartedAt+=Mathf.Max(0,Time.unscaledDeltaTime-.05f);
        if(width!=Screen.width || height!=Screen.height)RebuildMask();
        else RenderGlyphs();
        UpdateRoomLook();
        // Any fresh left click enters; keep the opening/held-input gate and consume the transition click.
        if(inputArmed && IntroReady && !IsTransitioning && (Mouse.current?.leftButton.wasPressedThisFrame==true
            || Keyboard.current?.enterKey.wasPressedThisFrame==true || Keyboard.current?.spaceKey.wasPressedThisFrame==true
            || Gamepad.current?.buttonSouth.wasPressedThisFrame==true))BeginGame();
        // Opening a scene while a submit key/button is held must not skip its title screen.
        if(!inputArmed && Time.frameCount>openedFrame+1 && Mouse.current?.leftButton.isPressed!=true
            && Keyboard.current?.enterKey.isPressed!=true && Keyboard.current?.spaceKey.isPressed!=true
            && Gamepad.current?.buttonSouth.isPressed!=true)inputArmed=true;
        if(IsTransitioning)TransitionProgress=Mathf.Clamp01((Time.unscaledTime-startedAt)/Mathf.Max(.5f,transitionSeconds));
        menuMaterial.SetVector("_MenuSize",new Vector4(width,height,720f*width/height,720));
        menuMaterial.SetFloat("_GridPitch",BlackBoxPointCloudFeature.GetDotSpacing(view));
        menuMaterial.SetFloat("_DotRadius",BlackBoxPointCloudFeature.GetDotDiameter(view)*.5f);
        menuMaterial.SetFloat("_TitleDotScale",titleDotScale);menuMaterial.SetFloat("_TitleDensity",titlePointDensity);
        menuMaterial.SetFloat("_TitleShimmer",titleShimmer);menuMaterial.SetFloat("_MenuStarsEnabled",1);
        menuMaterial.SetFloat("_RoomHalfSize",Mathf.Max(2,roomSize)*.5f);
        menuMaterial.SetFloat("_RoomTanHalfFov",Mathf.Tan(Mathf.Clamp(roomFieldOfView,50,110)*Mathf.Deg2Rad*.5f));
        menuMaterial.SetMatrix("_RoomView",Matrix4x4.Rotate(RoomViewRotation));
        menuMaterial.SetFloat("_OpeningTime",OpeningElapsed);
        menuMaterial.SetFloat("_Transition",TransitionProgress);
        if(TransitionProgress>=1 && !finishing){finishing=true;StartCoroutine(FinishAfterInputFrame());}
    }

    void UpdateRoomLook()
    {
        if(IsTransitioning)return;
        // Menu-only look: gameplay stays paused while the pointer looks around.
        if(Mouse.current!=null)
        {
            Vector2 position=Mouse.current.position.ReadValue();
            if(position.x>=0 && position.x<=Screen.width && position.y>=0 && position.y<=Screen.height)
            {
                Vector2 delta=Vector2.ClampMagnitude(Mouse.current.delta.ReadValue(),150)*mouseSensitivity;
                lookTarget.x+=delta.x;lookTarget.y=Mathf.Clamp(lookTarget.y-delta.y,-78,78);
            }
        }
        if(Gamepad.current!=null)lookTarget+=new Vector2(Gamepad.current.rightStick.x.ReadValue(),-Gamepad.current.rightStick.y.ReadValue())*(75*Time.unscaledDeltaTime);
        lookTarget.y=Mathf.Clamp(lookTarget.y,-78,78);
        // Keep yaw numerically bounded without changing the relative smoothing distance.
        float turns=Mathf.Floor((lookTarget.x+180)/360)*360;lookTarget.x-=turns;lookAngles.x-=turns;
        lookAngles=Vector2.SmoothDamp(lookAngles,lookTarget,ref lookVelocity,Mathf.Max(.01f,lookSmoothTime),Mathf.Infinity,Time.unscaledDeltaTime);
        RoomViewRotation=Quaternion.Euler(lookAngles.y,lookAngles.x,0);
    }

    public void BeginGame()
    {
        if(!ownsPause || !IntroReady || IsTransitioning || HasStarted)return;
        IsTransitioning=true;startedAt=Time.unscaledTime;
    }

    IEnumerator FinishAfterInputFrame()
    {
        // Consume the menu click completely before gameplay can see mouse/keyboard input.
        yield return new WaitForEndOfFrame();
        HasStarted=true;IsTransitioning=false;canvas.enabled=false;ReleasePause();
        if(view!=null && view.TryGetComponent<SphericalSonar>(out var sonar))sonar.Emit();
        ReleaseGraphics();
    }

    void ReleasePause()
    {
        if(!ownsPause)return;
        ownsPause=false;Time.timeScale=previousTimeScale;AudioListener.pause=previousAudioPause;
        if(active==this)active=null;
        if(player!=null)player.SetInputCaptured(!previousInputSuspended);
    }
    void ReleaseGraphics()
    {
        if(mask!=null){mask.Release();Destroy(mask);mask=null;}
        if(menuMaterial!=null)Destroy(menuMaterial);if(glyphMaterial!=null)Destroy(glyphMaterial);
        maskCommands?.Release();maskCommands=null;
        if(canvas!=null)Destroy(canvas.gameObject);if(maskCanvas!=null)Destroy(maskCanvas.gameObject);
    }
    void OnDisable(){ReleasePause();ReleaseGraphics();}
}
