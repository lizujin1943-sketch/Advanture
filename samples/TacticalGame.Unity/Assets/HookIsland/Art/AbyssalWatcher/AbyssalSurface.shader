Shader "Hook Island/Abyssal Surface"
{
    Properties
    {
        _MainTex ("Skin Albedo", 2D) = "white" {}
        _RoughnessMap ("Roughness", 2D) = "white" {}
        _NormalMap ("Surface Normal", 2D) = "bump" {}
        _EmissionMap ("Eye / Fissure Emission", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _NormalStrength ("Normal Strength", Range(0,1)) = 0.24
        _EmissionStrength ("Emission Strength", Range(0,4)) = 0
        _TissueWeight ("Use Mouth Tissue Weights", Range(0,1)) = 0
        _Specular ("Wet Specular", Range(0,0.2)) = 0.028
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf StandardSpecular fullforwardshadows vertex:vert addshadow
        #include "UnityCG.cginc"
        sampler2D _MainTex, _RoughnessMap, _NormalMap, _EmissionMap;
        fixed4 _Color;
        half _NormalStrength, _EmissionStrength, _TissueWeight, _Specular;
        struct Input
        {
            float2 uv_MainTex;
            float4 tissue;
        };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            // 这是制作网格中的连续组织混合权重，不是需要 gamma 转换的显示颜色。
            o.tissue = v.color;
        }
        void surf(Input i, inout SurfaceOutputStandardSpecular o)
        {
            half3 color = tex2D(_MainTex, i.uv_MainTex).rgb * _Color.rgb;
            half flesh = saturate(i.tissue.r * _TissueWeight);
            half throat = saturate(i.tissue.g * _TissueWeight);
            color = lerp(color, half3(.075, .062, .055), flesh);
            color = lerp(color, half3(.002, .005, .004), throat);
            o.Albedo = color;
            half3 normal = UnpackNormal(tex2D(_NormalMap, i.uv_MainTex));
            o.Normal = normalize(half3(normal.xy * _NormalStrength, normal.z));
            o.Smoothness = 1 - tex2D(_RoughnessMap, i.uv_MainTex).r;
            o.Specular = _Specular * (1 - throat);
            o.Emission = tex2D(_EmissionMap, i.uv_MainTex).rgb * _EmissionStrength;
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
