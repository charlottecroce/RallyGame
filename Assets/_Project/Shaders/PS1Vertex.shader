// PS1-style URP shader: vertex snapping, affine (non-perspective-correct) UVs,
// point-filtered textures, plus a damage overlay driven by CarAssembly.
//
Shader "Rally/PS1 Vertex"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _DamageMap ("Damage Overlay", 2D) = "black" {}
        _DamageAmount ("Damage Amount", Range(0,1)) = 0
        _DamageTiling ("Damage Tiling", Float) = 3

        [Header(PS1)]
        _SnapResolution ("Vertex Snap Grid", Float) = 160
        _AffineStrength ("Affine Warp", Range(0,1)) = 0.6
        _SnapNearFade ("Snap Fade Start (m)", Float) = 3
        _SnapFadeRange ("Snap Fade Range (m)", Float) = 4

        [Header(Lighting)]
        _Ambient ("Ambient Floor", Range(0,1)) = 0.45
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // Two copies of the same UV. The interpolator modifier is the only
                // difference: 'noperspective' gives the classic PS1 texture swim,
                // the plain one is correct, and the fragment blends between them.
                noperspective float2 uvAffine : TEXCOORD0;
                float2 uvCorrect  : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DamageMap);    SAMPLER(sampler_DamageMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _DamageAmount;
                float _DamageTiling;
                float _SnapResolution;
                float _AffineStrength;
                float _SnapNearFade;
                float _SnapFadeRange;
                float _Ambient;
                float _ShadowStrength;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float4 clip = TransformWorldToHClip(positionWS);

                // Quantise to a low-res grid in NDC: the PS1 had no subpixel precision.
                // Only where the divide is safe — w <= 0 means the vertex is at or behind
                // the camera plane, where clip.xy/clip.w is meaningless and snapping it
                // scatters the triangle. Leave those alone; the clipper handles them.
                if (clip.w > 1e-4)
                {
                    // Square cells: NDC is -1..1 on both axes but the screen is not square.
                    float aspect = _ScreenParams.y / max(_ScreenParams.x, 1.0);
                    float2 grid = float2(_SnapResolution, _SnapResolution * aspect);
                    grid = max(grid, 1.0);

                    float2 ndc = clip.xy / clip.w;
                    float2 snapped = round(ndc * grid) / grid;

                    // clip.w is view depth. Near the camera the wobble reads as a defect
                    // rather than a style, so ease it in over a few metres.
                    float strength = saturate((clip.w - _SnapNearFade) / max(_SnapFadeRange, 1e-3));

                    clip.xy = lerp(ndc, snapped, strength) * clip.w;
                }

                OUT.positionCS = clip;
                OUT.uvAffine   = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.uvCorrect  = OUT.uvAffine;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = positionWS;
                OUT.fogFactor  = ComputeFogFactor(clip.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = lerp(IN.uvCorrect, IN.uvAffine, saturate(_AffineStrength));

                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;

                // Scratch/dirt overlay: bodywork condition only.
                half4 dmg = SAMPLE_TEXTURE2D(_DamageMap, sampler_DamageMap, uv * _DamageTiling);
                base.rgb = lerp(base.rgb, base.rgb * dmg.rgb, saturate(_DamageAmount) * dmg.a);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half atten = lerp(1.0h, mainLight.shadowAttenuation, saturate(_ShadowStrength));
                half ndotl = saturate(dot(normalize(IN.normalWS), mainLight.direction));

                // Flat-ish, era-appropriate: a hard ambient floor and one diffuse term.
                half3 lighting = mainLight.color * (_Ambient + (1.0h - _Ambient) * ndotl * atten);

                half3 color = base.rgb * lighting;
                color = MixFog(color, IN.fogFactor);
                return half4(color, base.a);
            }
            ENDHLSL
        }

        // Without this the car casts no shadow and floats. Cheap, no snapping needed.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _DamageAmount;
                float _DamageTiling;
                float _SnapResolution;
                float _AffineStrength;
                float _SnapNearFade;
                float _SnapFadeRange;
                float _Ambient;
                float _ShadowStrength;
            CBUFFER_END

            float3 _LightDirection;

            float4 ShadowVert(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float4 clip = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
            #else
                clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return clip;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
