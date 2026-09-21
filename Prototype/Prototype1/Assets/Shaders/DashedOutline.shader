Shader "Protector/Dashed Outline"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Edge Texture", 2D) = "white" {}
        _DashCount ("Dash Count", Float) = 40
        _DashRatio ("Dash Fraction", Range(0,1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _DashCount;
            float _DashRatio;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            Varyings vert(Input v)
            {
                Varyings o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                clip(_DashRatio - frac(i.uv.x * max(1, _DashCount)));
                return tex2D(_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw) * i.color;
            }
            ENDCG
        }
    }
}
