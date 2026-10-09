// Dev-texture style world grid for demo recordings: 64 Source unit squares, lines every 256 units, lit by the
// main directional light. Only used by the demo recorder, never in the prefab.
Shader "SourceDemo/Grid"
{
    Properties { _Color ("Color", Color) = (0.55, 0.55, 0.6, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            fixed4 _LightColor0;
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; };
            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz / 0.01905; // Source units
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = abs(normalize(i.normal));
                // Pick the two world axes across the face.
                float2 uv = n.y > 0.5 ? i.world.xz : (n.x > n.z ? i.world.zy : i.world.xy);
                float2 cell = floor(uv / 64);
                float checker = fmod(abs(cell.x + cell.y), 2) < 1 ? 1.0 : 0.85;
                float2 big = abs(frac(uv / 256 + 0.5) - 0.5) * 256;
                float edge = min(big.x, big.y) < 2 ? 0.6 : 1.0;
                float light = saturate(dot(normalize(i.normal), _WorldSpaceLightPos0.xyz)) * 0.6 + 0.45;
                return fixed4(_Color.rgb * checker * edge * light, 1);
            }
            ENDCG
        }
    }
}
