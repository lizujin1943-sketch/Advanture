Shader "Hook Island/Stylized Ocean"
{
    Properties
    {
        _ShoreDistance ("Shore Distance (RG16)", 2D) = "white" {}
        [HideInInspector] _ShoreBounds ("Shore Bounds (centre XZ, size XZ)", Vector) = (0,0,80,80)
        [HideInInspector] _PlatformShore ("Platform Foam, Reach, Spacing, Enabled", Vector) = (0,0,0,0)
        [HideInInspector] _PlatformShoreMotion ("Platform Ripple Speed, Foam Strength", Vector) = (0,0,0,0)
        _ShallowColor ("Shallow", Color) = (0.10,0.73,0.66,1)
        _DeepColor ("Deep", Color) = (0.025,0.40,0.47,1)
        _FoamColor ("Foam", Color) = (0.85,0.98,0.96,1)
        _RippleMap ("Small Waves (linear RG slopes, B height)", 2D) = "gray" {}
        _RippleScale ("Small Wave Tiling", Float) = 0.18
        _RippleStrength ("Small Wave Strength", Range(0,1)) = 0.26
        _ReflectionStrength ("Reflection Strength", Range(0,1)) = 0.48
        _SkyReflectionBlur ("Sky Reflection Softness (Radians)", Range(0,0.2)) = 0.08
        _CloudReflectionStrength ("Cloud Reflection Intensity", Range(0,2)) = 1.5
        _CloudReflectionCoverage ("Reflected Cloud Coverage", Range(0,1)) = 0.4
        _CloudReflectionHeight ("Reflected Cloud Layer Height", Range(4,60)) = 12
        _CloudReflectionWarp ("Cloud Reflection Wave Warp (Metres)", Range(0,1)) = 0.18
        _GlintStrength ("Sun Glints", Range(0,1)) = 0.35
        _SkyHorizonColor ("Sky Reflection Horizon", Color) = (0.57,0.82,0.82,1)
        _SkyZenithColor ("Sky Reflection Zenith", Color) = (0.19,0.48,0.63,1)
        [HideInInspector] _IslandSkyAvailable ("Directional Island Sky", Float) = 0
        [HideInInspector] _SkyMiddleColor ("Clear Blue", Color) = (0.61,0.81,0.85,1)
        [HideInInspector] _HorizonGlowColor ("Warm Horizon", Color) = (1,0.95,0.78,1)
        [HideInInspector] _CloudColor ("Cloud Light", Color) = (1,0.99,0.95,1)
        [HideInInspector] _CloudShadowColor ("Cloud Shade", Color) = (0.85,0.77,0.59,1)
        [HideInInspector] _CloudCoverage ("Cloud Coverage", Float) = 0.68
        [HideInInspector] _CloudSoftness ("Cloud Softness", Float) = 0.1
        [HideInInspector] _CloudScale ("Cloud Field Scale", Float) = 0.6
        [HideInInspector] _CloudSpeed ("Cloud Drift", Float) = 0.35
        [HideInInspector] _SunGlow ("Sun Glow", Float) = 0.3
        [HideInInspector] _OceanReflection ("Planar Reflection", 2D) = "black" {}
        [HideInInspector] _OceanReflectionValid ("Planar Reflection Available", Float) = 0
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
            #include "IslandSky.cginc"
            sampler2D _ShoreDistance,_RippleMap,_OceanReflection;
            float4 _ShallowColor,_DeepColor,_FoamColor;
            float4 _ShoreBounds,_PlatformShore,_PlatformShoreMotion;
            float _WaveHeight,_WaveSpeed,_FoamWidth,_ShallowDistance,_OceanTime;
            float _RippleScale,_RippleStrength,_ReflectionStrength,_GlintStrength,_OceanReflectionValid;
            float _IslandSkyAvailable;
            float _SkyReflectionBlur;
            float _CloudReflectionStrength, _CloudReflectionCoverage, _CloudReflectionHeight, _CloudReflectionWarp;
            float4x4 _OceanReflectionVP;
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
            float3 waterNormal(float2 p,float t)
            {
                float2 warp=float2(noise(p*.13),noise(p*.13+17.1))*.12;
                float2 uv=p*_RippleScale+warp;
                float2 a=tex2D(_RippleMap,uv+float2(t*.012,-t*.025)).rg*2-1;
                float2 rotated=float2(uv.x*.866+uv.y*.5,-uv.x*.5+uv.y*.866);
                float2 b=tex2D(_RippleMap,rotated*1.73+float2(-t*.018,t*.014)).rg*2-1;
                b=float2(b.x*.866-b.y*.5,b.x*.5+b.y*.866);
                float2 swell=float2(
                    .72*cos(p.x*.72+p.y*.36+t)*.6-.21*cos(p.y*1.1-p.x*.21-t*.72)*.4,
                    .36*cos(p.x*.72+p.y*.36+t)*.6+1.1*cos(p.y*1.1-p.x*.21-t*.72)*.4)*_WaveHeight;
                float2 slope=(a+b*.4)*_RippleStrength+swell;
                return normalize(float3(-slope.x,1,-slope.y));
            }
            float3 skyReflection(float3 direction,float2 p,float t)
            {
                float height=saturate(direction.y);
                float3 sky=lerp(_SkyHorizonColor.rgb,_SkyZenithColor.rgb,smoothstep(0,.9,height));
                float2 uv=direction.xz/(height+.4)*2.4+p*.035+float2(t*.0015,0);
                float cloud=noise(uv)*.7+noise(uv*2.1+9.3)*.3;
                cloud=smoothstep(.45,.74,cloud)*smoothstep(.05,.3,height);
                return lerp(sky,_FoamColor.rgb,cloud*.65);
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
                float2 drift=float2(t*.09,-t*.065);
                // Continuous depth-colour variation: no Voronoi cells or hard painted patches.
                float depthVariation=noise(p*.075+drift*.12)*.65+noise(p*.19-drift*.1)*.35;
                float3 openWater=lerp(_DeepColor.rgb,_ShallowColor.rgb,.18+depthVariation*.62);
                float3 color=lerp(_ShallowColor.rgb,openWater,blend);
                float3 normal=waterNormal(p,t);
                float3 viewDirection=normalize(lerp(UnityWorldSpaceViewDir(i.worldPos),UNITY_MATRIX_V[2].xyz,unity_OrthoParams.w));
                float3 reflectedDirection=reflect(-viewDirection,normal);
                float fresnel=.035+.965*pow(1-saturate(dot(normal,viewDirection)),4);
                float reflectionAmount=saturate((.22+fresnel*.78)*_ReflectionStrength);
                float3 reflection;
                if(_IslandSkyAvailable>.5)
                {
                    // A finite, world-anchored cloud layer keeps a few clouds visible
                    // from parallel overhead rays. Never project a perspective sky RT
                    // across an orthographic sea. The horizon retains directional sky.
                    // Keep fine ripples subdued; broader waves gently bend the cloud
                    // silhouette instead of breaking it into flickering bright flecks.
                    float3 calmNormal=normalize(float3(normal.x*.1,normal.y,normal.z*.1));
                    float3 cloudRay=reflect(-viewDirection,calmNormal);
                    float2 cloudPoint=p+cloudRay.xz*(_CloudReflectionHeight/max(cloudRay.y,.12));
                    float2 cloudSway=float2(sin(dot(p,float2(.58,.33))+t*.45),
                        sin(dot(p,float2(-.27,.64))-t*.37+1.7));
                    cloudPoint+=cloudSway*_CloudReflectionWarp;
                    float3 localCloudDirection=normalize(float3(cloudPoint.x,_CloudReflectionHeight,cloudPoint.y));
                    float3 cloudDirection=normalize(lerp(reflectedDirection,localCloudDirection,
                        smoothstep(.25,.65,cloudRay.y)));
                    reflection=IslandSampleSky(reflectedDirection,_Time.y,_SkyReflectionBlur,
                        cloudDirection,min(_CloudCoverage,_CloudReflectionCoverage),_CloudReflectionStrength);
                }
                else reflection=skyReflection(reflectedDirection,p,t);
                if(_OceanReflectionValid>.5)
                {
                    float4 projected=mul(_OceanReflectionVP,float4(i.worldPos,1));
                    float2 reflectionUV=projected.xy/projected.w*.5+.5;
                    reflectionUV+=normal.xz*.025;
                    float within=step(0,reflectionUV.x)*step(reflectionUV.x,1)*step(0,reflectionUV.y)*step(reflectionUV.y,1);
                    float4 objects=tex2D(_OceanReflection,saturate(reflectionUV));
                    reflection=lerp(reflection,objects.rgb,objects.a*within);
                }
                color*=1+(normal.x*.35+normal.z*.8)*.45;
                color=lerp(color,reflection,reflectionAmount);
                float3 lightDirection=normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDirection=normalize(lightDirection+viewDirection);
                float highlight=saturate(dot(normal,halfDirection));
                float sun=(pow(highlight,140)*.6+pow(highlight,28)*.09)*_GlintStrength;
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
                color*=lerp(.78,1,attenuation);
                color+=_LightColor0.rgb*sun*attenuation;
                float horizon=smoothstep(45,155,length(_WorldSpaceCameraPos.xz-p))*(1-unity_OrthoParams.w);
                color=lerp(color,_SkyHorizonColor.rgb,horizon);
                return fixed4(color,1);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
