Shader "Sound2/Black Box Proximity Dots"
{
    Properties
    {
        _RevealDistance ("Reveal Distance (metres)", Float) = 3
        _WorldCircleRadius ("Maximum Wall Circle Radius (metres)", Float) = 1.2
        _ScreenDotSpacing ("Densest Square Grid Spacing (pixels at 720p)", Float) = 5.5
        _ScreenDotRadius ("Dot Radius (pixels at 720p)", Float) = 4.125
        _DistanceBands ("Band Ends: Near / Medium / Far / Limit (metres)", Vector) = (0.75, 1.5, 2.25, 3)
        _DensityLevels ("Ring Density Multipliers: Near / Medium / Far / Farthest", Vector) = (1, 0.75, 0.5, 0.3)
        _WorldCoreRadius ("Maximum Wall Core Radius (metres)", Float) = 0.28
        _CoreDensityLevels ("Core Density: Near / Medium / Far / Farthest", Vector) = (1, 0.99, 0.98, 0.97)
        _WorldRadialBandEnds ("Maximum Wall Ring Outer Radii (metres)", Vector) = (0.52, 0.78, 1, 1.2)
        _RadialDensityLevels ("Ring Density: Inner / Middle / Outer / Edge", Vector) = (0.55, 0.22, 0.075, 0.02)
        [Toggle] _FaceEnabled ("Face Points", Float) = 1
        _FaceDensityScale ("Overall Face Density", Range(0,1)) = 0.03
        [Toggle] _EdgeEnabled ("Corner Points", Float) = 1
        _EdgeRevealDistance ("Corner Reveal Distance (metres)", Float) = 3
        _EdgeWidth ("Corner Detection Width (pixels at 720p)", Float) = 7
        _EdgeAngle ("Corner Angle Threshold (degrees)", Range(5,85)) = 35
        _EdgeDensityLevels ("Corner Density: Near / Medium / Far / Farthest", Vector) = (1, 0.65, 0.35, 0.15)
        [Toggle] _TwinkleEnabled ("Gentle Point Twinkle", Float) = 1
        _TwinkleFraction ("Fraction of Points Twinkling at Once", Range(0,0.05)) = 0.005
        _TwinkleDuration ("Point Fade Out and In Duration (seconds)", Float) = 6
        [Toggle] _StarsEnabled ("Sparse Ambient Stars", Float) = 1
        _StarChance ("Star Chance per World Direction", Range(0,1)) = 0.2
        _StarDuration ("Star Fade In and Out Duration (seconds)", Float) = 10
        _StarBrightness ("Star Brightness", Range(0,1)) = 0.65
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 Frag() : SV_Target { return half4(0,0,0,1); }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
        UsePass "Universal Render Pipeline/Unlit/DepthNormalsOnly"
    }
}
