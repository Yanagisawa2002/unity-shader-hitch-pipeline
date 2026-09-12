Shader "PsoHotsetFixture/Route"
{
    Properties { _Tint ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local __ HOTSET_A
            #pragma multi_compile_local __ HOTSET_B
            #pragma multi_compile_local __ HOTSET_C
            #pragma multi_compile_local __ HOTSET_D
            #include "UnityCG.cginc"
            float4 _Tint;
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(float4 pos:POSITION, float2 uv:TEXCOORD0) { V o; o.pos=UnityObjectToClipPos(pos); o.uv=uv; return o; }
            float4 Frag(V v):SV_Target
            {
                float3 c = _Tint.rgb;
                #if HOTSET_A
                c *= 0.5 + 0.5 * sin(v.uv.x * 20);
                #endif
                #if HOTSET_B
                c += float3(v.uv, 0) * 0.3;
                #endif
                #if HOTSET_C
                c *= 0.7 + 0.3 * cos(v.uv.y * 30);
                #endif
                #if HOTSET_D
                c = lerp(c, c.brg, v.uv.x);
                #endif
                return float4(c,1);
            }
            ENDHLSL
        }
    }
}
