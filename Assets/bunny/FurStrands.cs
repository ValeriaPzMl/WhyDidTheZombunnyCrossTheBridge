using System.Collections.Generic;
using UnityEngine;

// Genera hebras de pelo (cintas curvas) sobre la superficie de una malla
// y las mueve con inercia cuando el objeto se desplaza o gira.
[ExecuteAlways]
public class FurStrands : MonoBehaviour
{
    public Material strandMaterial;

    [Header("Cantidad")]
    [Range(1000, 80000)] public int strandCount = 60000;
    [Range(3, 10)] public int segments = 8;

    [Header("Largo")]
    public float length = 0.1f;
    [Range(0f, 1f)] public float lengthVariation = 0.4f;

    [Header("Forma")]
    public float gravity = 0.6f;
    public float clumpSize = 0.1f;
    [Range(0f, 1f)] public float clumpStrength = 0.35f;
    [Range(0f, 1f)] public float curl = 0.3f;
    public int seed = 1;

    [Header("Movimiento (solo en Play)")]
    public bool animate = true;
    [Range(1f, 40f)] public float stiffness = 14f;      // mas alto = pelo mas tieso
    [Range(0.1f, 1f)] public float damping = 0.35f;     // mas bajo = mas rebote
    [Range(0f, 3f)] public float motionAmount = 1.2f;  // cuanto se atrasa el pelo
    [Range(0f, 1f)] public float maxStretch = 0.8f;    // limite del desplazamiento (fraccion del largo)

    const string HolderName = "FurStrands (generado)";
    static readonly int LagId = Shader.PropertyToID("_FurLagO2W");
    static readonly int MotionId = Shader.PropertyToID("_MotionAmount");
    static readonly int MaxDispId = Shader.PropertyToID("_MaxDisp");

    Mesh mesh;
    MeshRenderer holderMR;
    MaterialPropertyBlock mpb;
    Transform motionT;
    Vector3 lagPos, lagVel;
    Quaternion lagRot = Quaternion.identity;

    void OnEnable() { Rebuild(); }
    void OnDisable() { Cleanup(); }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall -= DelayedRebuild;
        UnityEditor.EditorApplication.delayCall += DelayedRebuild;
    }

    void DelayedRebuild()
    {
        if (this != null && isActiveAndEnabled) Rebuild();
    }
#endif

    void Cleanup()
    {
        var found = new List<GameObject>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t != null && t.name == HolderName) found.Add(t.gameObject);

        foreach (var go in found)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        if (mesh != null)
        {
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            mesh = null;
        }
        holderMR = null;
    }

    Mesh GetSourceMesh(out Transform t)
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.gameObject.name != HolderName && mf.sharedMesh != null)
            {
                t = mf.transform;
                return mf.sharedMesh;
            }
        }
        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null && smr.sharedMesh != null)
        {
            t = smr.transform;
            return smr.sharedMesh;
        }
        t = null;
        return null;
    }

    static float Hash01(int x, int y, int z, int salt)
    {
        unchecked
        {
            int h = x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ salt * 374761393;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    // ---------- Movimiento ----------

    void LateUpdate()
    {
        if (holderMR == null || motionT == null) return;

        if (animate && Application.isPlaying)
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            Vector3 target = motionT.position;

            // Teletransporte: no arrastrar el pelo
            if ((target - lagPos).sqrMagnitude > 25f)
            {
                lagPos = target;
                lagVel = Vector3.zero;
                lagRot = motionT.rotation;
            }

            // Resorte amortiguado (con rebote) para la posicion
            float w0 = stiffness;
            Vector3 acc = (target - lagPos) * (w0 * w0) - lagVel * (2f * damping * w0);
            lagVel += acc * dt;
            lagPos += lagVel * dt;

            // Suavizado exponencial para la rotacion
            lagRot = Quaternion.Slerp(lagRot, motionT.rotation, 1f - Mathf.Exp(-w0 * 0.6f * dt));
        }
        else
        {
            lagPos = motionT.position;
            lagRot = motionT.rotation;
            lagVel = Vector3.zero;
        }

        ApplyMotion();
    }

    void ApplyMotion()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        holderMR.GetPropertyBlock(mpb);
        mpb.SetMatrix(LagId, Matrix4x4.TRS(lagPos, lagRot, motionT.lossyScale));
        mpb.SetFloat(MotionId, (animate && Application.isPlaying) ? motionAmount : 0f);
        mpb.SetFloat(MaxDispId, length * maxStretch);
        holderMR.SetPropertyBlock(mpb);
    }

    // ---------- Generacion ----------

    [ContextMenu("Regenerar")]
    public void Rebuild()
    {
        Cleanup();
        if (strandMaterial == null) return;

        Transform srcT;
        Mesh src = GetSourceMesh(out srcT);
        if (src == null)
        {
            Debug.LogWarning("FurStrands: no encontre ninguna malla en este objeto ni en sus hijos.");
            return;
        }
        if (!src.isReadable)
        {
            Debug.LogError("FurStrands: la malla no es legible. Selecciona el modelo en Project, " +
                           "pestana Model, activa 'Read/Write' y pulsa Apply.");
            return;
        }

        Vector3[] verts = src.vertices;
        Vector3[] norms = src.normals;
        int[] tris = src.triangles;
        int triCount = tris.Length / 3;
        if (triCount == 0) return;
        bool hasNormals = norms != null && norms.Length == verts.Length;

        float[] cdf = new float[triCount];
        float total = 0f;
        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = verts[tris[i * 3]], b = verts[tris[i * 3 + 1]], c = verts[tris[i * 3 + 2]];
            total += 0.5f * Vector3.Cross(b - a, c - a).magnitude;
            cdf[i] = total;
        }

        var rng = new System.Random(seed);
        Vector3 gravLocal = srcT.InverseTransformDirection(Vector3.down) * gravity;

        int vertsPerStrand = (segments + 1) * 2;
        int vCount = strandCount * vertsPerStrand;
        var pos = new Vector3[vCount];
        var dirs = new Vector3[vCount];
        var rootN = new Vector4[vCount];
        var uv0 = new Vector2[vCount];
        var uv1 = new Vector2[vCount];
        var uv2 = new Vector2[vCount];
        var idx = new int[strandCount * segments * 6];

        for (int s = 0; s < strandCount; s++)
        {
            float r = (float)(rng.NextDouble() * total);
            int lo = 0, hi = triCount - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cdf[mid] < r) lo = mid + 1; else hi = mid;
            }
            int ia = tris[lo * 3], ib = tris[lo * 3 + 1], ic = tris[lo * 3 + 2];
            float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            float w = 1f - u - v;

            Vector3 root = verts[ia] * w + verts[ib] * u + verts[ic] * v;
            Vector3 n = hasNormals
                ? norms[ia] * w + norms[ib] * u + norms[ic] * v
                : Vector3.Cross(verts[ib] - verts[ia], verts[ic] - verts[ia]);
            n.Normalize();

            int cx = Mathf.FloorToInt(root.x / clumpSize);
            int cy = Mathf.FloorToInt(root.y / clumpSize);
            int cz = Mathf.FloorToInt(root.z / clumpSize);
            Vector3 clumpRand = new Vector3(
                Hash01(cx, cy, cz, 1), Hash01(cx, cy, cz, 2), Hash01(cx, cy, cz, 3)) * 2f - Vector3.one;
            float clumpLen = Hash01(cx, cy, cz, 4);
            Vector3 clumpDir = clumpRand - n * Vector3.Dot(clumpRand, n);
            clumpDir = (clumpDir.sqrMagnitude > 1e-6f ? clumpDir.normalized : Vector3.zero) * clumpStrength;
            float lenMul = Mathf.Lerp(1f, 0.5f + clumpLen * 1.1f, clumpStrength);

            float rl = (float)rng.NextDouble();
            float rc = (float)rng.NextDouble();
            float L = length * lenMul * (1f + (rl * 2f - 1f) * lengthVariation * 0.5f);

            Vector3 rv = new Vector3(
                (float)rng.NextDouble() * 2f - 1f,
                (float)rng.NextDouble() * 2f - 1f,
                (float)rng.NextDouble() * 2f - 1f);
            rv -= n * Vector3.Dot(rv, n);
            rv = rv.sqrMagnitude > 1e-6f ? rv.normalized : Vector3.zero;

            Vector3 bend = gravLocal + clumpDir + rv * curl;

            int baseIdx = s * vertsPerStrand;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 p = root + n * (L * t) + bend * (L * t * t);
                Vector3 d = (n * L + bend * (2f * L * t)).normalized;

                int k = baseIdx + i * 2;
                for (int j = 0; j < 2; j++)
                {
                    float side = j == 0 ? -1f : 1f;
                    pos[k + j] = p;
                    dirs[k + j] = d;
                    rootN[k + j] = new Vector4(n.x, n.y, n.z, 0f);
                    uv0[k + j] = new Vector2(side, t);
                    uv1[k + j] = new Vector2(rc, rl);
                    uv2[k + j] = new Vector2(clumpLen, 0f);
                }
            }

            int ii = s * segments * 6;
            for (int i = 0; i < segments; i++)
            {
                int a = baseIdx + i * 2;
                idx[ii++] = a;     idx[ii++] = a + 2; idx[ii++] = a + 1;
                idx[ii++] = a + 1; idx[ii++] = a + 2; idx[ii++] = a + 3;
            }
        }

        mesh = new Mesh();
        mesh.name = "FurStrandsMesh";
        mesh.hideFlags = HideFlags.DontSave;
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = pos;
        mesh.normals = dirs;       // direccion de la hebra
        mesh.tangents = rootN;     // normal de la superficie en la raiz
        mesh.uv = uv0;             // x = lado (-1/+1), y = posicion en la hebra
        mesh.uv2 = uv1;            // aleatorios por hebra
        mesh.SetUVs(2, uv2);       // aleatorio por mechon
        mesh.triangles = idx;
        mesh.RecalculateBounds();
        Bounds bnd = mesh.bounds;
        bnd.Expand(0.3f);          // margen para el movimiento
        mesh.bounds = bnd;

        var holder = new GameObject(HolderName);
        holder.hideFlags = HideFlags.DontSave;
        holder.layer = gameObject.layer;
        holder.transform.SetParent(srcT, false);
        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        holderMR = holder.AddComponent<MeshRenderer>();
        holderMR.sharedMaterial = strandMaterial;
        holderMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        motionT = srcT;
        lagPos = srcT.position;
        lagRot = srcT.rotation;
        lagVel = Vector3.zero;
        ApplyMotion();
    }
}
