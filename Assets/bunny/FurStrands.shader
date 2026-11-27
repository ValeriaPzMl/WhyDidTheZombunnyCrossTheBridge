Shader "ACG/FurStrands"
{
    Properties
    {
        _RootColor ("Color raiz", Color) = (0.30, 0.10, 0.42, 1)
        _TipColor ("Color punta", Color) = (0.95, 0.80, 1.00, 1)
        _ColorVariation ("Variacion de tono", Range(0, 0.5)) = 0.2
        _Width ("Grosor de hebra", Range(0.0005, 0.01)) = 0.002
        _Taper ("Afinado hacia la punta", Range(0.3, 4)) = 1.5
        _SpecStrength ("Brillo", Range(0, 1)) = 0.35
        _SpecPower ("Nitidez del brillo", Range(4, 200)) = 80
        _Backlight ("Luz por detras", Range(0, 2)) = 0.6
        _WindStrength ("Viento: fuerza", Range(0, 0.05)) = 0.012
        _WindSpeed ("Viento: velocidad", Range(0, 6)) = 1.5
        [HideInInspector] _MotionAmount ("Movimiento", Float) = 0
        [HideInInspector] _MaxDisp ("Desplazamiento maximo", Float) = 0.08
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+2" }

        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }
            Cull Off
            AlphaToMask On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            half4 _RootColor;
            half4 _TipColor;
            half _ColorVariation;
            half _Width;
            half _Taper;
            half _SpecStrength;
            half _SpecPower;
            half _Backlight;
            half _WindStrength;
            half _WindSpeed;
            half _MotionAmount;
            half _MaxDisp;
            float4x4 _FurLagO2W;

            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
                float2 rnd     : TEXCOORD1;
                float2 clump   : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 rnd        : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 tangentWS  : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float  clump      : TEXCOORD5;
                SHADOW_COORDS(6)
            };

            v2f vert(appdata v)
            {
                v2f o;

                float t = v.uv.y;
                float w = t * t;

                float3 posNow = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 posWS = posNow;

                if (_MotionAmount > 0.0001)
                {
                    float3 posLag = mul(_FurLagO2W, float4(v.vertex.xyz, 1.0)).xyz;
                    float3 d = (posLag - posNow) * (_MotionAmount * w * lerp(0.7, 1.3, v.rnd.y));
                    float lenD = length(d);
                    float maxD = _MaxDisp * w;
                    d *= min(1.0, maxD / max(lenD, 1e-5));
                    posWS += d;
                }

                float phase = _Time.y * _WindSpeed + dot(posNow, float3(2.1, 1.7, 2.9)) + v.rnd.x * 6.2831;
                float3 wind = float3(sin(phase), 0.35 * sin(phase * 1.7 + 1.3), cos(phase * 0.8 + 0.7));
                posWS += wind * (_WindStrength * w);

                float3 tanWS = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));

                float3 viewDir = _WorldSpaceCameraPos - posWS;
                float3 side = normalize(cross(tanWS, viewDir));
                float width = _Width * pow(saturate(1.0 - t), _Taper);
                posWS += side * (v.uv.x * width);

                o.positionWS = posWS;
                o.pos = mul(UNITY_MATRIX_VP, float4(posWS, 1.0));
                o.normalWS = UnityObjectToWorldNormal(v.tangent.xyz);
                o.tangentWS = tanWS;
                o.uv = v.uv;
                o.rnd = v.rnd;
                o.clump = v.clump.x;
                TRANSFER_SHADOW(o);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float t = i.uv.y;

                half3 baseCol = lerp(_RootColor.rgb, _TipColor.rgb, pow(t, 0.9));
                baseCol *= lerp(1.0 - _ColorVariation, 1.0 + _ColorVariation, i.rnd.x);
                baseCol *= lerp(0.85, 1.1, i.clump);

                half ao = lerp(0.4, 1.0, saturate(t * 1.3)) * lerp(0.85, 1.0, i.clump);

                float3 N = normalize(i.normalWS);
                float3 T = normalize(i.tangentWS);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 Ldir = normalize(_WorldSpaceLightPos0.xyz);
                half3 Lcol = _LightColor0.rgb;
                half shadow = SHADOW_ATTENUATION(i);

                half ndl = dot(N, Ldir) * 0.5 + 0.5;
                half3 diffuse = baseCol * (Lcol * ndl * shadow + ShadeSH9(float4(N, 1.0)));

                float3 H = normalize(Ldir + V);
                float shift = (i.rnd.x - 0.5) * 0.2;
                float3 T1 = normalize(T + N * (shift - 0.05));
                float3 T2 = normalize(T + N * (shift + 0.25));
                float TH1 = dot(T1, H);
                float TH2 = dot(T2, H);
                float s1 = sqrt(saturate(1.0 - TH1 * TH1));
                float s2 = sqrt(saturate(1.0 - TH2 * TH2));
                half facing = saturate(dot(N, Ldir) + 0.3);
                half3 spec = (pow(s1, _SpecPower) * half3(1, 1, 1)
                            + pow(s2, _SpecPower * 0.4) * baseCol * 0.8) * _SpecStrength * facing;

                half back = pow(saturate(dot(V, -Ldir)), 3.0) * _Backlight * t;

                half3 col = diffuse * ao
                          + Lcol * spec * shadow
                          + _TipColor.rgb * Lcol * back * shadow;

                half alpha = 1.0 - smoothstep(0.7, 1.0, abs(i.uv.x));

                return half4(col, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
