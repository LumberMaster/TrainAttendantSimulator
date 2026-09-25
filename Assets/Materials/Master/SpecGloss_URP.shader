Shader "Custom/SpecGloss_URP"
{
    Properties
    {
        [MainTexture] _BaseMap ("Diffuse (A = Opacity)", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        [KeywordEnum(DiffuseAlpha, Map)] _OpacitySource ("Opacity Source", Float) = 0
        [NoScaleOffset] _OpacityMap ("Opacity Map (R)", 2D) = "white" {}

        [NoScaleOffset] _SpecularMap ("Specular (RGB)", 2D) = "white" {}
        _SpecColor ("Specular Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _GlossinessMap ("Glossiness (R)", 2D) = "white" {}
        _GlossinessScale ("Glossiness Scale", Range(0,2)) = 1

        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 1
        [Normal][NoScaleOffset] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1

        [Toggle(_EMISSION)] _UseEmission ("Use Emission", Float) = 0
        [NoScaleOffset] _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)

        [Toggle(_ALPHATEST_ON)] _UseAlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Cutoff", Range(0,1)) = 0.5
        [Toggle] _AlphaToMask ("Alpha To Coverage (MSAA)", Float) = 0

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
        TEXTURE2D(_EmissionMap);
        SAMPLER(sampler_EmissionMap);

        // всё до единого свойства должно лежать здесь, иначе отваливается SRP Batcher
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _SpecColor;
            half _GlossinessScale;
            half _BumpScale;
            half4 _EmissionColor;
            half _Cutoff;
            half _AlphaToMask;
        CBUFFER_END

        void ReadSpecGloss(float2 uv, out half3 specular, out half smoothness)
        {
            specular = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, uv).rgb * _SpecColor.rgb;
            smoothness = saturate(SAMPLE_TEXTURE2D(_GlossinessMap, sampler_GlossinessMap, uv).r * _GlossinessScale);
        }

        half GetOpacity(float2 uv, half diffuseAlpha)
        {
            #ifdef _OPACITYSOURCE_MAP
            return SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, uv).r * _BaseColor.a;
            #else
            return diffuseAlpha * _BaseColor.a;
            #endif
        }

        void ClipAlpha(float2 uv)
        {
            #ifdef _ALPHATEST_ON
            half alpha = GetOpacity(uv, SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a);
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
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #pragma target 3.5

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _OPACITYSOURCE_DIFFUSEALPHA _OPACITYSOURCE_MAP
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
                half4 tangentWS : TEXCOORD3;
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

            half4 LitFragment(Varyings IN, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 baseSample = baseTex * _BaseColor;

                half outAlpha = 1.0h;
                #ifdef _ALPHATEST_ON
                half opacity = GetOpacity(IN.uv, baseTex.a);
                half aaWidth = max(fwidth(opacity), 0.0001h);
                outAlpha = saturate((opacity - _Cutoff) / aaWidth + 0.5h);
                clip(opacity - _Cutoff);
                #endif

                half3 specular;
                half smoothness;
                ReadSpecGloss(IN.uv, specular, smoothness);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = baseSample.rgb;
                surface.alpha = 1.0h;
                surface.occlusion = 1.0h;
                surface.smoothness = smoothness;
                surface.specular = specular;
                surface.metallic = 0;

                #ifdef _NORMALMAP
                surface.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                #else
                surface.normalTS = half3(0, 0, 1);
                #endif

                #ifdef _EMISSION
                surface.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;
                #endif

                // при Cull Off обратная грань иначе освещается как лицевая
                half3 vertexNormal = IN.normalWS * IS_FRONT_VFACE(isFrontFace, 1.0h, -1.0h);

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
                color.a = outAlpha;
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
            #pragma shader_feature_local_fragment _OPACITYSOURCE_DIFFUSEALPHA _OPACITYSOURCE_MAP
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
            #pragma shader_feature_local_fragment _OPACITYSOURCE_DIFFUSEALPHA _OPACITYSOURCE_MAP
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
            #pragma shader_feature_local_fragment _OPACITYSOURCE_DIFFUSEALPHA _OPACITYSOURCE_MAP
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

            half4 DepthNormalsFragment(DNVaryings IN, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ClipAlpha(IN.uv);

                half3 normalWS = IN.normalWS * IS_FRONT_VFACE(isFrontFace, 1.0h, -1.0h);

                #ifdef _NORMALMAP
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                half sgn = IN.tangentWS.w;
                half3 bitangent = sgn * cross(normalWS, IN.tangentWS.xyz);
                normalWS = TransformTangentToWorld(normalTS, half3x3(IN.tangentWS.xyz, bitangent, normalWS));
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
            #pragma vertex SGMetaVertex
            #pragma fragment SGMetaFragment
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

            MetaVaryings SGMetaVertex(MetaAttributes IN)
            {
                MetaVaryings OUT;
                OUT.positionCS = UnityMetaVertexPosition(IN.positionOS.xyz, IN.uv1, IN.uv2, unity_LightmapST, unity_DynamicLightmapST);
                OUT.uv = TRANSFORM_TEX(IN.uv0, _BaseMap);
                return OUT;
            }

            half4 SGMetaFragment(MetaVaryings IN) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                half3 specular;
                half smoothness;
                ReadSpecGloss(IN.uv, specular, smoothness);

                half reflectivity = max(max(specular.r, specular.g), specular.b);
                half roughness = PerceptualSmoothnessToRoughness(smoothness);

                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = baseSample.rgb * (1.0h - reflectivity) + specular * roughness * 0.5h;

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
