Shader "Hook Island/Baked Color Surface"
{
    Properties
    {
        _MainTex ("Blender Albedo", 2D) = "white" {}
        _OcclusionMap ("Blender Occlusion", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OcclusionStrength ("Occlusion", Range(0,1)) = 0.6
        _Ambient ("Soft Fill", Range(0,1)) = 0.27
        _SunStrength ("Sun Response", Range(0,2)) = 0.98
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            sampler2D _MainTex, _OcclusionMap;
            float4 _MainTex_ST, _Color;
            float _Ambient, _SunStrength, _OcclusionStrength;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct v2f
            {
                float4 pos:SV_POSITION;
                float2 uv:TEXCOORD0;
                float3 normal:TEXCOORD1;
                float3 worldPos:TEXCOORD2;
                SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.worldPos=mul(unity_ObjectToWorld,v.vertex).xyz;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i, fixed facing:VFACE):SV_Target
            {
                float3 normal=normalize(i.normal)*(facing>=0?1:-1);
                float ao=lerp(1,tex2D(_OcclusionMap,i.uv).r,_OcclusionStrength);
                float ndl=saturate(dot(normal,normalize(_WorldSpaceLightPos0.xyz)));
                UNITY_LIGHT_ATTENUATION(attenuation,i,i.worldPos);
                float3 light=_Ambient*float3(.88,.95,1.0)*ao+
                             _LightColor0.rgb*ndl*attenuation*_SunStrength;
                fixed4 color=fixed4(tex2D(_MainTex,i.uv).rgb*_Color.rgb*light,1);
                UNITY_APPLY_FOG(i.fogCoord,color);
                return color;
            }
            ENDCG
        }
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
    Fallback "Diffuse"
}
