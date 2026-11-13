Shader "ACG/OceanShader"
{
    Properties
    {
        [Header(Waves  dirX dirZ steepness wavelength)]
        _WaveA ("Wave A", Vector) = (1, 0, 0.25, 30)
        _WaveB ("Wave B", Vector) = (0, 1, 0.2, 18)
        _WaveC ("Wave C", Vector) = (1, 1, 0.15, 10)
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 1

        [Header(Color by Depth)]
        _ShallowColor ("Shallow Color", Color) = (0.2, 0.7, 0.7, 0.3)
        _DeepColor ("Deep Color", Color) = (0.02, 0.1, 0.25, 0.95)
        _DepthMax ("Depth Max", Range(0.1, 20)) = 4

        [Header(Fresnel)]
        _FresnelColor ("Fresnel Color", Color) = (0.4, 0.6, 0.8, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 10)) = 4

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamHeight ("Foam Height", Range(0, 3)) = 0.8
        _FoamSoftness ("Foam Softness", Range(0.01, 2)) = 0.3

        _Smoothness ("Smoothness", Range(0, 1)) = 0.9
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        CGPROGRAM
        #pragma surface surf Standard alpha:fade vertex:vert
        #pragma target 3.0

        float4 _WaveA, _WaveB, _WaveC;
        float _WaveSpeed;
        float4 _ShallowColor, _DeepColor;
        float _DepthMax;
        float4 _FresnelColor;
        float _FresnelPower;
        float4 _FoamColor;
        float _FoamHeight, _FoamSoftness;
        float _Smoothness;

        sampler2D_float _CameraDepthTexture;

        struct Input
        {
            float4 screenPos;
            float3 viewDir;
            float3 worldNormal;
            float waveHeight;
        };

        float3 GerstnerWave(float4 wave, float3 p, inout float3 tangent, inout float3 binormal)
        {
            float steepness = wave.z;
            float k = UNITY_TWO_PI / wave.w;
            float c = sqrt(9.8 / k);
            float2 d = normalize(wave.xy);
            float f = k * (dot(d, p.xz) - c * _Time.y * _WaveSpeed);
            float a = steepness / k;

            tangent += float3(-d.x * d.x * steepness * sin(f),
                               d.x * steepness * cos(f),
                              -d.x * d.y * steepness * sin(f));
            binormal += float3(-d.x * d.y * steepness * sin(f),
                                d.y * steepness * cos(f),
                               -d.y * d.y * steepness * sin(f));

            return float3(d.x * a * cos(f), a * sin(f), d.y * a * cos(f));
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            float3 p = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 tangent = float3(1, 0, 0);
            float3 binormal = float3(0, 0, 1);

            float3 disp = 0;
            disp += GerstnerWave(_WaveA, p, tangent, binormal);
            disp += GerstnerWave(_WaveB, p, tangent, binormal);
            disp += GerstnerWave(_WaveC, p, tangent, binormal);

            p += disp;
            v.vertex.xyz = mul(unity_WorldToObject, float4(p, 1)).xyz;

            float3 worldNormal = normalize(cross(binormal, tangent));
            v.normal = normalize(mul(worldNormal, (float3x3)unity_ObjectToWorld));

            o.waveHeight = disp.y;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(IN.screenPos)));
            float waterDepth = sceneDepth - IN.screenPos.w;
            float t = saturate(waterDepth / _DepthMax);
            float4 waterColor = lerp(_ShallowColor, _DeepColor, t);

            float fresnel = pow(1 - saturate(dot(normalize(IN.viewDir), normalize(IN.worldNormal))), _FresnelPower);

            float foam = saturate((IN.waveHeight - _FoamHeight) / _FoamSoftness);

            o.Albedo = lerp(waterColor.rgb, _FoamColor.rgb, foam);
            o.Emission = _FresnelColor.rgb * fresnel;
            o.Smoothness = lerp(_Smoothness, 0.1, foam);
            o.Metallic = 0;
            o.Alpha = saturate(waterColor.a + fresnel + foam);
        }
        ENDCG
    }
    FallBack Off
}
