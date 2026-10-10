// Canonical OS glyph shader: translucent base + selection edge glow (same pass).
// Prism glyphs use object-space face-edge falloff (N·V Fresnel is useless on flat faces —
// the whole face shares one normal, so glow floods the surface at small scale).
// Hosts toggle with MaterialPropertyBlock _Selected (0 = off, 1 = on).
Shader "ThreeOS/Glyph"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.75, 0.75, 0.8, 0.55)
        _GlowColor ("Glow Color", Color) = (1, 1, 1, 1)
        _GlowIntensity ("Glow Intensity", Range(0, 8)) = 0.5
        // Higher = thinner rim (glow collapses toward the geometric edge).
        _GlowPower ("Glow Falloff Power", Range(0.5, 64)) = 3
        // How far the glow reaches from the face border toward the center (0–1 of half-face).
        // Smaller = thinner rim.
        _GlowWidth ("Glow Edge Width", Range(0.005, 0.5)) = 0.5
        _GlowBoost ("Edge Peak Boost", Range(0, 4)) = 4
        // 0 = pure geometric edge; 1 = also fade by view Fresnel (optional).
        _GlowViewMix ("View Fresnel Mix", Range(0, 1)) = 0.5
        _Selected ("Selected (look-dev)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Glyph"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _BaseColor;
            float4 _GlowColor;
            float _GlowIntensity;
            float _GlowPower;
            float _GlowWidth;
            float _GlowBoost;
            float _GlowViewMix;
            float _Selected;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 0 at face border, 1 at face center (axis-aligned prism / Unity cube).
            float FaceCenterFactorOS(float3 positionOS)
            {
                float3 a = abs(positionOS);
                float max1 = max(a.x, max(a.y, a.z));
                max1 = max(max1, 1e-5);
                // Second-largest axis: 0 at face center → max1 at the face's square border.
                float max2 = a.x >= a.y
                    ? (a.x >= a.z ? max(a.y, a.z) : max(a.x, a.y))
                    : (a.y >= a.z ? max(a.x, a.z) : max(a.x, a.y));
                return 1.0 - saturate(max2 / max1);
            }

            float EdgeGlow(float faceCenter, float3 normalWS, float3 viewDirWS)
            {
                // faceCenter: 0 at border, 1 at face center.
                // Width = how far inward from the border the band extends (smaller = thinner rim).
                float width = max(_GlowWidth, 1e-4);
                float band = saturate(1.0 - saturate(faceCenter) / width);
                // High power → quicker die-off inside that band (more base color).
                float shaped = pow(band, max(_GlowPower, 0.5));
                float peak = shaped + shaped * shaped * _GlowBoost;

                float view = 1.0;
                if (_GlowViewMix > 1e-4)
                {
                    float3 n = normalize(normalWS);
                    float3 v = normalize(viewDirWS);
                    float fresnel = saturate(1.0 - saturate(dot(n, v)));
                    view = lerp(1.0, fresnel, saturate(_GlowViewMix));
                }

                return peak * _GlowIntensity * view;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.positionOS = v.vertex.xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDirWS = _WorldSpaceCameraPos - worldPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float faceCenter = FaceCenterFactorOS(i.positionOS);
                float glow = EdgeGlow(faceCenter, i.normalWS, i.viewDirWS) * saturate(_Selected);

                float3 rgb = _BaseColor.rgb + _GlowColor.rgb * glow;
                float alpha = saturate(_BaseColor.a + glow * 0.2);
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glyph"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _BaseColor;
            float4 _GlowColor;
            float _GlowIntensity;
            float _GlowPower;
            float _GlowWidth;
            float _GlowBoost;
            float _GlowViewMix;
            float _Selected;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            float FaceCenterFactorOS(float3 positionOS)
            {
                float3 a = abs(positionOS);
                float max1 = max(a.x, max(a.y, a.z));
                max1 = max(max1, 1e-5);
                float max2 = a.x >= a.y
                    ? (a.x >= a.z ? max(a.y, a.z) : max(a.x, a.y))
                    : (a.y >= a.z ? max(a.x, a.z) : max(a.x, a.y));
                return 1.0 - saturate(max2 / max1);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.positionOS = v.vertex.xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDirWS = _WorldSpaceCameraPos - worldPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float faceCenter = FaceCenterFactorOS(i.positionOS);
                float width = max(_GlowWidth, 1e-4);
                float band = saturate(1.0 - saturate(faceCenter) / width);
                float shaped = pow(band, max(_GlowPower, 0.5));
                float peak = shaped + shaped * shaped * _GlowBoost;
                float view = 1.0;
                if (_GlowViewMix > 1e-4)
                {
                    float fresnel = saturate(1.0 - saturate(dot(normalize(i.normalWS), normalize(i.viewDirWS))));
                    view = lerp(1.0, fresnel, saturate(_GlowViewMix));
                }
                float glow = peak * _GlowIntensity * view * saturate(_Selected);

                float3 rgb = _BaseColor.rgb + _GlowColor.rgb * glow;
                float alpha = saturate(_BaseColor.a + glow * 0.2);
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
