Shader "ACG/BlackHoleShader"
{
    Properties
    {
        [Header(Black Hole)]
        _EventHorizon ("Event Horizon (fraction of radius)", Range(0.02, 0.4)) = 0.12
        _LensStrength ("Lens Strength", Range(0, 4)) = 1.5
        _Steps ("Ray Steps", Range(32, 256)) = 160

        [Header(Accretion Disk)]
        _DiskInner ("Disk Inner Radius", Range(0.05, 1)) = 0.3
        _DiskOuter ("Disk Outer Radius", Range(0.1, 1)) = 0.85
        _DiskSpeed ("Disk Rotation Speed", Range(0, 5)) = 0.6
        _NoiseScale ("Disk Noise Scale", Range(1, 30)) = 8
        _DiskDensity ("Disk Density", Range(0, 3)) = 1.4
        _DiskBrightness ("Disk Brightness", Range(0, 10)) = 2.5
        _TempInner ("Inner Temperature (K)", Range(1000, 40000)) = 12000
        _TempOuter ("Outer Temperature (K)", Range(1000, 40000)) = 2500
        _Doppler ("Doppler Beaming", Range(0, 2)) = 0.7
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True" }

        GrabPass { "_BHGrab" }

        Pass
        {
            ZWrite On
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            float _EventHorizon, _LensStrength, _Steps;
            float _DiskInner, _DiskOuter, _DiskSpeed, _NoiseScale, _DiskDensity, _DiskBrightness;
            float _TempInner, _TempOuter, _Doppler;
            sampler2D _BHGrab;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0;
                float amp = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    sum += ValueNoise(p) * amp;
                    p *= 2.03;
                    amp *= 0.5;
                }
                return sum;
            }

            float3 Blackbody(float kelvin)
            {
                float t = kelvin / 100;
                float r = t <= 66 ? 1 : saturate(1.29293618606 * pow(t - 60, -0.1332047592));
                float g = t <= 66 ? saturate(0.390081578769 * log(t) - 0.631841443788)
                                  : saturate(1.12989086089 * pow(t - 60, -0.0755148492));
                float b = t >= 66 ? 1 : (t <= 19 ? 0 : saturate(0.543206789110 * log(t - 10) - 1.19625408914));
                return float3(r, g, b);
            }

            float4 DiskColor(float3 rel, float3 n, float R, float3 rd)
            {
                float3 radial = rel - n * dot(rel, n);
                float rr = length(radial) / R;
                if (rr < _DiskInner || rr > _DiskOuter) return 0;

                float3 axisU = normalize(abs(n.y) < 0.99 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0)));
                float3 axisV = cross(n, axisU);
                float angle = atan2(dot(radial, axisV), dot(radial, axisU));

                float omega = _DiskSpeed / pow(rr, 1.5);
                float rotated = angle - _Time.y * omega;
                float2 q = float2(cos(rotated), sin(rotated)) * rr;

                float t01 = saturate((rr - _DiskInner) / (_DiskOuter - _DiskInner));
                float noise = Fbm(q * _NoiseScale + float2(rr * 20, 0));
                float edge = smoothstep(0, 0.08, t01) * smoothstep(1, 0.5, t01);
                float density = saturate(noise * edge * _DiskDensity);

                float kelvin = lerp(_TempInner, _TempOuter, pow(t01, 0.75));
                float3 color = Blackbody(kelvin);

                float3 velocity = normalize(cross(n, radial));
                float doppler = max(1 + _Doppler * dot(velocity, -rd), 0.05);
                float brightness = _DiskBrightness * doppler * doppler * (1 + (1 - t01) * 2);

                return float4(color * brightness, density);
            }

            float3 Background(float3 p, float3 rd)
            {
                float4 clip = mul(UNITY_MATRIX_VP, float4(p + rd * 1000, 1));
                if (clip.w > 0)
                {
                    float4 grab = ComputeGrabScreenPos(clip);
                    float2 uv = grab.xy / grab.w;
                    if (all(uv >= 0) && all(uv <= 1)) return tex2D(_BHGrab, uv).rgb;
                }
                half4 sky = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, rd);
                return DecodeHDR(sky, unity_SpecCube0_HDR);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 C = unity_ObjectToWorld._m03_m13_m23;
                float R = 0.5 * length(unity_ObjectToWorld._m00_m10_m20);
                float Rs = _EventHorizon * R;
                float3 n = normalize(unity_ObjectToWorld._m01_m11_m21);

                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.worldPos - ro);
                float3 p = i.worldPos;

                float3 col = 0;
                float alpha = 0;
                bool absorbed = false;
                float prevSide = dot(p - C, n);

                for (int s = 0; s < 256; s++)
                {
                    if (s >= _Steps) break;

                    float3 toC = C - p;
                    float r = length(toC);
                    if (r < Rs)
                    {
                        absorbed = true;
                        break;
                    }

                    float dt = clamp((r - Rs) * 0.1, R * 0.004, R * 0.04);
                    rd = normalize(rd + (toC / r) * (_LensStrength * Rs / (r * r)) * dt);
                    float3 next = p + rd * dt;

                    float side = dot(next - C, n);
                    if (side * prevSide < 0 && alpha < 0.99)
                    {
                        float k = prevSide / (prevSide - side);
                        float4 disk = DiskColor(lerp(p, next, k) - C, n, R, rd);
                        col += (1 - alpha) * disk.rgb * disk.a;
                        alpha += (1 - alpha) * disk.a;
                    }
                    prevSide = side;
                    p = next;

                    float3 fromC = p - C;
                    if (dot(fromC, fromC) > R * R * 1.0001 && dot(rd, fromC) > 0) break;
                }

                if (!absorbed) col += (1 - alpha) * Background(p, rd);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
