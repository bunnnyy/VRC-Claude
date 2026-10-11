// A map's sky brush faces (tools/toolsskybox), drawn like CS:S draws them: the sky seen through them, and nothing
// that's behind them (another stage, the map's outside). SourceMapVisuals renders the map's skybox into _Sky.
Shader "SourceMaps/Sky"
{
    Properties
    {
        _Sky ("Sky (cubemap)", Cube) = "grey" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            samplerCUBE _Sky;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = mul(unity_ObjectToWorld, v.vertex).xyz - _WorldSpaceCameraPos;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(texCUBE(_Sky, i.dir).rgb, 1);
            }
            ENDCG
        }
    }
}
