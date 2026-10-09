Shader "Skybox/LakeGradient"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.008,0.012,0.05,1)
        _MidColor ("Mid", Color) = (0.03,0.04,0.12,1)
        _HorizonColor ("Horizon", Color) = (0.07,0.09,0.22,1)
        _MidHeight ("Mid Height", Range(0.02,0.6)) = 0.2
        _TopHeight ("Top Height", Range(0.3,1)) = 0.75
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _TopColor, _MidColor, _HorizonColor;
            float _MidHeight, _TopHeight;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float h = saturate(normalize(i.dir).y);
                fixed3 c = h < _MidHeight
                    ? lerp(_HorizonColor.rgb, _MidColor.rgb, h / _MidHeight)
                    : lerp(_MidColor.rgb, _TopColor.rgb, saturate((h - _MidHeight) / (_TopHeight - _MidHeight)));
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}