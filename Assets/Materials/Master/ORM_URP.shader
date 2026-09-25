// Стандартный PBR под URP с UE-упаковкой ORM (R=AO, G=Roughness, B=Metallic).
// Освещение считает штатный UniversalFragmentPBR.
//
// Подрезан под WebGL. Выброшены ключевые слова под функции, которых в проекте
// нет: декали, куки, динамические лайтмапы, блендинг и box projection у
// reflection probe, тени от дополнительных источников, LOD cross-fade.
// Каждое такое слово удваивает число вариантов, которые браузер компилирует
// при первом открытии курса.
//
// Оставлено то, без чего сцена сломается: запечённые лайтмапы, shadowmask,
// тени главного источника, дополнительные источники, туман, инстансинг.
Shader "Custom/ORM_URP"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Color", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)

        [NoScaleOffset] _ORMMap ("ORM (R=AO G=Rough B=Metal)", 2D) = "white" {}
        [Toggle(_ORM_G_IS_SMOOTHNESS)] _ORMGIsSmoothness ("ORM.G is Smoothness", Float) = 0
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1
        _RoughnessScale ("Roughness Scale", Range(0,2)) = 1
        _MetallicScale ("Metallic Scale", Range(0,2)) = 1

        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 1
        [Normal][NoScaleOffset] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1

        [Toggle(_EMISSION)] _UseEmission ("Use Emission", Float) = 0
        [NoScaleOffset] _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)

        [Toggle(_ALPHATEST_ON)] _UseAlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Cutoff", Range(0,1)) = 0.5

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_ORMMap);
        SAMPLER(sampler_ORMMap);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);
        TEXTURE2D(_EmissionMap);
        SAMPLER(sampler_EmissionMap);

        // всё до единого свойства должно лежать здесь, иначе отваливается SRP Batcher
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _OcclusionStrength;
            half _RoughnessScale;
            half _MetallicScale;
            half _BumpScale;
            half4 _EmissionColor;
            half _Cutoff;
        CBUFFER_END

        // ORM пришёл из UE, где G - шероховатость. URP оперирует гладкостью,
        // поэтому по умолчанию инвертируем.
        void ReadORM(float2 uv, out half occlusion, out half smoothness, out half metallic)
        {
            half3 orm = SAMPLE_TEXTURE2D(_ORMMap, sampler_ORMMap, uv).rgb;

            occlusion = LerpWhiteTo(orm.r, _OcclusionStrength);

            #ifdef _ORM_G_IS_SMOOTHNESS
            smoothness = saturate(orm.g * _RoughnessScale);
            #else
            smoothness = saturate(1.0h - orm.g * _RoughnessScale);
            #endif

            metallic = saturate(orm.b * _MetallicScale);
        }

        void ClipAlpha(float2 uv)
        {
            #ifdef _ALPHATEST_ON
            half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
            clip(alpha - _Cutoff);
            #endif
        }

        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #pragma target 3.5

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ORM_G_IS_SMOOTHNESS
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3; // w - знак бинормали
                half4 fogFactorAndVertexLight : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LitVertex(Attributes IN)
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

                OUTPUT_LIGHTMAP_UV(IN.staticLightmapUV, unity_LightmapST, OUT.staticLightmapUV);
                OUTPUT_SH(vni.normalWS, OUT.vertexSH);

                return OUT;
            }

            half4 LitFragment(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                #ifdef _ALPHATEST_ON
                clip(baseSample.a - _Cutoff);
                #endif

                half occlusion, smoothness, metallic;
                ReadORM(IN.uv, occlusion, smoothness, metallic);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = baseSample.rgb;
                surface.alpha = 1.0h;
                surface.occlusion = occlusion;
                surface.smoothness = smoothness;
                surface.metallic = metallic;
                surface.specular = 0;

                #ifdef _NORMALMAP
                surface.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                #else
                surface.normalTS = half3(0, 0, 1);
                #endif

                #ifdef _EMISSION
                surface.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;
                #endif

                half sgn = IN.tangentWS.w;
                half3 bitangent = sgn * cross(IN.normalWS.xyz, IN.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(IN.tangentWS.xyz, bitangent, IN.normalWS.xyz);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;

                #ifdef _NORMALMAP
                inputData.tangentToWorld = tangentToWorld;
                inputData.normalWS = TransformTangentToWorld(surface.normalTS, tangentToWorld);
                #else
                inputData.normalWS = IN.normalWS;
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
                inputData.bakedGI = SAMPLE_GI(IN.staticLightmapUV, IN.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(IN.staticLightmapUV);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

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

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
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

                // для точечных источников свет направлен от лампы к фрагменту
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
                ClipAlpha(IN.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma target 3.5

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVertex(DepthAttributes IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 DepthFragment(DepthVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ClipAlpha(IN.uv);
                return 0;
            }
            ENDHLSL
        }

        // без этого прохода не будет ни SSAO, ни экранных эффектов по нормалям
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma target 3.5

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DNVaryings DepthNormalsVertex(DNAttributes IN)
            {
                DNVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vni = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = vpi.positionCS;
                OUT.normalWS = vni.normalWS;
                OUT.tangentWS = half4(vni.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 DepthNormalsFragment(DNVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ClipAlpha(IN.uv);

                half3 normalWS = IN.normalWS;

                #ifdef _NORMALMAP
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                half sgn = IN.tangentWS.w;
                half3 bitangent = sgn * cross(IN.normalWS.xyz, IN.tangentWS.xyz);
                normalWS = TransformTangentToWorld(normalTS, half3x3(IN.tangentWS.xyz, bitangent, IN.normalWS.xyz));
                #endif

                return half4(NormalizeNormalPerPixel(normalWS), 0.0);
            }
            ENDHLSL
        }

        // нужен, чтобы объект участвовал в запекании света
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex ORMMetaVertex
            #pragma fragment ORMMetaFragment
            #pragma target 3.5

            #pragma shader_feature_local_fragment _EMISSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct MetaAttributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct MetaVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            MetaVaryings ORMMetaVertex(MetaAttributes IN)
            {
                MetaVaryings OUT;
                OUT.positionCS = UnityMetaVertexPosition(IN.positionOS.xyz, IN.uv1, IN.uv2, unity_LightmapST, unity_DynamicLightmapST);
                OUT.uv = TRANSFORM_TEX(IN.uv0, _BaseMap);
                return OUT;
            }

            half4 ORMMetaFragment(MetaVaryings IN) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = baseSample.rgb;

                #ifdef _EMISSION
                metaInput.Emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;
                #endif

                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
