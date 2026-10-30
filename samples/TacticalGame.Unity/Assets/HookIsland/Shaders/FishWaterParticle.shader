Shader "Hook Island/Fish Water Particle"
{
    Properties { _Style ("0 Drop / 1 Bubble / 2 Ripple / 3 Crown / 4 Contact", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Style;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o; o.position=UnityObjectToClipPos(v.vertex); o.color=v.color;
                o.uv=v.uv; o.normal=UnityObjectToWorldNormal(v.normal); return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float alpha=1; float3 color=i.color.rgb;
                if (_Style<.5)
                {
                    float2 p=i.uv*2-1;
                    p.x*=1+.18*p.y;
                    float radiusSquared=dot(p,p);
                    float glint=exp(-22*dot(p-float2(-.25,.3),p-float2(-.25,.3)));
                    alpha=(1-smoothstep(.12,1,radiusSquared))*.8;
                    color=lerp(color,float3(1,1,1),.22+glint*.35);
                }
                else if (_Style<2.5)
                {
                    float2 p=i.uv*2-1; float r=length(p); float angle=atan2(p.y,p.x);
                    if (_Style<1.5)
                    {
                        float ring=(1-smoothstep(.68,.93,r))*smoothstep(.47,.69,r);
                        float glint=exp(-70*dot(p-float2(-.32,.42),p-float2(-.32,.42)));
                        alpha=max(ring*.8,glint);
                    }
                    else
                    {
                        float distanceToWave=abs(r-.78);
                        float ring=(1-smoothstep(.012,.038,distanceToWave))*.85
                            +(1-smoothstep(.035,.075,distanceToWave))*.15;
                        alpha=ring*.8;
                    }
                }
                else if (_Style<3.5)
                {
                    float lip=.76+sin(i.uv.x*31.416+.5)*.07;
                    float sheet=smoothstep(.02,.22,i.uv.y)*(1-smoothstep(lip-.18,1,i.uv.y));
                    float variation=.68+.32*sin(i.uv.x*18.85+i.uv.y*2)*sin(i.uv.x*18.85+i.uv.y*2);
                    alpha=sheet*variation*.4;
                    color=lerp(color,float3(1,1,1),.16);
                }
                else
                {
                    float2 p=i.uv*2-1; float angle=atan2(p.y,p.x);
                    float distanceToWave=abs(length(p)-.78+sin(angle*7)*.012);
                    float sides=.2+.8*smoothstep(-.25,.75,cos(angle*2));
                    alpha=(1-smoothstep(.015,.07,distanceToWave))*sides*(.75+.25*sin(angle*5+.4));
                }
                return fixed4(color,i.color.a*alpha);
            }
            ENDCG
        }
    }
}
