Shader "Custom/SpecGlossOpacity_URP"
{
    Properties
    {
        [MainTexture] _BaseMap ("Diffuse (A = Opacity)", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        [KeywordEnum(DiffuseAlpha, Map)] _OpacitySource ("Opacity Source", Float) = 0
        [NoScaleOffset] _OpacityMap ("Opacity Map (R)", 2D) = "white" {}
        _Opacity ("Opacity Multiplier", Range(0,1)) = 1
        _OpaqueThreshold ("Treat As Opaque Above", Range(0.5,1)) = 0.95
        _OpacityContrast ("Opacity Contrast", Range(0.25,4)) = 1

        [Header(Surface)]
        [NoScaleOffset] _SpecularMap ("Specular (RGB)", 2D) = "white" {}
        _SpecColor ("Specular Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _GlossinessMap ("Glossiness (R)", 2D) = "white" {}
        _GlossinessScale ("Glossiness Scale", Range(0,2)) = 1
        _SmoothnessMin ("Glossiness Floor", Range(0,1)) = 0

        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal][NoScaleOffset] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1

        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(1,8)) = 4
        _FresnelStrength ("Fresnel Opacity", Range(0,1)) = 0

        [Header(Rendering)]
        [Toggle] _ZWrite ("Depth Write", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex OpacityVertex
            #pragma fragment OpacityFragment
            #pragma target 3.5

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _OPACITYSOURCE_DIFFUSEALPHA _OPACITYSOURCE_MAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            // должен стоять до инклюдов: BRDF.hlsl проверяет его при разборе
            #define _SPECULAR_SETUP 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OpacityMap);
            SAMPLER(sampler_OpacityMap);
            TEXTURE2D(_SpecularMap);
            SAMPLER(sampler_SpecularMap);
            TEXTURE2D(_GlossinessMap);
            SAMPLER(sampler_GlossinessMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Opacity;
                half _OpaqueThreshold;
                half _OpacityContrast;
                half4 _SpecColor;
                half _GlossinessScale;
                half _SmoothnessMin;
                half _BumpScale;
                half _FresnelPower;
                half _FresnelStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half4 fogFactorAndVertexLight : TEXCOORD4;
                half3 vertexSH : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OpacityVertex(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vni = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS = vni.normalWS;
                OUT.tangentWS = half4(vni.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                half3 vertexLight = VertexLighting(vpi.positionWS, vni.normalWS);
                OUT.fogFactorAndVertexLight = half4(ComputeFogFactor(vpi.positionCS.z), vertexLight);

                // прозрачная геометрия не лайтмапится, непрямой свет только из SH
                OUT.vertexSH = SampleSHVertex(vni.normalWS);

                return OUT;
            }

            half RemapOpacity(half alpha)
            {
                alpha = saturate(alpha / max(_OpaqueThreshold, 0.001h));
                alpha = pow(alpha, _OpacityContrast);
                return saturate(alpha * _Opacity);
            }

            half4 OpacityFragment(Varyings IN, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 specular = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, IN.uv).rgb * _SpecColor.rgb;
                half gloss = SAMPLE_TEXTURE2D(_GlossinessMap, sampler_GlossinessMap, IN.uv).r;
                half smoothness = max(saturate(gloss * _GlossinessScale), _SmoothnessMin);

                half3 vertexNormal = normalize(IN.normalWS) * IS_FRONT_VFACE(isFrontFace, 1.0h, -1.0h);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                SurfaceData surface = (SurfaceData)0;
                surface.occlusion = 1.0h;
                surface.smoothness = smoothness;
                surface.specular = specular;
                surface.metallic = 0;

                #ifdef _NORMALMAP
                surface.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                #else
                surface.normalTS = half3(0, 0, 1);
                #endif

                half sgn = IN.tangentWS.w;
                half3 bitangent = sgn * cross(vertexNormal, IN.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(IN.tangentWS.xyz, bitangent, vertexNormal);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;

                #ifdef _NORMALMAP
                inputData.tangentToWorld = tangentToWorld;
                inputData.normalWS = TransformTangentToWorld(surface.normalTS, tangentToWorld);
                #else
                inputData.normalWS = vertexNormal;
                #endif

                inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
                inputData.viewDirectionWS = viewDirWS;

                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif

                inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactorAndVertexLight.x);
                inputData.vertexLighting = IN.fogFactorAndVertexLight.yzw;
                inputData.bakedGI = SampleSHPixel(IN.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                // источник непрозрачности: отдельная карта (R) или альфа диффуза.
                // отдельная карта идёт через UV/тайлинг диффуза
                #ifdef _OPACITYSOURCE_MAP
                half rawOpacity = SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, IN.uv).r;
                #else
                half rawOpacity = baseSample.a;
                #endif
                half alpha = RemapOpacity(rawOpacity);

                // под острым углом поверхность становится зеркалом - без этого
                // стекло выглядит одинаково мутным со всех сторон
                half fresnel = pow(1.0h - saturate(dot(inputData.normalWS, viewDirWS)), _FresnelPower);
                alpha = saturate(lerp(alpha, 1.0h, fresnel * _FresnelStrength));

                // premultiplied: диффуз гасим альфой, спекуляр и отражения оставляем
                surface.albedo = baseSample.rgb * _BaseColor.rgb * alpha;
                surface.alpha = alpha;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = alpha;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
