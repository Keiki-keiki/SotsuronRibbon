Shader "LibelSpiral/MatCapUnlit"
{
    Properties
    {
        _MatCap("MatCap", 2D) = "white" {}
        _Tint("Tint", Color) = (1,1,1,1)
        _Sweep("Sheen Sweep (flat faces)", Range(0,2)) = 0.6
        _Alpha("Alpha", Range(0,1)) = 1
        [Enum(Off,0,On,1)] _ZWrite("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MatCap; fixed4 _Tint; float _Sweep; float _Alpha;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                float3 pv = mul(UNITY_MATRIX_MV, float4(v.vertex.xyz,1)).xyz - UNITY_MATRIX_MV._m03_m13_m23;
                n = normalize(n + float3(pv.xy,0) * _Sweep);
                o.uv = n.xy * 0.5 + 0.5;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { fixed4 c = tex2D(_MatCap, i.uv) * _Tint; return fixed4(c.rgb, _Alpha); }
            ENDCG
        }
    }
}
