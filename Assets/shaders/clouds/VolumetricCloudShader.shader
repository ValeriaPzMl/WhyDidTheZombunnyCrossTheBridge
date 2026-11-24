Shader "ACG/VolumetricCloudShader"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex ("Noise 3D (Perlin-Worley)", 3D) = "white" {}

        [Header(Shape)]
        _ShapeScale ("Shape Scale", Range(0.001, 0.05)) = 0.007
        _DetailScale ("Detail Scale", Range(0.005, 0.2)) = 0.03
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.35
        _Coverage ("Coverage", Range(0, 1)) = 0.55
        _Density ("Density", Range(0, 5)) = 1.5
        _EdgeFade ("Edge Fade (m)", Range(1, 100)) = 40
        _Wind ("Wind (xyz)", Vector) = (0.004, 0, 0.002, 0)

        [Header(Lighting)]
        _Absorption ("Absorption (Beer-Lambert)", Range(0, 2)) = 0.6
        _LightAbsorption ("Light Absorption", Range(0, 2)) = 0.8
        _LightIntensity ("Sun Intensity", Range(0, 5)) = 1.5
        _Anisotropy ("HG Anisotropy (g)", Range(0, 0.99)) = 0.6
        _SilverIntensity ("Silver Lining", Range(0, 1)) = 0.6
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _AmbientColor ("Ambient Color", Color) = (0.55, 0.65, 0.8, 1)
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 0.6

        [Header(Quality)]
        _Steps ("Ray Steps", Range(8, 128)) = 48
        _LightSteps ("Light Steps", Range(1, 16)) = 6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler3D _NoiseTex;
            float _ShapeScale, _DetailScale, _DetailStrength, _Coverage, _Density, _EdgeFade;
            float4 _Wind;
            float _Absorption, _LightAbsorption, _LightIntensity, _Anisotropy, _SilverIntensity;
            float4 _CloudColor, _AmbientColor;
            float _AmbientIntensity;
            float _Steps, _LightSteps;

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            static float3 boxMin;
            static float3 boxMax;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float Remap(float v, float l0, float h0, float l1, float h1)
            {
                return l1 + (v - l0) * (h1 - l1) / (h0 - l0);
            }

            float2 RayBox(float3 ro, float3 invDir)
            {
                float3 t0 = (boxMin - ro) * invDir;
                float3 t1 = (boxMax - ro) * invDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                float tNear = max(max(tmin.x, tmin.y), tmin.z);
                float tFar = min(min(tmax.x, tmax.y), tmax.z);
                return float2(tNear, tFar);
            }

            float SampleDensity(float3 p)
            {
                float3 wind = _Wind.xyz * _Time.y;

                float4 shape = tex3Dlod(_NoiseTex, float4(p * _ShapeScale + wind, 0));
                float worleyFbm = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
                float base = Remap(shape.r, worleyFbm - 1, 1, 0, 1);

                float h = saturate((p.y - boxMin.y) / (boxMax.y - boxMin.y));
                float heightGrad = saturate(Remap(h, 0, 0.2, 0, 1)) * saturate(Remap(h, 0.6, 1, 1, 0));
                base *= heightGrad;

                float cloud = saturate(Remap(base, 1 - _Coverage, 1, 0, 1));
                if (cloud <= 0) return 0;

                float4 detail = tex3Dlod(_NoiseTex, float4(p * _DetailScale + wind * 1.5, 0));
                float detailFbm = detail.g * 0.625 + detail.b * 0.25 + detail.a * 0.125;
                cloud = saturate(Remap(cloud, detailFbm * _DetailStrength, 1, 0, 1));

                float3 toMin = p - boxMin;
                float3 toMax = boxMax - p;
                float edge = min(min(toMin.x, toMax.x), min(toMin.z, toMax.z));
                cloud *= saturate(edge / _EdgeFade);

                return cloud * _Density;
            }

            float LightMarch(float3 p, float3 L)
            {
                float tExit = RayBox(p, 1 / L).y;
                float stepL = tExit / _LightSteps;
                float sum = 0;
                for (int i = 0; i < 16; i++)
                {
                    if (i >= _LightSteps) break;
                    p += L * stepL;
                    sum += SampleDensity(p) * stepL;
                }
                return exp(-sum * _LightAbsorption);
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1 - g2) / (4 * UNITY_PI * pow(1 + g2 - 2 * g * cosTheta, 1.5));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 center = unity_ObjectToWorld._m03_m13_m23;
                float3 size = float3(length(unity_ObjectToWorld._m00_m10_m20),
                                     length(unity_ObjectToWorld._m01_m11_m21),
                                     length(unity_ObjectToWorld._m02_m12_m22));
                boxMin = center - size * 0.5;
                boxMax = center + size * 0.5;

                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.worldPos - ro);

                float2 hit = RayBox(ro, 1 / rd);
                float tNear = max(hit.x, 0);
                float tFar = hit.y;

                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float eyeDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV));
                float3 camForward = -UNITY_MATRIX_V[2].xyz;
                float sceneDist = eyeDepth / dot(rd, camForward);
                tFar = min(tFar, sceneDist);

                if (tFar <= tNear) return 0;

                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float cosTheta = dot(rd, L);
                float phase = lerp(1, HenyeyGreenstein(cosTheta, _Anisotropy) * 4 * UNITY_PI, _SilverIntensity);

                float stepSize = (tFar - tNear) / _Steps;
                float jitter = frac(sin(dot(screenUV * _ScreenParams.xy, float2(12.9898, 78.233))) * 43758.5453);
                float t = tNear + stepSize * jitter;

                float transmittance = 1;
                float3 light = 0;

                for (int s = 0; s < 128; s++)
                {
                    if (s >= _Steps || t >= tFar) break;

                    float3 p = ro + rd * t;
                    float density = SampleDensity(p);

                    if (density > 0.001)
                    {
                        float lightT = LightMarch(p, L);
                        float3 lum = _LightColor0.rgb * lightT * phase * _LightIntensity
                                   + _AmbientColor.rgb * _AmbientIntensity;

                        float sampleT = exp(-density * stepSize * _Absorption);
                        light += transmittance * lum * (1 - sampleT);
                        transmittance *= sampleT;

                        if (transmittance < 0.01) break;
                    }
                    t += stepSize;
                }

                return float4(light * _CloudColor.rgb, 1 - transmittance);
            }
            ENDCG
        }
    }
    FallBack Off
}
