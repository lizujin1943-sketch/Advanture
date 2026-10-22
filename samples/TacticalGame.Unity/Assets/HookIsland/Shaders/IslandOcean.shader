Shader "Hook Island/Stylized Ocean"
{
    Properties
    {
        _ShoreDistance ("Shore Distance (RG16)", 2D) = "white" {}
        [HideInInspector] _ShoreBounds ("Shore Bounds (centre XZ, size XZ)", Vector) = (0,0,80,80)
        [HideInInspector] _PlatformShore ("Platform Foam, Reach, Spacing, Enabled", Vector) = (0,0,0,0)
        [HideInInspector] _PlatformShoreMotion ("Platform Ripple Speed, Foam Strength", Vector) = (0,0,0,0)
        _ShallowColor ("Shallow", Color) = (0.12,0.76,0.74,1)
        _DeepColor ("Deep", Color) = (0.035,0.38,0.56,1)
        _FoamColor ("Foam", Color) = (0.83,1,0.96,1)
        _WaveHeight ("Wave Height", Float) = 0.035
        _WaveSpeed ("Wave Speed", Float) = 0.75
        _FoamWidth ("Foam Width", Float) = 0.85
        _ShallowDistance ("Shallow Distance", Float) = 6
        [HideInInspector] _OceanTime ("Time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+20" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            sampler2D _ShoreDistance;
            float4 _ShallowColor,_DeepColor,_FoamColor;
            float4 _ShoreBounds,_PlatformShore,_PlatformShoreMotion;
            float _WaveHeight,_WaveSpeed,_FoamWidth,_ShallowDistance,_OceanTime;
            struct appdata { float4 vertex:POSITION; };
            struct v2f
            {
                float4 pos:SV_POSITION;
                float3 worldPos:TEXCOORD0;
                SHADOW_COORDS(1)
            };
            float hash(float2 p)
            {
                return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);
            }
            float noise(float2 p)
            {
                float2 c=floor(p),f=frac(p);
                f=f*f*(3-2*f);
                return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),
                            lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);
            }
            float waterFacet(float2 p)
            {
                float2 cell=floor(p),f=frac(p);
                float nearest=8,shade=0;
                [unroll] for(int y=-1;y<=1;y++)
                [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 offset=float2(x,y);
                    float2 seed=cell+offset;
                    float2 random=float2(hash(seed),hash(seed+19.37));
                    float2 delta=offset+.18+random*.64-f;
                    float d=dot(delta,delta);
                    if(d<nearest) { nearest=d;shade=hash(seed+51.7); }
                }
                return shade;
            }
            float distanceToShore(float2 p)
            {
                float2 rg=tex2D(_ShoreDistance,(p-_ShoreBounds.xy)/_ShoreBounds.zw+.5).rg;
                return (rg.x*65280+rg.y*255)/65535*32-16;
            }
            v2f vert(appdata v)
            {
                v2f o;
                float3 p=mul(unity_ObjectToWorld,v.vertex).xyz;
                float t=_OceanTime*_WaveSpeed;
                float wave=(sin(p.x*.72+p.z*.36+t)*.6+
                            sin(p.z*1.1-p.x*.21-t*.72)*.4)*_WaveHeight;
                v.vertex.y+=wave;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.worldPos=mul(unity_ObjectToWorld,v.vertex).xyz;
                TRANSFER_SHADOW(o);
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 p=i.worldPos.xz;
                float t=_OceanTime*_WaveSpeed;
                float shore=distanceToShore(p);
                // Land masks clip waves; floating decks keep the water beneath them.
                clip(lerp(shore+.08,1,_PlatformShore.w));
                float blend=smoothstep(0,_ShallowDistance,shore);
                blend=lerp(blend,1,_PlatformShore.w);
                float3 color=lerp(_ShallowColor.rgb,_DeepColor.rgb,blend);
                float2 drift=float2(t*.09,-t*.065);
                float facets=waterFacet((p+drift)*.95);
                color*=.93+facets*.14;
                float ripples=sin(p.x*1.1+p.y*.75+t*.85+
                                  noise(p*.45+drift)*5)*.5+.5;
                color+=float3(.003,.012,.013)*pow(ripples,20);
                float irregular=noise(p*2.6+drift)*.27;
                float wash=sin(shore*8-t*1.7+noise(p*.7)*3)*.5+.5;
                float band=1-smoothstep(_FoamWidth*.15,_FoamWidth+irregular,shore);
                float broken=smoothstep(.24,.68,noise(p*4.8+drift*2));
                float foam=band*(.28+.72*wash)*broken;
                float edge=(1-smoothstep(.03,.19+irregular*.4,shore))*.65;
                if(_PlatformShore.w>.5)
                {
                    // Broken contact foam washes against the deck while narrow crests
                    // travel outwards. World-space noise keeps corners and joins continuous.
                    float slosh=sin(t*1.5+noise(p*.9)*5)*.08;
                    float contact=1-smoothstep(.015,.13+irregular*.35,shore);
                    float washBand=1-smoothstep(.08,_PlatformShore.x+irregular*.4+slosh,shore);
                    float flecks=smoothstep(.25,.6,noise(p*6+drift*2.5));
                    float edgeBreaks=.35+.65*smoothstep(.2,.7,noise(p*8+drift*3));
                    float d=shore+(noise(p*2.2+drift*.5)-.5)*.18;
                    float phase=frac((d-_OceanTime*_PlatformShoreMotion.x)/_PlatformShore.z);
                    float crest=1-smoothstep(.08,.2,abs(phase-.5));
                    float fade=smoothstep(.2,.45,shore)*(1-smoothstep(.4,_PlatformShore.y,shore));
                    float breaks=smoothstep(.3,.65,noise(p*2.8-drift));
                    float foamAmount=(contact*.42*edgeBreaks+washBand*flecks*.85)*_PlatformShoreMotion.y;
                    foamAmount+=crest*fade*breaks*.24;
                    foamAmount=saturate(foamAmount)*smoothstep(-.03,.06,shore);
                    color=lerp(color,_FoamColor.rgb,foamAmount);
                }
                else color=lerp(color,_FoamColor.rgb,saturate(foam+edge)*.9);
                UNITY_LIGHT_ATTENUATION(attenuation,i,i.worldPos);
                color*=lerp(.72,1,attenuation);
                return fixed4(color,1);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
