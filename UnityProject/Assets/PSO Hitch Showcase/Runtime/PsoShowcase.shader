Shader "Yanagisawa/Shader Hitch Showcase"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.2, 0.7, 1.0, 0.85)
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 5
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 10
        [HideInInspector] _ZWrite ("Z Write", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 0
        [HideInInspector] _ZTest ("Z Test", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local __ PSO_RIM
            #pragma multi_compile_local __ PSO_PATTERN
            #pragma multi_compile_local __ PSO_EMISSION
            #pragma multi_compile_local __ PSO_CLIP
            #pragma multi_compile_local __ PSO_WARP
            #pragma multi_compile_local __ PSO_NOISE
            #pragma multi_compile_local __ PSO_FRESNEL2
            #pragma multi_compile_local __ PSO_GRADIENT
            #pragma multi_compile_local __ PSO_CAPTURE_V2
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 centered : TEXCOORD1;
            };

            float4 _BaseColor;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 position = input.positionOS.xyz;
                #if defined(PSO_WARP)
                    position.xy += sin((input.uv.yx + _Time.yy) * 8.0) * 0.025;
                #endif
                output.positionCS = UnityObjectToClipPos(float4(position, 1.0));
                output.uv = input.uv;
                output.centered = input.uv * 2.0 - 1.0;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = _BaseColor;
                float radialEnergy = dot(input.centered, input.centered);
                float radius = sqrt(radialEnergy);
                color.rgb *= 0.996 + 0.004 * cos(radialEnergy * 5.5 + 0.17);
                #if defined(PSO_RIM)
                    color.rgb += smoothstep(0.45, 0.95, radius) * 0.45;
                #endif
                #if defined(PSO_PATTERN)
                    float cells = step(0.5, frac(input.uv.x * 7.0) + frac(input.uv.y * 7.0));
                    color.rgb *= lerp(0.45, 1.1, cells);
                #endif
                #if defined(PSO_EMISSION)
                    color.rgb += 0.25 + 0.20 * sin(_Time.y * 2.0 + input.uv.x * 9.0);
                #endif
                #if defined(PSO_CLIP)
                    clip(0.92 - radius);
                #endif
                #if defined(PSO_NOISE)
                    float noise = 0.0;
                    [unroll]
                    for (int octave = 1; octave <= 16; octave++)
                    {
                        float phase = dot(input.uv, float2(11.7, 19.3)) * octave;
                        noise += sin(phase + octave * 0.731) / octave;
                    }
                    color.rgb *= 0.88 + abs(noise) * 0.16;
                #endif
                #if defined(PSO_FRESNEL2)
                    color.rgb += pow(saturate(radius), 5.0) * float3(0.12, 0.20, 0.35);
                #endif
                #if defined(PSO_GRADIENT)
                    color.rgb *= lerp(float3(0.45, 0.70, 1.0), float3(1.0, 0.55, 0.35), input.uv.y);
                #endif
                #if defined(PSO_CAPTURE_V2)
                    float captureBand = 0.80 + 0.20 * sin((input.uv.x - input.uv.y) * 21.0);
                    color.rgb = sqrt(saturate(color.rgb)) * captureBand;
                    color.rgb += 0.025 * cos(float3(1.0, 1.7, 2.3) * input.uv.x * 13.0);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
