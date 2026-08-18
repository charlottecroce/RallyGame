// PS1-style URP shader: vertex snapping, affine (non-perspective-correct) UVs,
// point-filtered textures, plus a damage overlay driven by CarAssembly.
Shader "Rally/PS1 Vertex"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _DamageMap ("Damage Overlay", 2D) = "black" {}
        _DamageAmount ("Damage Amount", Range(0,1)) = 0
        _SnapResolution ("Vertex Snap Resolution", Float) = 80
        _AffineStrength ("Affine Warp", Range(0,1)) = 1
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
                noperspective float2 uv : TEXCOORD0;   // affine mapping = classic PS1 texture swim
                float3 normalWS   : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DamageMap);    SAMPLER(sampler_DamageMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _DamageAmount;
                float _SnapResolution;
                float _AffineStrength;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float4 clip = TransformObjectToHClip(IN.positionOS.xyz);

                // Quantise to a low-res grid in NDC: the PS1 had no subpixel precision.
                float2 grid = _SnapResolution;
                float2 ndc = clip.xy / clip.w;
                ndc = floor(ndc * grid) / grid;
                clip.xy = ndc * clip.w;

                OUT.positionCS = clip;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Scratch/dirt overlay: bodywork condition only.
                half4 dmg = SAMPLE_TEXTURE2D(_DamageMap, sampler_DamageMap, IN.uv * 3.0);
                base.rgb = lerp(base.rgb, base.rgb * dmg.rgb, saturate(_DamageAmount) * dmg.a);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                half3 lighting = mainLight.color * (0.45 + 0.55 * ndotl);   // flat-ish, era-appropriate

                return half4(base.rgb * lighting, base.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
