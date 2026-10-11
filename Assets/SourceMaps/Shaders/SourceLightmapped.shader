// A map surface lit by its own Source lightmap, like CS:S's LightmappedGeneric: texture x lightmap, no other lighting.
// SourceMapVisuals gives every imported brush surface a material with this shader and its lightmap (UV2).
// _SecondTex blends a second texture by vertex alpha (displacements, WorldVertexTransition); _Detail is $detail
// (Source's default blend: base x detail x 2, by $detailblendfactor); translucent surfaces ($translucent) blend.
Shader "SourceMaps/Lightmapped"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _SecondTex ("Second texture (blended by vertex alpha)", 2D) = "white" {}
        [Toggle] _Blend ("Blend second texture", Float) = 0
        _LightMap ("Lightmap (UV2, half brightness)", 2D) = "gray" {}
        _Cutoff ("Alpha cutoff (0 = off)", Range(0,1)) = 0
        _Detail ("Detail texture ($detail)", 2D) = "gray" {}
        _DetailFactor ("Detail blend factor (0 = off)", Range(0,1)) = 0
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

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float2 uvSecond : TEXCOORD2; float blend : TEXCOORD3; float2 uvDetail : TEXCOORD5; UNITY_FOG_COORDS(4) };

            sampler2D _MainTex, _SecondTex, _LightMap, _Detail;
            float4 _MainTex_ST, _SecondTex_ST, _Detail_ST;
            fixed4 _Color;
            float _Blend, _Cutoff, _DetailFactor, _SrcBlend;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvSecond = TRANSFORM_TEX(v.uv, _SecondTex);
                o.uvDetail = TRANSFORM_TEX(v.uv, _Detail);
                o.uv2 = v.uv2;
                o.blend = _Blend * v.color.a;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = lerp(tex2D(_MainTex, i.uv), tex2D(_SecondTex, i.uvSecond), i.blend) * _Color;
                clip(c.a - _Cutoff);
                c.rgb *= lerp(1, tex2D(_Detail, i.uvDetail).rgb * 2, _DetailFactor);
                // The lightmap holds half the light (so up to 2x brightness fits): x2, as Source's overbright.
                c.rgb *= tex2D(_LightMap, i.uv2).rgb * 2;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return fixed4(c.rgb, _SrcBlend == 1 ? 1 : c.a);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
