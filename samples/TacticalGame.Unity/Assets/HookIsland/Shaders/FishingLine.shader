Shader "Hook Island/Fishing Line"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Input { float4 vertex:POSITION; fixed4 color:COLOR; };
            struct Output { float4 vertex:SV_POSITION; fixed4 color:COLOR; };
            Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color*_Color;return o; }
            fixed4 frag(Output i):SV_Target { return i.color; }
            ENDCG
        }
    }
}
