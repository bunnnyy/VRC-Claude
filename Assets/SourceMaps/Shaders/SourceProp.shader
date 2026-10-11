// A static prop lit like CS:S lights models: SourceMapVisuals bakes the map's ambient cube and its visible lights into
// the vertex colours (half brightness, so up to 2x fits), and this shader multiplies the texture by them.
// Translucent models ($translucent) blend.
Shader "SourceMaps/Prop"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha cutoff (0 = off)", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 0
        [Toggle] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 light : TEXCOORD1; UNITY_FOG_COORDS(2) };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.light = v.color.rgb * 2;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                clip(c.a - _Cutoff);
                c.rgb *= i.light;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return fixed4(c.rgb, _SrcBlend == 1 ? 1 : c.a);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
