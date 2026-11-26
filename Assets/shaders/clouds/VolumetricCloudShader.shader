Shader "ACG/VolumetricCloudShader"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex ("Noise 3D (Perlin-Worley)", 3D) = "white" {}

        [Header(Shape)]
        _MacroScale ("Macro Scale (Separation)", Range(0.0005, 0.015)) = 0.0025
        _ShapeScale ("Shape Scale", Range(0.001, 0.05)) = 0.011
        _VerticalScale ("Vertical Noise Stretch", Range(0.5, 3.0)) = 1.6
        _DetailScale ("Detail Scale", Range(0.005, 0.2)) = 0.045
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.55
        _Coverage ("Coverage", Range(0, 1)) = 0.48
        _Separation ("Cloud Separation", Range(0, 1)) = 0.6
        _Density ("Density", Range(0, 5)) = 1.8
        _EdgeFade ("Edge Fade (m)", Range(1, 100)) = 45
        _Wind ("Wind (xyz)", Vector) = (0.004, 0, 0.002, 0)

        [Header(Lighting)]
        _Absorption ("Absorption (Beer-Lambert)", Range(0, 2)) = 0.55
        _LightAbsorption ("Light Absorption", Range(0, 2)) = 0.75
        _LightIntensity ("Sun Intensity", Range(0, 5)) = 1.6
        _Anisotropy ("HG Anisotropy (g)", Range(0, 0.99)) = 0.65
        _SilverIntensity ("Silver Lining", Range(0, 1)) = 0.65
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _AmbientColor ("Ambient Color", Color) = (0.52, 0.64, 0.82, 1)
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 0.65

        [Header(Quality)]
        _Steps ("Ray Steps", Range(8, 128)) = 64
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
            float _MacroScale, _ShapeScale, _VerticalScale, _DetailScale, _DetailStrength;
            float _Coverage, _Separation, _Density, _EdgeFade;
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

            float3 SafeInv(float3 v)
            {
                float3 s = step(0.0, v) * 2.0 - 1.0;
                return 1.0 / (s * max(abs(v), 1e-5));
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

                // 1. Macro weather mask: separa las nubes en cumulos individuales con cielo abierto
                float3 macroPos = float3(p.x, p.y * 0.15, p.z) * _MacroScale + wind * 0.3;
                float4 macroTex = tex3Dlod(_NoiseTex, float4(macroPos, 0));
                float macroPerlin = saturate(Remap(macroTex.r, 0.55, 0.78, 0.0, 1.0));
                float macro = macroPerlin * 0.55 + macroTex.g * 0.45;

                float threshold = lerp(0.82, 0.22, _Coverage);
                float softness = lerp(0.45, 0.14, _Separation);
                float weather = saturate((macro - threshold) / softness);
                if (weather <= 0.001) return 0;

                // 2. Perfil vertical de cumulo: base suave y topes redondeados variables segun weather
                float h = saturate((p.y - boxMin.y) / (boxMax.y - boxMin.y));
                float topH = lerp(0.32, 1.0, weather);
                float heightGrad = saturate(Remap(h, 0.0, 0.14, 0.0, 1.0))
                                 * saturate(Remap(h, topH * 0.42, topH, 1.0, 0.0));
                if (heightGrad <= 0.001) return 0;

                // 3. Forma volumetrica 3D (estirada en Y para dar volumen real dentro de la caja)
                float3 scaledP = float3(p.x, p.y * _VerticalScale, p.z);
                float4 shape = tex3Dlod(_NoiseTex, float4(scaledP * _ShapeScale + wind, 0));
                float worleyFbm = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
                float baseShape = saturate(Remap(shape.r, 0.52, 0.80, 0.0, 1.0));
                float shapeNoise = saturate(Remap(baseShape, (1.0 - worleyFbm) * 0.45, 1.0, 0.0, 1.0));

                float cloud = saturate(Remap(shapeNoise * heightGrad * weather, 0.12, 0.85, 0.0, 1.0));
                if (cloud <= 0.001) return 0;

                // 4. Erosion de detalle 3D (coliflor arriba, tenue abajo) con ligera distorsion organica
                float3 warp = (shape.gba - 0.5) * 0.08;
                float4 detail = tex3Dlod(_NoiseTex, float4(scaledP * _DetailScale + warp + wind * 1.8, 0));
                float detailFbm = detail.g * 0.625 + detail.b * 0.25 + detail.a * 0.125;
                float detailMod = lerp(detailFbm, 1.0 - detailFbm, saturate(h * 3.0));
                float erode = min(detailMod * _DetailStrength * (1.0 - cloud * 0.35), 0.95);
                cloud = saturate(Remap(cloud, erode, 1.0, 0.0, 1.0));

                float3 toMin = p - boxMin;
                float3 toMax = boxMax - p;
                float edge = min(min(toMin.x, toMax.x), min(toMin.z, toMax.z));
                cloud *= smoothstep(0.0, _EdgeFade, edge);

                return cloud * _Density;
            }

            float LightMarch(float3 p, float3 L)
            {
                float tExit = RayBox(p, SafeInv(L)).y;
                float maxDist = min(tExit, (boxMax.y - boxMin.y) * 1.6);
                float stepL = maxDist / _LightSteps;
                float sum = 0;
                p += L * (stepL * 0.5);
                for (int i = 0; i < 16; i++)
                {
                    if (i >= _LightSteps) break;
                    sum += SampleDensity(p) * stepL;
                    p += L * stepL;
                }
                float od = sum * _LightAbsorption;
                return max(exp(-od), exp(-od * 0.25) * 0.65);
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1 - g2) / (4 * UNITY_PI * pow(1 + g2 - 2 * g * cosTheta, 1.5));
            }

            float DualHenyeyGreenstein(float cosTheta, float g)
            {
                return lerp(HenyeyGreenstein(cosTheta, -g * 0.35), HenyeyGreenstein(cosTheta, g), 0.75);
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

                float2 hit = RayBox(ro, SafeInv(rd));
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
                float phase = lerp(1, DualHenyeyGreenstein(cosTheta, _Anisotropy) * 4 * UNITY_PI, _SilverIntensity);

                float stepSize = (tFar - tNear) / _Steps;
                float2 px = screenUV * _ScreenParams.xy;
                float jitter = frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715))));
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
                        float h = saturate((p.y - boxMin.y) / (boxMax.y - boxMin.y));
                        float lightT = LightMarch(p, L);
                        float powder = 1.0 - 0.35 * exp(-density * 2.0);
                        float3 lum = _LightColor0.rgb * lightT * powder * phase * _LightIntensity
                                   + _AmbientColor.rgb * _AmbientIntensity * lerp(0.45, 1.15, h);

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
