using UnityEngine;
using UnityEngine.Rendering;

// Genera una malla cuadricula con muchos vertices para el OceanShader.
// El Plane de Unity solo tiene 11x11 vertices, muy pocos para que se vean las olas.
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class OceanMesh : MonoBehaviour
{
    [Min(1f)] public float size = 100f;
    [Range(2, 512)] public int resolution = 256;
    [Tooltip("Margen extra de los bounds para que la malla no desaparezca al moverse las olas")]
    public float boundsPadding = 10f;

    Mesh mesh;
    bool dirty = true;

    void OnEnable() { dirty = true; }
    void OnValidate() { dirty = true; }

    void Update()
    {
        if (!dirty) return;
        dirty = false;
        Build();
    }

    void Build()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "OceanGrid", hideFlags = HideFlags.DontSave };
        }
        mesh.Clear();

        int n = resolution + 1;
        mesh.indexFormat = n * n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        var normals = new Vector3[n * n];
        float half = size * 0.5f;

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float u = x / (float)resolution;
                float v = z / (float)resolution;
                verts[i] = new Vector3(u * size - half, 0f, v * size - half);
                uvs[i] = new Vector2(u, v);
                normals[i] = Vector3.up;
            }
        }

        var tris = new int[resolution * resolution * 6];
        int t = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = z * n + x;
                tris[t++] = i;
                tris[t++] = i + n;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + n;
                tris[t++] = i + n + 1;
            }
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        var b = mesh.bounds;
        b.Expand(boundsPadding * 2f);
        mesh.bounds = b;

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    void OnDestroy()
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }
}
