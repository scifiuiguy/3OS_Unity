// OVR-style translucent blue Fresnel ghost for skinned hand meshes.
// Built-in RP pass (this sample project has no URP asset). Matches Glyph CG style.
Shader "ThreeOS/HandGhost"
{
    Properties
    {
        _Color ("Tint", Color) = (0.25, 0.55, 1.0, 0.18)
        _RimColor ("Rim Color", Color) = (0.45, 0.75, 1.0, 0.85)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimIntensity ("Rim Intensity", Range(0, 4)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HandGhost"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float4 _RimColor;
            float _RimPower;
            float _RimIntensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldView : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityWorldToClipPos(world.xyz);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldView = _WorldSpaceCameraPos - world.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.worldView);
                float ndotv = saturate(dot(n, v));
                float rim = pow(1.0 - ndotv, _RimPower) * _RimIntensity;
                float3 rgb = lerp(_Color.rgb, _RimColor.rgb, saturate(rim));
                float a = saturate(_Color.a + rim * _RimColor.a);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }

    FallBack Off
}
