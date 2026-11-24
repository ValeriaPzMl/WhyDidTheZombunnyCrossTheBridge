using UnityEditor;
using UnityEngine;

public static class CloudNoiseBaker
{
    public const string NoisePath = "Assets/shaders/clouds/CloudNoise.asset";
    const int Size = 64;
    const int Seed = 7;

    [MenuItem("ACG/Generar ruido 3D de nubes")]
    public static Texture3D Bake()
    {
        var colors = new Color[Size * Size * Size];

        try
        {
            for (int z = 0; z < Size; z++)
            {
                EditorUtility.DisplayProgressBar("Ruido 3D de nubes", "Worley + Perlin...", z / (float)Size);
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        var p = new Vector3(x, y, z) / Size;

                        float w4 = Worley(p, 4);
                        float w8 = Worley(p, 8);
                        float w16 = Worley(p, 16);
                        float w32 = Worley(p, 32);

                        float perlin = PerlinFbm(p, 4, 4);
                        float worleyFbm = w4 * 0.625f + w8 * 0.25f + w16 * 0.125f;
                        float perlinWorley = Mathf.Clamp01(Remap(perlin, worleyFbm - 1f, 1f, 0f, 1f));

                        colors[x + y * Size + z * Size * Size] = new Color(perlinWorley, w8, w16, w32);
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        var tex = new Texture3D(Size, Size, Size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "CloudNoise"
        };
        tex.SetPixels(colors);
        tex.Apply();

        AssetDatabase.DeleteAsset(NoisePath);
        AssetDatabase.CreateAsset(tex, NoisePath);
        AssetDatabase.SaveAssets();
        return tex;
    }

    static float Remap(float v, float l0, float h0, float l1, float h1)
    {
        return l1 + (v - l0) * (h1 - l1) / (h0 - l0);
    }

    static Vector3 Hash3(int x, int y, int z, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(salt * 2654435761u) ^ (uint)Seed;
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        float a = (h & 0x3FF) / 1023f;
        float b = ((h >> 10) & 0x3FF) / 1023f;
        float c = ((h >> 20) & 0x3FF) / 1023f;
        return new Vector3(a, b, c);
    }

    static int Wrap(int v, int period)
    {
        return ((v % period) + period) % period;
    }

    static float Worley(Vector3 p, int cells)
    {
        Vector3 sp = p * cells;
        int cx = Mathf.FloorToInt(sp.x);
        int cy = Mathf.FloorToInt(sp.y);
        int cz = Mathf.FloorToInt(sp.z);
        float minDist = float.MaxValue;

        for (int oz = -1; oz <= 1; oz++)
        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
        {
            int nx = cx + ox, ny = cy + oy, nz = cz + oz;
            Vector3 feature = new Vector3(nx, ny, nz) + Hash3(Wrap(nx, cells), Wrap(ny, cells), Wrap(nz, cells), cells);
            float d = (feature - sp).sqrMagnitude;
            if (d < minDist) minDist = d;
        }

        return 1f - Mathf.Clamp01(Mathf.Sqrt(minDist));
    }

    static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    static float GradDot(int x, int y, int z, int period, Vector3 f)
    {
        Vector3 g = Hash3(Wrap(x, period), Wrap(y, period), Wrap(z, period), 1000 + period) * 2f - Vector3.one;
        return Vector3.Dot(g.normalized, f);
    }

    static float Perlin(Vector3 p, int period)
    {
        Vector3 sp = p * period;
        int x0 = Mathf.FloorToInt(sp.x);
        int y0 = Mathf.FloorToInt(sp.y);
        int z0 = Mathf.FloorToInt(sp.z);
        Vector3 f = sp - new Vector3(x0, y0, z0);
        float u = Fade(f.x), v = Fade(f.y), w = Fade(f.z);

        float c000 = GradDot(x0, y0, z0, period, f);
        float c100 = GradDot(x0 + 1, y0, z0, period, f - new Vector3(1, 0, 0));
        float c010 = GradDot(x0, y0 + 1, z0, period, f - new Vector3(0, 1, 0));
        float c110 = GradDot(x0 + 1, y0 + 1, z0, period, f - new Vector3(1, 1, 0));
        float c001 = GradDot(x0, y0, z0 + 1, period, f - new Vector3(0, 0, 1));
        float c101 = GradDot(x0 + 1, y0, z0 + 1, period, f - new Vector3(1, 0, 1));
        float c011 = GradDot(x0, y0 + 1, z0 + 1, period, f - new Vector3(0, 1, 1));
        float c111 = GradDot(x0 + 1, y0 + 1, z0 + 1, period, f - new Vector3(1, 1, 1));

        float x00 = Mathf.Lerp(c000, c100, u);
        float x10 = Mathf.Lerp(c010, c110, u);
        float x01 = Mathf.Lerp(c001, c101, u);
        float x11 = Mathf.Lerp(c011, c111, u);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
    }

    static float PerlinFbm(Vector3 p, int basePeriod, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        int period = basePeriod;
        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin(p, period) * amp;
            norm += amp;
            amp *= 0.5f;
            period *= 2;
        }
        return Mathf.Clamp01(sum / norm * 0.5f + 0.5f);
    }
}
