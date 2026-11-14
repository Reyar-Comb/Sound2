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
    [Min(0)] public float characterFloat=5f;
    public float OpeningElapsed => Mathf.Max(0,Time.unscaledTime-introStartedAt);
    public int VisibleTitleCharacters {get;private set;}
    public bool IntroReady => OpeningElapsed>=titleDelay+Title.Length*characterInterval+.55f;
    const string Title="SONICSPACE";
    public bool IsTransitioning {get;private set;}
    public bool HasStarted {get;private set;}
    public float TransitionProgress {get;private set;}
    public RectTransform StartButtonRect {get;private set;}
    public static bool BlocksGameplay => active!=null && !active.HasStarted;
    static SonicSpaceStartMenu active;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()=>active=null;

    Canvas canvas,maskCanvas;
    UnityEngine.UI.RawImage image;
    UnityEngine.UI.Button startButton;
    TextMeshProUGUI title,subtitle,startLabel;
    Material menuMaterial,glyphMaterial;
    RenderTexture mask;
    Camera view;
    float previousTimeScale,startedAt,hover,introStartedAt;
    readonly float[] characterX=new float[Title.Length];
    readonly bool[] characterPositioned=new bool[Title.Length];
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

        var hit=new GameObject("Start Button",typeof(RectTransform),typeof(UnityEngine.UI.Button));hit.transform.SetParent(go.transform,false);
        StartButtonRect=hit.GetComponent<RectTransform>();StartButtonRect.anchorMin=StartButtonRect.anchorMax=new Vector2(.5f,.5f);
        StartButtonRect.anchoredPosition=new Vector2(0,-182);StartButtonRect.sizeDelta=new Vector2(240,86);
        startButton=hit.GetComponent<UnityEngine.UI.Button>();startButton.transition=UnityEngine.UI.Selectable.Transition.None;
        startButton.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};startButton.onClick.AddListener(BeginGame);

        // Never shown directly: TMP supplies the real SECONDERSIGN glyphs (including fallback submeshes).
        var templates=new GameObject("Menu glyph templates",typeof(RectTransform),typeof(Canvas));templates.transform.SetParent(transform,false);
        maskCanvas=templates.GetComponent<Canvas>();maskCanvas.renderMode=RenderMode.WorldSpace;maskCanvas.enabled=false;
        title=MakeLabel("Main title","",118,66,Color.red);
        subtitle=MakeLabel("Chinese subtitle","声 之 域",62,-46,Color.red);
        startLabel=MakeLabel("Start label","开 始",46,-182,Color.green);
        labels=new[]{title,subtitle,startLabel};
        glyphProperties=new MaterialPropertyBlock();
        maskCommands=new CommandBuffer {name="Animate SECONDERSIGN menu glyphs"};
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
        float fullAt=titleDelay+Title.Length*characterInterval;
        float referenceWidth=720f*width/height;
        var command=maskCommands;command.Clear();
        command.SetRenderTarget(mask);command.ClearRenderTarget(false,true,Color.clear);
        command.SetGlobalMatrix("_MaskProjection",GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-referenceWidth*.5f,referenceWidth*.5f,-360,360,-1,1),true));
        foreach(var text in labels)
        {
            text.ForceMeshUpdate(true,true);
            for(int c=0;c<text.textInfo.characterCount;c++)
            {
                var character=text.textInfo.characterInfo[c];if(!character.isVisible)continue;
                var meshInfo=text.textInfo.meshInfo[character.materialReferenceIndex];int v=character.vertexIndex;
                float born=text==title?titleDelay+c*characterInterval:text==subtitle?fullAt:fullAt+.25f;
                float appear=Smooth01((age-born)/.26f);
                float y=(Mathf.PerlinNoise(c*7.13f+4.7f,age*.28f)*2-1)*characterFloat*1.5f
                    +Mathf.Sin(age*1.1f+c*2.399f)*characterFloat*.24f+(1-appear)*8;
                float x=0;
                if(text==title)
                {
                    float target=(meshInfo.vertices[v].x+meshInfo.vertices[v+2].x)*.5f;
                    if(!characterPositioned[c]){characterX[c]=target;characterPositioned[c]=true;}
                    characterX[c]=Mathf.Lerp(characterX[c],target,1-Mathf.Exp(-Time.unscaledDeltaTime*16));
                    x=characterX[c]-target;
                }
                // Start's hit target stays still. Title/subtitle letters each have independent smooth noise.
                if(text==startLabel)y=0;
                for(int corner=0;corner<4;corner++)
                {
                    meshInfo.vertices[v+corner]+=new Vector3(x,y,0);
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
        // Explicit mouse hit testing avoids changing the scene's existing EventSystem/action setup.
        bool over=Mouse.current!=null && RectTransformUtility.RectangleContainsScreenPoint(StartButtonRect,Mouse.current.position.ReadValue());
        hover=Mathf.MoveTowards(hover,over && IntroReady && !IsTransitioning?1:0,Time.unscaledDeltaTime*5);
        startButton.interactable=IntroReady && !IsTransitioning;
        if(inputArmed && IntroReady && !IsTransitioning && ((over && Mouse.current.leftButton.wasPressedThisFrame)
            || Keyboard.current?.enterKey.wasPressedThisFrame==true || Keyboard.current?.spaceKey.wasPressedThisFrame==true
            || Gamepad.current?.buttonSouth.wasPressedThisFrame==true))startButton.onClick.Invoke();
        // Opening a scene while a submit key/button is held must not skip its title screen.
        if(!inputArmed && Time.frameCount>openedFrame+1 && Mouse.current?.leftButton.isPressed!=true
            && Keyboard.current?.enterKey.isPressed!=true && Keyboard.current?.spaceKey.isPressed!=true
            && Gamepad.current?.buttonSouth.isPressed!=true)inputArmed=true;
        if(IsTransitioning)TransitionProgress=Mathf.Clamp01((Time.unscaledTime-startedAt)/Mathf.Max(.5f,transitionSeconds));
        menuMaterial.SetVector("_MenuSize",new Vector4(width,height,720f*width/height,720));
        menuMaterial.SetFloat("_GridPitch",BlackBoxPointCloudFeature.GetDotSpacing(view));
        menuMaterial.SetFloat("_DotRadius",BlackBoxPointCloudFeature.GetDotDiameter(view)*.5f);
        menuMaterial.SetFloat("_TitleDotScale",titleDotScale);menuMaterial.SetFloat("_TitleDensity",titlePointDensity);
        menuMaterial.SetFloat("_OpeningTime",OpeningElapsed);
        menuMaterial.SetFloat("_Transition",TransitionProgress);menuMaterial.SetFloat("_Hover",hover);
        if(TransitionProgress>=1 && !finishing){finishing=true;StartCoroutine(FinishAfterInputFrame());}
    }

    public void BeginGame()
    {
        if(!ownsPause || !IntroReady || IsTransitioning || HasStarted)return;
        IsTransitioning=true;startedAt=Time.unscaledTime;startButton.interactable=false;
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
