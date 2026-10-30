Shader "Hook Island/Distant Scenery"
{
    Properties
    {
        _HazeColor ("Atmospheric Haze", Color) = (0.86,0.72,0.71,1)
        _Haze ("Haze Start / End / Base / Height Fade", Vector) = (80,240,0.08,9)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            float4 _HazeColor,_Haze;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float4 color:COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal);
                // Mesh vertex colours are stored in linear space by the editor generator.
                o.color=v.color;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float diffuse=saturate(dot(normalize(i.normal),normalize(_WorldSpaceLightPos0.xyz)));
                float3 color=i.color.rgb*lerp(.68,1.08,diffuse);
                float distance=length(i.world.xz-_WorldSpaceCameraPos.xz);
                float haze=lerp(_Haze.z,.72,smoothstep(_Haze.x,_Haze.y,distance));
                haze+=.1*(1-smoothstep(0,_Haze.w,max(0,i.world.y)));
                return float4(lerp(color,_HazeColor.rgb,saturate(haze)),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
