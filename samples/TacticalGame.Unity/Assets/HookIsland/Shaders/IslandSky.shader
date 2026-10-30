Shader "Hook Island/Cartoon Sky"
{
    Properties
    {
        _SkyHorizonColor ("Horizon", Color) = (0.86,0.72,0.71,1)
        _SkyZenithColor ("Zenith", Color) = (0.3,0.62,0.8,1)
        _SkyMiddleColor ("Clear Blue", Color) = (0.61,0.81,0.85,1)
        _HorizonGlowColor ("Warm Horizon Band", Color) = (1,0.95,0.78,1)
        _CloudColor ("Cloud Light", Color) = (1,0.99,0.95,1)
        _CloudShadowColor ("Cloud Shade", Color) = (0.85,0.77,0.59,1)
        _CloudCoverage ("Cloud Coverage", Range(0,1)) = 0.68
        _CloudSoftness ("Cloud Edge Softness", Range(0.025,0.25)) = 0.1
        _CloudScale ("Cloud Group Size Inverse", Range(0.2,1.5)) = 0.6
        _CloudSpeed ("Cloud Drift", Range(0,2)) = 0.35
        _SunGlow ("Sun Glow", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "IslandSky.cginc"
            struct v2f { float4 pos:SV_POSITION; float3 direction:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o;
                o.pos=UnityObjectToClipPos(vertex);
                o.direction=vertex.xyz;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                return float4(IslandSampleSky(i.direction,_Time.y,0),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
