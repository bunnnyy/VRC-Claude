// A map's water, like CS:S draws it from above: the water's fog colour ($fogcolor, what the deep water shows) with the
// sky reflected on top, more at grazing angles (Fresnel). SourceMapVisuals gives every surface whose material uses
// Source's Water shader a material with this one, its fog colour and the map's sky colour at the horizon.
Shader "SourceMaps/Water"
{
    Properties
    {
        _FogColor ("Fog colour ($fogcolor)", Color) = (0.07, 0.14, 0.12, 1)
        _SkyColor ("Sky colour (reflected)", Color) = (0.5, 0.55, 0.6, 1)
        _Reflect ("Reflection", Range(0, 1)) = 0.5
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
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 toEye : TEXCOORD1; UNITY_FOG_COORDS(2) };

            fixed4 _FogColor, _SkyColor;
            float _Reflect;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.toEye = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float facing = abs(dot(normalize(i.normal), normalize(i.toEye)));
                float fresnel = 0.1 + 0.9 * pow(1 - facing, 4);
                fixed4 c = fixed4(_FogColor.rgb + _SkyColor.rgb * _Reflect * fresnel, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
