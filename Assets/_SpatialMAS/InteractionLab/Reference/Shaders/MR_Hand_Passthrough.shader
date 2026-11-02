Shader "EnigmaLux/MR_Hand_Passthrough"
{
    Properties
    {
        _MainColor ("Center Color (Transparent)", Color) = (0.05, 0.15, 0.25, 0.02)
        _RimColor ("Silhouette Rim Color", Color) = (0.60, 0.88, 1.0, 0.40)
        _RimPower ("Rim Fresnel Power", Range(1.0, 8.0)) = 3.5
        _OutlineWidth ("Outline Extrusion Width", Range(0.0, 0.005)) = 0.0005

        // Horizon OS Wrist Fade Out parameters
        // _WristFadeParams: (x = FadeStart offset into palm, y = FadeEnd offset behind wrist, z = unused, w = enableFade: 1/0)
        _WristFadeParams ("Wrist Fade Parameters", Vector) = (0.04, -0.015, 0.0, 1.0)
        _WristPosWS ("Wrist World Position", Vector) = (0, 0, 0, 0)
        _WristDirWS ("Wrist Forward Direction", Vector) = (0, 1, 0, 0)

        // Compatibility aliases for runtime script property bindings and XRI Affordance animations
        [HideInInspector] _Color ("Rim Color Alias", Color) = (0.60, 0.88, 1.0, 0.40)
        [HideInInspector] _EdgeColor ("Edge Color Alias", Color) = (0.60, 0.88, 1.0, 0.40)
        [HideInInspector] _EdgeHighlightPower ("Edge Highlight Power Alias", Float) = 3.5
        [HideInInspector] _FingerColor_1 ("Finger 1 Color Alias", Color) = (0.60, 0.88, 1.0, 0.0)
        [HideInInspector] _FingerColor_2 ("Finger 2 Color Alias", Color) = (0.60, 0.88, 1.0, 0.0)
        [HideInInspector] _FingerColor_3 ("Finger 3 Color Alias", Color) = (0.60, 0.88, 1.0, 0.0)
        [HideInInspector] _FingerColor_4 ("Finger 4 Color Alias", Color) = (0.60, 0.88, 1.0, 0.0)
        [HideInInspector] _ThumbColor ("Thumb Color Alias", Color) = (0.60, 0.88, 1.0, 0.0)
        [HideInInspector] _BaseColor ("Base Color Alias", Color) = (0.05, 0.15, 0.25, 0.02)
        [HideInInspector] _HandColor ("Hand Color Alias", Color) = (0.05, 0.15, 0.25, 0.02)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Standard URP transparency blend preserving alpha accumulation for Meta Quest Passthrough underlay compositing.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _MainColor;
                half4 _RimColor;
                float _RimPower;
                float _OutlineWidth;
                float4 _WristFadeParams;
                float4 _WristPosWS;
                float4 _WristDirWS;
                half4 _Color;
                half4 _EdgeColor;
                float _EdgeHighlightPower;
                half4 _FingerColor_1;
                half4 _FingerColor_2;
                half4 _FingerColor_3;
                half4 _FingerColor_4;
                half4 _ThumbColor;
                half4 _BaseColor;
                half4 _HandColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Slight normal extrusion to hug the physical hand naturally
                float3 posOS = input.positionOS.xyz + input.normalOS * _OutlineWidth;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(posOS);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);

                // Dot product between surface normal and eye view direction
                float NdotV = abs(dot(normalWS, viewDirWS));
                float fresnel = 1.0 - saturate(NdotV);
                float rim = pow(fresnel, _RimPower);

                // Horizon OS Wrist Fade Out
                // Smoothly fades out the hand mesh towards the wrist to eliminate harsh cutoffs and open cylinder edges
                float wristFade = 1.0;
                if (_WristFadeParams.w > 0.5 && dot(_WristDirWS.xyz, _WristDirWS.xyz) > 0.001)
                {
                    float3 wristToVert = input.positionWS - _WristPosWS.xyz;
                    float wristDist = dot(wristToVert, normalize(_WristDirWS.xyz));
                    float fadeT = saturate((wristDist - _WristFadeParams.y) / max(0.001, (_WristFadeParams.x - _WristFadeParams.y)));
                    wristFade = smoothstep(0.0, 1.0, fadeT);
                }

                // Blend see-through center with luminous holographic silhouette rim, smoothed at the wrist
                half3 col = lerp(_MainColor.rgb, _RimColor.rgb, rim) * wristFade;
                half alpha = lerp(_MainColor.a, _RimColor.a, rim) * wristFade;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
