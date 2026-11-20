Shader "Advanture/Fortified Compound"
{
    Properties
    {
        _Dark ("Dark mineral", Color) = (.45,.44,.4,1)
        _Light ("Light mineral", Color) = (.7,.68,.62,1)
        _Paint ("Paint", Color) = (.4,.48,.43,1)
        _Scan ("Albedo, roughness, height", 2D) = "gray" {}
        _HasScan ("Has scan", Float) = 0
        _Meters ("Tile width (metres)", Float) = 3
        _Range ("Albedo range", Vector) = (0,1,0,0)
        _Band ("Lower painted course", Float) = -1
        _Plaza ("Slab paving", Float) = 0
        _Roughness ("Roughness", Range(0,1)) = .8
        _Metallic ("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #include "UnityCG.cginc"
        struct Input { float3 worldPos; float3 projectionNormal; float paintHeight; };
        sampler2D _Scan;
        fixed4 _Dark, _Light, _Paint;
        float4 _Range;
        half _Meters, _Band, _Plaza, _HasScan, _Roughness, _Metallic;
        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input,data);
            data.projectionNormal = UnityObjectToWorldNormal(v.normal);
            data.paintHeight = v.texcoord.x;
        }
        void surf(Input input, inout SurfaceOutputStandard o)
        {
            float3 pos = input.worldPos / _Meters;
            half3 blend = pow(abs(normalize(input.projectionNormal)),4);
            blend /= max(.001, dot(blend,half3(1,1,1)));
            half3 scan = tex2D(_Scan,pos.zy).rgb * blend.x + tex2D(_Scan,pos.xz).rgb * blend.y + tex2D(_Scan,pos.xy).rgb * blend.z;
            half gray = GammaToLinearSpaceExact(scan.r);
            half tone = lerp(.55, .07+.89*saturate((gray-_Range.x)/max(.001,_Range.y-_Range.x)), _HasScan);
            half3 base = lerp(_Dark.rgb,_Light.rgb,tone);
            if (_Band >= 0)
            {
                half paint = 1-smoothstep(_Band-.008,_Band+.008,input.paintHeight+scan.b*.013);
                base = lerp(base, _Paint.rgb, paint*(.64+.18*scan.b));
            }
            if (_Plaza > .5)
            {
                float2 plaza = input.worldPos.xz - float2(-5.25,7.4);
                float2 slabSize = float2(5.25,4.475);
                float2 f = frac(plaza / slabSize);
                float2 joint = min(f,1-f)*slabSize;
                // Filter joints by their screen footprint to avoid flicker at a distance.
                float feather = max(.009,min(.2,max(fwidth(plaza.x),fwidth(plaza.y))));
                float seam = 1-smoothstep(.006,.025+feather,min(joint.x,joint.y));
                float2 d = abs(plaza)-float2(13.43,15.58);
                float border = 1-smoothstep(.735,.775,2.32-length(max(d,0))-min(max(d.x,d.y),0));
                float2 slab = floor(plaza/slabSize);
                base *= (1-.18*border)*(1-.38*seam)*(.96+.04*frac(sin(dot(slab,float2(13.5,71.7)))*3284.4));
            }
            o.Albedo = base;
            o.Metallic = _Metallic;
            o.Smoothness = saturate(1-_Roughness + _HasScan*(.52-scan.g)*.23);
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
