Shader "Hook Island/Winged Cthulhu"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _RoughnessMap ("Roughness", 2D) = "white" {}
        _NormalMap ("Normal", 2D) = "bump" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _NormalStrength ("Normal strength", Range(0,1)) = 1
        _RoughnessScale ("Roughness scale", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow
        #include "UnityCG.cginc"
        sampler2D _MainTex, _RoughnessMap, _NormalMap;
        fixed4 _Color;
        half4 _EmissionColor;
        half _NormalStrength, _RoughnessScale;
        struct Input { float2 uv_MainTex; };
        void surf(Input i, inout SurfaceOutputStandard o)
        {
            o.Albedo = tex2D(_MainTex, i.uv_MainTex).rgb * _Color.rgb;
            half3 normal = UnpackNormal(tex2D(_NormalMap, i.uv_MainTex));
            o.Normal = normalize(half3(normal.xy * _NormalStrength, normal.z));
            o.Smoothness = 1 - tex2D(_RoughnessMap, i.uv_MainTex).r * _RoughnessScale;
            o.Metallic = 0;
            o.Emission = _EmissionColor.rgb;
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
