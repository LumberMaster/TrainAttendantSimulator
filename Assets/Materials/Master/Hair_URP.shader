Shader "Custom/Hair_URP"
{
    Properties
    {
        [MainTexture] _BaseMap ("Diffuse (A = Opacity)", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        [KeywordEnum(DiffuseAlpha, Map)] _OpacitySource ("Opacity Source", Float) = 0
        [NoScaleOffset] _OpacityMap ("Opacity Map (R)", 2D) = "white" {}

        [Header(Opacity)]
        _Cutoff ("Core Cutoff", Range(0.05,1)) = 0.5
        _EdgeCutoff ("Edge Min Alpha", Range(0,0.5)) = 0.03
        _EdgeFalloff ("Edge Falloff", Range(0.25,4)) = 1

        [Header(Surface)]
        [NoScaleOffset] _SpecularMap ("Specular (RGB)", 2D) = "black" {}
        _SpecColor ("Specular Tint", Color) = (1,1,1,1)
        _PBRSpecularWeight ("PBR Specular Weight", Range(0,1)) = 0.5
        [NoScaleOffset] _GlossinessMap ("Glossiness (R)", 2D) = "white" {}
        _GlossinessScale ("Glossiness Scale", Range(0,2)) = 1

        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal][NoScaleOffset] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1

        [Header(Anisotropic Highlight)]
        [Enum(Tangent,0,Bitangent,1)] _StrandDirection ("Strand Direction (UV U or V)", Float) = 1
        _AnisoStrength ("Strength", Range(0,4)) = 0.5
        _PrimaryColor ("Primary Color", Color) = (0.6,0.6,0.6,1)
        _PrimaryShift ("Primary Shift", Range(-1,1)) = 0.1
        _PrimaryExponent ("Primary Exponent", Range(1,512)) = 96
        _SecondaryColor ("Secondary Color (x Diffuse)", Color) = (1,1,1,1)
        _SecondaryShift ("Secondary Shift", Range(-1,1)) = -0.15
        _SecondaryExponent ("Secondary Exponent", Range(1,512)) = 16

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE

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

        // всё до единого свойства должно лежать здесь, иначе отваливается SRP Batcher
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _EdgeCutoff;
            half _EdgeFalloff;
            half4 _SpecColor;
            half _PBRSpecularWeight;
            half _GlossinessScale;
            half _BumpScale;
            half _StrandDirection;
            half _AnisoStrength;
            half4 _PrimaryColor;
            half _PrimaryShift;
            half _PrimaryExponent;
            half4 _SecondaryColor;
            half _SecondaryShift;
            half _SecondaryExponent;
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

        Varyings HairVertex(Attributes IN)
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

            OUT.vertexSH = SampleSHVertex(vni.normalWS);

            return OUT;
        }

        // Kajiya-Kay: блик тянется поперёк пряди, а не пятном
        half StrandSpecular(half3 strandDir, half3 halfDir, half exponent)
        {
            half dotTH = dot(strandDir, halfDir);
            half sinTH = sqrt(saturate(1.0h - dotTH * dotTH));
            half dirAtten = smoothstep(-1.0h, 0.0h, dotTH);
            return dirAtten * pow(sinTH, exponent);
        }

        half4 ShadeHair(Varyings IN, half faceSign, half4 baseSample, half alpha)
        {
            half3 specular = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, IN.uv).rgb
                           * _SpecColor.rgb * _PBRSpecularWeight;
            half gloss = saturate(SAMPLE_TEXTURE2D(_GlossinessMap, sampler_GlossinessMap, IN.uv).r * _GlossinessScale);

            half3 normalWS = normalize(IN.normalWS);
            half3 tangentWS = normalize(IN.tangentWS.xyz);
            half3 bitangentWS = IN.tangentWS.w * cross(normalWS, tangentWS);

            // карточки двусторонние: обратной грани разворачиваем нормаль
            half3 vertexNormal = normalWS * faceSign;
            half3x3 tangentToWorld = half3x3(tangentWS, bitangentWS, vertexNormal);

            SurfaceData surface = (SurfaceData)0;
            surface.albedo = baseSample.rgb;
            surface.alpha = alpha;
            surface.specular = specular;
            surface.smoothness = gloss;
            surface.metallic = 0;
            surface.occlusion = 1.0h;

            #ifdef _NORMALMAP
            surface.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
            #else
            surface.normalTS = half3(0, 0, 1);
            #endif

            InputData inputData = (InputData)0;
            inputData.positionWS = IN.positionWS;

            #ifdef _NORMALMAP
            inputData.tangentToWorld = tangentToWorld;
            inputData.normalWS = TransformTangentToWorld(surface.normalTS, tangentToWorld);
            #else
            inputData.normalWS = vertexNormal;
            #endif

            inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
            inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);

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

            half4 color = UniversalFragmentPBR(inputData, surface);

            // анизотропный блик от главного источника
            Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
            half3 N = inputData.normalWS;
            half3 strand = normalize(lerp(tangentWS, bitangentWS, _StrandDirection));
            half3 H = normalize(mainLight.direction + inputData.viewDirectionWS);

            half3 T1 = normalize(strand + _PrimaryShift * N);
            half3 T2 = normalize(strand + _SecondaryShift * N);

            half3 aniso = StrandSpecular(T1, H, _PrimaryExponent) * _PrimaryColor.rgb
                        + StrandSpecular(T2, H, _SecondaryExponent) * _SecondaryColor.rgb * baseSample.rgb;

            // мягкий wrap: нормали карточек шумные, жёсткий NdotL даёт полосы
            half wrapNdotL = saturate((dot(N, mainLight.direction) + 0.5h) / 1.5h);
            half3 lightTerm = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation * wrapNdotL);

            color.rgb += aniso * lightTerm * gloss * _AnisoStrength;
            color.rgb = MixFog(color.rgb, inputData.fogCoord);
            return half4(color.rgb, alpha);
        }

        half GetOpacity(float2 uv, half diffuseAlpha)
        {
            #ifdef _OPACITYSOURCE_MAP
            return SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, uv).r * _BaseColor.a;
            #else
            return diffuseAlpha * _BaseColor.a;
            #endif
        }

        // проход 1: непрозрачная середина прядей
        half4 HairCoreFragment(Varyings IN, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(IN);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

            half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
            half4 baseSample = baseTex * _BaseColor;
            clip(GetOpacity(IN.uv, baseTex.a) - _Cutoff);

            return ShadeHair(IN, IS_FRONT_VFACE(isFrontFace, 1.0h, -1.0h), baseSample, 1.0h);
        }

        // проход 2: полупрозрачная кайма вокруг середины
        half4 HairEdgeFragment(Varyings IN, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(IN);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

            half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
            half4 baseSample = baseTex * _BaseColor;
            half opacity = GetOpacity(IN.uv, baseTex.a);

            // середину уже нарисовал первый проход, здесь только то, что ниже порога
            clip(opacity - _EdgeCutoff);
            clip(_Cutoff - opacity);

            // растягиваем альфу каймы до 1 на границе с серединой, чтобы не было ступеньки
            half alpha = pow(saturate(opacity / max(_Cutoff, 0.001h)), _EdgeFalloff);

            return ShadeHair(IN, IS_FRONT_VFACE(isFrontFace, 1.0h, -1.0h), baseSample, alpha);
        }

        ENDHLSL

        Pass
        {
            Name "HairCore"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex HairVertex
            #pragma fragment HairCoreFragment
            #pragma target 3.5

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _OPACITYSOURCE_DIFFUSEALPHA _OPACITYSOURCE_MAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "HairEdges"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex HairVertex
            #pragma fragment HairEdgeFragment
            #pragma target 3.5

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _OPACITYSOURCE_DIFFUSEALPHA _OPACITYSOURCE_MAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDHLSL
        }

        // тень отбрасывает только плотная середина
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma target 3.5

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local_fragment _OPACITYSOURCE_DIFFUSEALPHA _OPACITYSOURCE_MAP
            #pragma multi_compile_instancing

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVertex(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                float3 lightDir = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));

                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 ShadowFragment(ShadowVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half alpha = GetOpacity(IN.uv, SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a);
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
