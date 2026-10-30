Shader "Hook Island/Soft Fish Shadow"
{
    Properties
    {
        _Color ("Underwater Tint", Color) = (0.025,0.19,0.23,0.64)
        _Profile ("Body Shape", Float) = 0
        _Opacity ("Visibility", Range(0,1)) = 1
        _Phase ("Confirmed Swim Phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float _Profile, _Opacity, _Phase;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o; o.position=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o;
            }
            float blurEllipse(float2 p, float2 center, float2 radius)
            {
                float2 q=(p-center)/radius;
                return exp(-2.8*dot(q,q));
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 p=i.uv*2-1;
                float rounded=step(.5,_Profile)*(1-step(1.5,_Profile));
                float longBody=step(1.5,_Profile);
                float tailWeight=1-smoothstep(-.65,.15,p.y);
                p.x-=sin(_Phase*4-p.y*4)*lerp(.065,.16,longBody)*tailWeight;
                float radiusX=lerp(lerp(.60,.76,rounded),.32,longBody);
                float body=blurEllipse(p,float2(0,.10),float2(radiusX,lerp(.65,.82,longBody)));
                float neck=blurEllipse(p,float2(0,-.38),float2(.18,.27));
                float tail=max(blurEllipse(p,float2(-.15,-.62),float2(.20,.22)),
                               blurEllipse(p,float2(.15,-.62),float2(.20,.22)));
                // Broad Gaussian lobes avoid sharp fins, eyes or species-specific markings.
                float silhouette=max(body,max(neck*.6,tail*lerp(.56,.20,longBody)));
                float border=smoothstep(0,.18,1-max(abs(i.uv.x*2-1),abs(i.uv.y*2-1)));
                return fixed4(_Color.rgb,_Color.a*_Opacity*silhouette*border);
            }
            ENDCG
        }
    }
}
