Shader "Custom/Paint"
{
    Properties
    {
        _PaintPositionWS("Paint Position", Vector) = (0, 0, 0, 0)
        _PaintNormalWS("Paint Normal", Vector) = (0, 0, 0, 0)
        _SpreadRadius("Spread Radius", Float) = 0
        _PaintColor("Paint Color", Color) = (0, 0, 0, 0)
        
        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
        }
        
        Pass
        {
            Name "Universal Forward"
            Tags
            {
            }
            Cull Off
            ZWrite Off
            ZClip On
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_local_fragment _ FLOOD_COLOR
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 lightMapUV   : TEXCOORD1;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float3 _PaintPositionWS;
                float3 _PaintNormalWS;
                float _SpreadRadius;
                float4 _PaintColor;
            CBUFFER_END
            
            Varyings vert(Attributes i)
            {
                Varyings v;
                v.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                v.normalWS = TransformObjectToWorldNormal(i.normalOS);
                
                float3 remappedPositionWS = float3(i.lightMapUV * 2 - 1, 0);
                v.positionCS = TransformWorldToHClip(remappedPositionWS);
                return v;
            }
            
            float4 frag(Varyings i) : SV_Target0
            {
            #ifdef FLOOD_COLOR
                return float4(_PaintColor.rgb, 1);
            #endif
                
                float dist = distance(i.positionWS, _PaintPositionWS);
                float positionStrength = 1 - saturate(dist / _SpreadRadius);
                float facingStrength = dot(i.normalWS, _PaintNormalWS) > 0 ? 1 : 0;
                return float4(_PaintColor.rgb, positionStrength * facingStrength);
            }
            
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}