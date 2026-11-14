Shader "Sound2/Elevator White Ink"
{
    Properties { _BaseColor("Paper", Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 cabin : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4x4 _PanelToCabin;
            float _CabinEdges;
            CBUFFER_END
            float4 _ElevatorPointGrid;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.cabin = mul(_PanelToCabin, input.positionOS).xyz;
                return output;
            }

            // On each shell face, an edge is where a second face meets it.
            float ShellEdge(float3 position, float3 low, float3 high, float3 pixelWidth)
            {
                float3 offset = max(low - position, position - high);
                if (any(offset > pixelWidth)) return 1e4;
                float3 d = min(abs(position - low), abs(position - high)) / pixelWidth;
                return max(min(d.x, d.y), min(max(d.x, d.y), d.z));
            }

            half4 frag(Varyings input):SV_Target
            {
                float pixel = _ElevatorPointGrid.z;
                float pitch = _ElevatorPointGrid.x;
                if (_CabinEdges < .5 || pitch <= 0 || pixel <= 0) return _BaseColor;

                float2 screen = input.positionCS.xy * pixel;
                float2 cell = floor(screen / pitch) + .5;
                float3 dx = ddx(input.cabin) / pixel;
                float3 dy = ddy(input.cabin) / pixel;
                float3 width = max((abs(dx) + abs(dy)) * (pitch * .48), .0001);
                float radius = _ElevatorPointGrid.y;
                float aa = .70710678 * pixel;
                float ink = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 delta = (cell + float2(x, y)) * pitch - screen;
                    float3 cabinPoint = input.cabin + dx * delta.x + dy * delta.y;
                    float inner = ShellEdge(cabinPoint, float3(-2, 0, -2.8), float3(2, 3.1, 2.725), width);
                    float outer = ShellEdge(cabinPoint, float3(-2.2, -.2, -3), float3(2.2, 3.3, 3), width);
                    // Inner doorway opening. The front and rear jamb faces share the same opening.
                    float3 door = abs(float3(abs(cabinPoint.x) - 1.3, cabinPoint.y - 2.7,
                        abs(cabinPoint.z - 2.85) - .125)) / width;
                    float jamb = cabinPoint.y <= 2.7 + width.y ? max(door.x, door.z) : 1e4;
                    float lintel = abs(cabinPoint.x) <= 1.3 + width.x ? max(door.y, door.z) : 1e4;
                    if (min(min(inner, outer), min(jamb, lintel)) <= 1)
                        ink = max(ink, 1 - smoothstep(radius - aa, radius + aa, length(delta)));
                }
                return half4(_BaseColor.rgb * (1 - ink), _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
