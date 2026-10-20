// Phase 0.7 Task 7.7 — feathered cylindrical skybox wipe (camera-relative shell).
// Host drives _Progress 0..1 and _Direction (1 = outward Palace→MR, -1 = inward MR→Palace).
Shader "ThreeOS/SkyboxWipe"
{
    Properties
    {
        _Color ("Laser Color", Color) = (0.45, 0.78, 0.95, 0.85)
        _Progress ("Progress", Range(0, 1)) = 0
        _Direction ("Direction", Float) = 1
        _Feather ("Feather", Range(0.01, 0.35)) = 0.08
        _SkyTint ("Sky Tint", Color) = (0.55, 0.72, 0.95, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "SkyboxWipe"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Progress;
            float _Direction;
            float _Feather;
            float4 _SkyTint;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 localPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Cylinder around Y: radius in XZ, height fade near poles.
                float r = length(i.localPos.xz);
                float h = abs(i.localPos.y);
                float maxR = 0.5; // unit cylinder mesh scaled by host
                float normR = saturate(r / max(maxR, 1e-4));

                float edge = _Progress;
                float cover;
                if (_Direction >= 0.0)
                {
                    // Outward blast: reveal office as ring expands (sky fades from center out).
                    cover = 1.0 - smoothstep(edge - _Feather, edge + _Feather, normR);
                }
                else
                {
                    // Inward close: sky closes in from horizon toward user.
                    float inv = 1.0 - edge;
                    cover = smoothstep(inv - _Feather, inv + _Feather, normR);
                }

                // Soft vertical feather so the wipe reads cylindrical, not a flat disc.
                float heightFade = 1.0 - smoothstep(0.35, 0.55, h);
                float alpha = saturate(cover * heightFade) * _Color.a;
                float3 col = lerp(_SkyTint.rgb, _Color.rgb, 0.35);
                return float4(col, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
