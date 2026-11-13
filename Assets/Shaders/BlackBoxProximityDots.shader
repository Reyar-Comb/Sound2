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
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Unlit"
        }

        Pass
        {
            Name "ProximityDots"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _RevealDistance;
                float _WorldCircleRadius;
                float _ScreenDotSpacing;
                float _ScreenDotRadius;
                float4 _DistanceBands;
                float4 _DensityLevels;
                float _WorldCoreRadius;
                float4 _CoreDensityLevels;
                float4 _WorldRadialBandEnds;
                float4 _RadialDensityLevels;
            CBUFFER_END

            // Written by the scene's player probe; deliberately not material properties.
            float4 _BlackBoxPlayerPosition;
            float _BlackBoxPlayerActive;
            float4 _BlackBoxGazeHit;
            float4 _BlackBoxGazeNormal;
            float _BlackBoxCenterMode;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normal = normalize(input.normalWS);
                float3 toPlayer = _BlackBoxPlayerPosition.xyz - input.positionWS;
                float signedDistance = dot(toPlayer, normal);
                float wallDistance = abs(signedDistance);
                float pointDistance = length(toPlayer);

                // Sample the actual distance to EACH wall location, not one shared
                // perpendicular distance for the entire wall. Density remains stepped.
                float4 bands;
                bands.x = max(0.0, _DistanceBands.x);
                bands.y = max(bands.x, _DistanceBands.y);
                bands.z = max(bands.y, _DistanceBands.z);
                bands.w = max(bands.z, _DistanceBands.w);
                float distanceDensity = pointDistance < bands.x ? _DensityLevels.x
                    : pointDistance < bands.y ? _DensityLevels.y
                    : pointDistance < bands.z ? _DensityLevels.z
                    : pointDistance < bands.w ? _DensityLevels.w : 0.0;
                float coreDensity = pointDistance < bands.x ? _CoreDensityLevels.x
                    : pointDistance < bands.y ? _CoreDensityLevels.y
                    : pointDistance < bands.z ? _CoreDensityLevels.z
                    : pointDistance < bands.w ? _CoreDensityLevels.w : 0.0;

                // The influence field belongs to the wall in world space, so its
                // projected bands compress naturally on the far side of an oblique wall.
                float3 wallCenter = _BlackBoxPlayerPosition.xyz - normal * signedDistance;
                float3 center = wallCenter;
                float selectedFace = 1.0;
                // Gaze/midpoint target the first obstacle's face, not unrelated walls.
                // A ray miss falls back to the original per-wall projection.
                if (_BlackBoxCenterMode != 1.0 && _BlackBoxGazeHit.w > 0.5)
                {
                    float planeError = abs(dot(_BlackBoxGazeHit.xyz - input.positionWS, normal));
                    selectedFace = abs(dot(normal, _BlackBoxGazeNormal.xyz)) > 0.99 && planeError < 0.03 ? 1.0 : 0.0;
                    float gazeWeight = _BlackBoxCenterMode < 0.5 ? 1.0 : 0.5;
                    center = lerp(wallCenter, _BlackBoxGazeHit.xyz, gazeWeight);
                }
                float circleDistance = length(input.positionWS - center);
                // Only the individual dot shapes and square grid use screen space.
                float2 screenScale = _ScaledScreenParams.xy * (720.0 / max(_ScaledScreenParams.y, 1.0));
                float2 screenPosition = input.screenPosition.xy / input.screenPosition.w * screenScale;

                // At the trigger distance the radius is exactly zero. Moving closer
                // expands every ring continuously, independently of the density steps.
                float revealLimit = min(max(_RevealDistance, 0.0), bands.w);
                float radiusScale = saturate(1.0 - wallDistance / max(revealLimit, 0.0001));
                float circleRadius = max(_WorldCircleRadius, 0.0) * radiusScale;
                float coreRadius = clamp(_WorldCoreRadius * radiusScale, 0.0, circleRadius);
                float4 rings;
                rings.x = max(coreRadius, _WorldRadialBandEnds.x * radiusScale);
                rings.y = max(rings.x, _WorldRadialBandEnds.y * radiusScale);
                rings.z = max(rings.y, _WorldRadialBandEnds.z * radiusScale);
                rings.w = max(rings.z, _WorldRadialBandEnds.w * radiusScale);
                float radialDensity = circleDistance < rings.x ? _RadialDensityLevels.x
                    : circleDistance < rings.y ? _RadialDensityLevels.y
                    : circleDistance < rings.z ? _RadialDensityLevels.z
                    : circleDistance < rings.w ? _RadialDensityLevels.w : 0.0;
                // The core has its own high density at every wall-distance tier.
                // It is made from overlapping dots, not a painted white disk.
                float density = saturate(circleDistance < coreRadius
                    ? coreDensity : radialDensity * distanceDensity);

                // A square lattice contains one point per spacing-squared area.
                // Retain the same density ratios by scaling BOTH axes by 1/sqrt(density).
                // No jitter, hashing or random removal: every zone is an evenly spaced grid.
                float baseSpacing = max(_ScreenDotSpacing, 1.0);
                float spacing = baseSpacing / sqrt(max(density, 0.000001));
                float2 toNearestPoint = (frac(screenPosition / spacing + 0.5) - 0.5) * spacing;
                // Point size remains fixed; only the square lattice pitch changes.
                float dotRadius = clamp(_ScreenDotRadius, 0.0, baseSpacing * 0.75);
                // Pixel-space AA is analytic: derivatives in a discarded neighbouring
                // fragment must never change a surviving dot at the expanding edge.
                float referencePixelSize = 720.0 / max(_ScaledScreenParams.y, 1.0);
                float dotAA = 0.70710678 * referencePixelSize;
                float circleAA = max(fwidth(circleDistance), 0.00001);
                // Inward-only coverage makes even a subpixel circle vanish cleanly
                // at the trigger instead of retaining a fixed-width outer halo.
                float circleMask = saturate((circleRadius - circleDistance) / circleAA);
                if (_BlackBoxPlayerActive < 0.5 || selectedFace < 0.5 || pointDistance >= revealLimit || radiusScale <= 0.0
                    || circleMask <= 0.0 || density <= 0.0 || dotRadius <= 0.0)
                    return half4(0, 0, 0, 1);

                // For equal-radius points on a regular lattice, the closest point
                // already gives the union of all circles, including the dense core.
                float dotDistance = length(toNearestPoint);
                float dotMask = 1.0 - smoothstep(dotRadius - dotAA, dotRadius + dotAA, dotDistance);
                float white = dotMask * circleMask;

                // No lighting, fog, transparency, or distance fade: everything else is black.
                return half4(white, white, white, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
