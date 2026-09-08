Shader "Yanagisawa/Deadline Run"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.1, 0.5, 0.9, 1.0)
        _EmissionColor ("Emission", Color) = (0.1, 0.7, 1.0, 1.0)
        _Pulse ("Pulse", Float) = 0.5
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite ("Z Write", Float) = 1
        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _ZTest ("Z Test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
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
            #pragma multi_compile_local __ DR_WET
            #pragma multi_compile_local __ DR_EMISSION
            #pragma multi_compile_local __ DR_GRID
            #pragma multi_compile_local __ DR_DECAL
            #pragma multi_compile_local __ DR_GLASS
            #pragma multi_compile_local __ DR_SHIELD
            #pragma multi_compile_local __ DR_RAIN
            #pragma multi_compile_local __ DR_HOLOGRAM
            #pragma multi_compile_local __ DR_DAMAGE
            #pragma multi_compile_local __ DR_DISTORT
            #include "UnityCG.cginc"
            #define DEADLINE_RUN_CACHE_BUSTER 0u

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 screenPosition : TEXCOORD3;
            };

            float4 _BaseColor;
            float4 _EmissionColor;
            float _Pulse;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 position = input.positionOS.xyz;
                #if defined(DR_DISTORT)
                    float wave = sin(position.y * 5.3 + position.x * 3.7 + _Time.y * 2.1);
                    position += input.normalOS * wave * 0.018;
                #endif
                float4 positionWS = mul(unity_ObjectToWorld, float4(position, 1.0));
                output.positionWS = positionWS.xyz;
                output.normalWS = UnityObjectToWorldNormal(input.normalOS);
                output.positionCS = mul(UNITY_MATRIX_VP, positionWS);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                output.uv = input.uv;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 lightDirection = normalize(float3(-0.35, 0.78, -0.42));
                float lambert = saturate(dot(normal, lightDirection)) * 0.72 + 0.28;
                float4 color = _BaseColor;
                color.rgb *= lambert;

                #if defined(DR_WET)
                    float wetFresnel = pow(1.0 - saturate(dot(normal, viewDirection)), 4.0);
                    float wetSpecular = pow(saturate(dot(reflect(-lightDirection, normal), viewDirection)), 42.0);
                    color.rgb = lerp(color.rgb * 0.58, color.rgb, 0.72);
                    color.rgb += wetFresnel * float3(0.08, 0.20, 0.34) + wetSpecular * 0.85;
                #endif
                #if defined(DR_EMISSION)
                    float pulse = 0.68 + 0.32 * sin(_Time.y * (2.0 + _Pulse) + input.positionWS.z * 0.12);
                    color.rgb += _EmissionColor.rgb * pulse;
                #endif
                #if defined(DR_GRID)
                    float2 gridUv = abs(frac(input.uv * 12.0) - 0.5);
                    float grid = 1.0 - smoothstep(0.035, 0.095, min(gridUv.x, gridUv.y));
                    color.rgb += grid * _EmissionColor.rgb * 0.62;
                #endif
                #if defined(DR_DECAL)
                    float ring = abs(length(input.uv * 2.0 - 1.0) - 0.62);
                    float decal = 1.0 - smoothstep(0.035, 0.10, ring);
                    color.rgb = lerp(color.rgb * 0.35, _EmissionColor.rgb * 1.4, decal);
                    color.a *= saturate(decal + 0.18);
                #endif
                #if defined(DR_GLASS)
                    float glassFresnel = pow(1.0 - saturate(dot(normal, viewDirection)), 3.0);
                    color.rgb = lerp(color.rgb * 0.38, _EmissionColor.rgb, glassFresnel * 0.58);
                    color.a *= 0.42 + glassFresnel * 0.38;
                #endif
                #if defined(DR_SHIELD)
                    float shieldFresnel = pow(1.0 - saturate(dot(normal, viewDirection)), 2.2);
                    float shieldBand = 0.65 + 0.35 * sin(
                        input.positionWS.y * 8.0 - _Time.y * 5.0 + input.positionWS.x * 2.0);
                    color.rgb += _EmissionColor.rgb * shieldFresnel * shieldBand * 1.6;
                    color.a *= saturate(shieldFresnel + 0.16);
                #endif
                #if defined(DR_RAIN)
                    float streak = 1.0 - smoothstep(0.03, 0.20, abs(input.uv.x - 0.5));
                    float rainPulse = 0.62 + 0.38 * sin(_Time.y * 16.0 + input.positionWS.z);
                    color.rgb = _EmissionColor.rgb * rainPulse;
                    color.a *= streak * 0.68;
                #endif
                #if defined(DR_HOLOGRAM)
                    float scan = 0.42 + 0.58 * step(0.48, frac(input.positionWS.y * 14.0 - _Time.y * 5.5));
                    float breakup = step(0.20, Hash21(floor(input.screenPosition.xy * 0.035)));
                    color.rgb = lerp(color.rgb, _EmissionColor.rgb, 0.68) * scan;
                    color.a *= breakup * 0.82;
                #endif
                #if defined(DR_DAMAGE)
                    float damage = 0.0;
                    [unroll]
                    for (int octave = 1; octave <= 12; octave++)
                    {
                        float phase = dot(input.uv, float2(13.7, 19.1)) * octave;
                        damage += sin(phase + octave * 0.713 + _Time.y * 0.2) / octave;
                    }
                    float heat = saturate(abs(damage) * 0.72);
                    color.rgb = lerp(color.rgb, float3(1.35, 0.12, 0.02), heat);
                #endif

                // The builder replaces this value for every acceptance invocation.
                // It changes real shader bytecode while remaining visually sub-pixel.
                color.r += (float)(DEADLINE_RUN_CACHE_BUSTER & 65535u) / 4294967296.0;
                color.g += (float)((DEADLINE_RUN_CACHE_BUSTER >> 16) & 65535u) /
                           4294967296.0;
                return color;
            }
            ENDHLSL
        }
    }
}
