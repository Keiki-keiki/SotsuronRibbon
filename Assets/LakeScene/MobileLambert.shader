Shader "LakeScene/MobileLambert"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150
        CGPROGRAM
        #pragma surface surf Lambert noforwardadd
        #pragma target 2.0
        fixed4 _Color;
        struct Input { float dummy; };
        void surf (Input IN, inout SurfaceOutput o) { o.Albedo = _Color.rgb; o.Alpha = 1; }
        ENDCG
    }
    Fallback "Mobile/VertexLit"
}