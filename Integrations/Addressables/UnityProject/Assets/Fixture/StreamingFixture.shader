Shader "ShaderHitchPipeline/StreamingFixture"
{
    Properties { _Tint("Tint", Color) = (0.1,0.4,0.8,1) }
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Tint;
            struct Input { float4 vertex : POSITION; };
            struct Output { float4 vertex : SV_POSITION; };
            Output vert(Input v) { Output o; o.vertex = UnityObjectToClipPos(v.vertex); return o; }
            float4 frag(Output v) : SV_Target { return _Tint; }
            ENDHLSL
        }
    }
}
