// Phase 0.7 Task 7.9 — toe→head / bottom→top mesh reveal.
// _Wipe 0 = fully clipped (invisible); 1 = fully revealed.
// Edge gets a plasma-esque emissive fringe. Host sets world Y bounds.
Shader "ThreeOS/VerticalWipe"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _EmissionMap ("Emission", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _GlossMapScale ("Smoothness", Range(0, 1)) = 0.5
        _BumpScale ("Normal Scale", Float) = 1
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1

        _Wipe ("Wipe", Range(0, 1)) = 0
        _WipeFeather ("Wipe Feather", Range(0.001, 0.35)) = 0.05
        _WipeEdgeColor ("Wipe Edge Color", Color) = (0.45, 0.78, 0.95, 1)
        _WipeEdgeIntensity ("Wipe Edge Intensity", Range(0, 12)) = 4
        _WipeMinY ("Wipe Min Y (world)", Float) = 0
        _WipeMaxY ("Wipe Max Y (world)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VerticalWipe"
            Tags { "LightMode" = "ForwardBase" }
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _BumpMap;
            sampler2D _MetallicGlossMap;
            sampler2D _OcclusionMap;
            sampler2D _EmissionMap;
            float4 _Color;
            float4 _EmissionColor;
            float _GlossMapScale;
            float _BumpScale;
            float _OcclusionStrength;

            float _Wipe;
            float _WipeFeather;
            float4 _WipeEdgeColor;
            float _WipeEdgeIntensity;
            float _WipeMinY;
            float _WipeMaxY;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBinormal : TEXCOORD4;
                float3 viewDir : TEXCOORD5;
                SHADOW_COORDS(6)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBinormal = cross(o.worldNormal, o.worldTangent) * v.tangent.w *
                                  unity_WorldTransformParams.w;
                o.viewDir = _WorldSpaceCameraPos - o.worldPos;
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float span = max(_WipeMaxY - _WipeMinY, 1e-4);
                float normY = saturate((i.worldPos.y - _WipeMinY) / span);
                float feather = max(_WipeFeather, 1e-4);
                // Reveal from bottom (toe) upward. Soft edge around the wipe front.
                float front = _Wipe;
                float d = front - normY; // >0 below the wipe line (revealed)
                clip(d + feather);

                float edge = 1.0 - saturate(abs(d) / feather);
                edge = pow(edge, 1.35);

                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                float3 emissionTex = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                float4 metalGloss = tex2D(_MetallicGlossMap, i.uv);
                float metallic = metalGloss.r;
                float gloss = metalGloss.a * _GlossMapScale;
                float occ = lerp(1.0, tex2D(_OcclusionMap, i.uv).g, _OcclusionStrength);

                float3 tnormal = UnpackScaleNormal(tex2D(_BumpMap, i.uv), _BumpScale);
                float3 N = normalize(
                    i.worldTangent * tnormal.x +
                    i.worldBinormal * tnormal.y +
                    i.worldNormal * tnormal.z);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = normalize(i.viewDir);
                float ndotl = saturate(dot(N, L));
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), lerp(8.0, 64.0, gloss)) * metallic;

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 ambient = ShadeSH9(float4(N, 1));
                float3 diffuse = albedo.rgb * (_LightColor0.rgb * ndotl * atten + ambient) * occ;
                float3 specular = _LightColor0.rgb * spec * atten;
                float3 plasma = _WipeEdgeColor.rgb * edge * _WipeEdgeIntensity;

                float3 rgb = diffuse + specular + emissionTex + plasma;
                return fixed4(rgb, 1);
            }
            ENDCG
        }
    }

    // Unlit fallback (no ForwardBase / missing lights).
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Wipe;
            float _WipeFeather;
            float4 _WipeEdgeColor;
            float _WipeEdgeIntensity;
            float _WipeMinY;
            float _WipeMaxY;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float span = max(_WipeMaxY - _WipeMinY, 1e-4);
                float normY = saturate((i.worldPos.y - _WipeMinY) / span);
                float feather = max(_WipeFeather, 1e-4);
                float d = _Wipe - normY;
                clip(d + feather);
                float edge = pow(1.0 - saturate(abs(d) / feather), 1.35);
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb += _WipeEdgeColor.rgb * edge * _WipeEdgeIntensity;
                return c;
            }
            ENDCG
        }
    }

    FallBack Off
}
