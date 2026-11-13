Shader "ACG/TessellationShader"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _MainTex ("Diffuse (Albedo)", 2D) = "white" {}
        [NoScaleOffset] _NormalMap ("Normal Map (GL)", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0,2)) = 1
        [NoScaleOffset] _DispTex ("Displacement (Height)", 2D) = "black" {}
        _Displacement ("Displacement", Range(0,1)) = 0.15
        _Tiling ("Tiling", Range(0.1,20)) = 4

        _Tess ("Tessellation Factor", Range(1,64)) = 32
        _MinDist ("Tess Min Distance", Range(0,50)) = 5
        _MaxDist ("Tess Max Distance", Range(1,200)) = 40

        _Smoothness ("Smoothness", Range(0,1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        CGPROGRAM
        #pragma surface surf Standard addshadow fullforwardshadows vertex:disp tessellate:tessDistance nolightmap
        #pragma target 4.6
        #include "Tessellation.cginc"

        struct appdata
        {
            float4 vertex : POSITION;
            float4 tangent : TANGENT;
            float3 normal : NORMAL;
            float2 texcoord : TEXCOORD0;
        };

        sampler2D _MainTex;
        sampler2D _NormalMap;
        sampler2D _DispTex;
        float4 _Color;
        float _NormalStrength;
        float _Displacement;
        float _Tiling;
        float _Tess;
        float _MinDist;
        float _MaxDist;
        float _Smoothness;

        float4 tessDistance(appdata v0, appdata v1, appdata v2)
        {
            return UnityDistanceBasedTess(v0.vertex, v1.vertex, v2.vertex, _MinDist, _MaxDist, _Tess);
        }

        void disp(inout appdata v)
        {
            float2 uv = v.texcoord.xy * _Tiling;
            float h = tex2Dlod(_DispTex, float4(uv, 0, 0)).r;
            v.vertex.xyz += v.normal * h * _Displacement;
        }

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.uv_MainTex * _Tiling;
            o.Albedo = tex2D(_MainTex, uv).rgb * _Color.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_NormalMap, uv), _NormalStrength);
            o.Smoothness = _Smoothness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
