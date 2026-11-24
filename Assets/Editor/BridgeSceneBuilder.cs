using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BridgeSceneBuilder
{
    const string RootName = "Escenario";
    const string OutDir = "Assets/Scenes/Generated";
    const string BridgePrefabPath = "Assets/Blue Polygon/Rope Bridge 3D/Prefab/rope_bridge.prefab";
    const string StoneMatPath = "Assets/shaders/tesselation/StonePathMat.mat";
    const string OceanMatPath = "Assets/shaders/water/OceanMat.mat";
    const string LayersDir = "Assets/MapMagic/Demo/TerrainLayers/";
    const string TreesDir = "Assets/MapMagic/Demo/Trees";

    const float TerrainSize = 200f;
    const float TerrainHeight = 40f;
    const int HeightRes = 513;
    const int SplatRes = 512;
    public const float GroundLevel = 10f;
    public const float WaterBelowGround = 2.5f;
    const float RiverDepth = 3f;
    const float PathWidth = 3.5f;
    const float HillHeight = 9f;
    const int TreeCount = 300;
    const int Seed = 1234;

    static float halfWater;
    static float bankEnd;
    static float waterLevel;
    static float noiseOffset;

    [MenuItem("ACG/Construir escenario del puente")]
    public static void Build()
    {
        if (!EditorUtility.DisplayDialog("Construir escenario",
                "Se va a (re)crear el objeto '" + RootName + "' con terreno, rio, caminos y puente, " +
                "y se acomodaran el Ocean y la Main Camera.\n\n¿Continuar?", "Si", "Cancelar"))
            return;

        var bridgeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BridgePrefabPath);
        if (bridgeAsset == null)
        {
            EditorUtility.DisplayDialog("Falta el puente", "No se encontro " + BridgePrefabPath, "OK");
            return;
        }

        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Construir escenario");

        noiseOffset = Seed * 0.137f;
        waterLevel = GroundLevel - WaterBelowGround;

        Bounds bridgeBounds = PlaceBridge(bridgeAsset, root.transform);
        float bridgeLength = bridgeBounds.size.x;
        halfWater = bridgeLength * 0.28f;
        bankEnd = Mathf.Max(halfWater + 2f, bridgeLength * 0.5f - 1.2f);

        EnsureFolder();
        BuildTerrain(root.transform);

        float pathEnd = TerrainSize * 0.5f - 2f;
        BuildPath("Camino_Este", bridgeBounds.max.x - 1f, pathEnd, root.transform);
        BuildPath("Camino_Oeste", -pathEnd, bridgeBounds.min.x + 1f, root.transform);

        SetupOcean();
        SetupReflectionProbe(root.transform);
        SetupCamera(bridgeBounds.center);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"Escenario listo. Puente de {bridgeLength:F1} m, rio de {halfWater * 2f:F1} m de ancho, agua en y = {waterLevel}.");
    }

    static Bounds PlaceBridge(GameObject prefab, Transform parent)
    {
        var bridge = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        bridge.name = "Puente";
        bridge.transform.SetParent(parent, false);
        bridge.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Bounds b = GetBounds(bridge);
        if (b.size.z > b.size.x)
        {
            bridge.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            b = GetBounds(bridge);
        }

        bridge.transform.position = new Vector3(-b.center.x, GroundLevel, -b.center.z);
        return GetBounds(bridge);
    }

    public static Bounds GetBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    static float Smooth(float a, float b, float v)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));
    }

    static float RiverX(float z)
    {
        return 8f * Mathf.Sin(z * 0.03f) * Smooth(15f, 50f, Mathf.Abs(z));
    }

    static float HeightAt(float x, float z)
    {
        float d = Mathf.Abs(x - RiverX(z));
        float az = Mathf.Abs(z);
        float y;

        if (d < halfWater)
        {
            float t = d / halfWater;
            y = waterLevel - RiverDepth * (1f - t * t);
        }
        else if (d < bankEnd)
        {
            y = Mathf.Lerp(waterLevel, GroundLevel, Smooth(halfWater, bankEnd, d));
        }
        else
        {
            y = GroundLevel;
        }

        float pathMask = Smooth(PathWidth * 0.5f + 2f, PathWidth * 0.5f + 18f, az);
        float riverMask = Smooth(bankEnd + 3f, bankEnd + 30f, d);
        float hills = Mathf.PerlinNoise(x * 0.015f + noiseOffset, z * 0.015f + noiseOffset) * HillHeight;
        y += hills * riverMask * pathMask;

        float detailMask = Smooth(bankEnd, bankEnd + 3f, d) * Smooth(PathWidth * 0.5f + 0.5f, PathWidth * 0.5f + 3f, az);
        float detail = (Mathf.PerlinNoise(x * 0.12f + noiseOffset * 2f, z * 0.12f + noiseOffset * 2f) - 0.5f) * 0.6f;
        y += detail * detailMask;

        return y;
    }

    static void BuildTerrain(Transform parent)
    {
        string path = OutDir + "/RiverTerrain.asset";
        AssetDatabase.DeleteAsset(path);

        var td = new TerrainData();
        td.heightmapResolution = HeightRes;
        td.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
        AssetDatabase.CreateAsset(td, path);

        float half = TerrainSize * 0.5f;
        var heights = new float[HeightRes, HeightRes];
        for (int zi = 0; zi < HeightRes; zi++)
        {
            float z = -half + zi / (float)(HeightRes - 1) * TerrainSize;
            for (int xi = 0; xi < HeightRes; xi++)
            {
                float x = -half + xi / (float)(HeightRes - 1) * TerrainSize;
                heights[zi, xi] = Mathf.Clamp01(HeightAt(x, z) / TerrainHeight);
            }
        }
        td.SetHeights(0, 0, heights);

        PaintTerrain(td);
        PlaceTrees(td);

        EditorUtility.SetDirty(td);

        var terrainGO = Terrain.CreateTerrainGameObject(td);
        terrainGO.name = "Terreno";
        terrainGO.transform.SetParent(parent, false);
        terrainGO.transform.position = new Vector3(-half, 0f, -half);
    }

    static void PaintTerrain(TerrainData td)
    {
        string[] names = { "GrassGreen", "GrassYellow", "Dirt", "Sand", "Gravel", "CliffDark" };
        var layers = new List<TerrainLayer>();
        var index = new Dictionary<string, int>();
        foreach (var n in names)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayersDir + n + ".terrainlayer");
            if (layer == null) continue;
            index[n] = layers.Count;
            layers.Add(layer);
        }
        if (layers.Count == 0)
        {
            Debug.LogWarning("No se encontraron TerrainLayers de MapMagic; el terreno queda sin textura.");
            return;
        }

        td.terrainLayers = layers.ToArray();
        td.alphamapResolution = SplatRes;

        int count = layers.Count;
        var maps = new float[SplatRes, SplatRes, count];
        var w = new float[count];
        float half = TerrainSize * 0.5f;

        for (int zi = 0; zi < SplatRes; zi++)
        {
            float nz = (zi + 0.5f) / SplatRes;
            float z = -half + nz * TerrainSize;
            for (int xi = 0; xi < SplatRes; xi++)
            {
                float nx = (xi + 0.5f) / SplatRes;
                float x = -half + nx * TerrainSize;
                float d = Mathf.Abs(x - RiverX(z));
                float az = Mathf.Abs(z);
                float steep = td.GetSteepness(nx, nz);

                System.Array.Clear(w, 0, count);
                w[0] = 1f;

                float yellow = Smooth(0.45f, 0.75f, Mathf.PerlinNoise(x * 0.05f + noiseOffset, z * 0.05f - noiseOffset));
                Paint(w, index, "GrassYellow", yellow);
                Paint(w, index, "Dirt", 1f - Smooth(PathWidth * 0.5f, PathWidth * 0.5f + 2.5f, az));
                Paint(w, index, "Sand", 1f - Smooth(halfWater, bankEnd + 2f, d));
                Paint(w, index, "Gravel", 1f - Smooth(halfWater * 0.6f, halfWater, d));
                Paint(w, index, "CliffDark", Smooth(30f, 45f, steep));

                for (int l = 0; l < count; l++) maps[zi, xi, l] = w[l];
            }
        }
        td.SetAlphamaps(0, 0, maps);
    }

    static void Paint(float[] w, Dictionary<string, int> index, string layer, float amount)
    {
        if (!index.TryGetValue(layer, out int li)) return;
        amount = Mathf.Clamp01(amount);
        for (int i = 0; i < w.Length; i++) w[i] *= 1f - amount;
        w[li] += amount;
    }

    static void PlaceTrees(TerrainData td)
    {
        var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { TreesDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null && (p.GetComponent<LODGroup>() != null || p.GetComponent<MeshRenderer>() != null))
            .ToList();
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("No se encontraron arboles de MapMagic; el terreno queda sin arboles.");
            return;
        }

        td.treePrototypes = prefabs.Select(p => new TreePrototype { prefab = p }).ToArray();

        var rng = new System.Random(Seed);
        var trees = new List<TreeInstance>();
        float half = TerrainSize * 0.5f;
        int attempts = 0;

        while (trees.Count < TreeCount && attempts < TreeCount * 20)
        {
            attempts++;
            float x = (float)(rng.NextDouble() * (TerrainSize - 6f)) - half + 3f;
            float z = (float)(rng.NextDouble() * (TerrainSize - 6f)) - half + 3f;
            if (Mathf.Abs(x - RiverX(z)) < bankEnd + 5f) continue;
            if (Mathf.Abs(z) < PathWidth * 0.5f + 4f) continue;

            float nx = (x + half) / TerrainSize;
            float nz = (z + half) / TerrainSize;
            if (td.GetSteepness(nx, nz) > 30f) continue;

            float scale = 0.8f + (float)rng.NextDouble() * 0.5f;
            trees.Add(new TreeInstance
            {
                position = new Vector3(nx, td.GetInterpolatedHeight(nx, nz) / TerrainHeight, nz),
                prototypeIndex = rng.Next(prefabs.Count),
                widthScale = scale,
                heightScale = scale,
                rotation = (float)(rng.NextDouble() * Mathf.PI * 2f),
                color = Color.white,
                lightmapColor = Color.white
            });
        }
        td.SetTreeInstances(trees.ToArray(), true);
    }

    static void BuildPath(string name, float x0, float x1, Transform parent)
    {
        int segX = Mathf.Max(1, Mathf.CeilToInt(x1 - x0));
        const int segZ = 4;
        int n = segX + 1;
        var verts = new Vector3[n * (segZ + 1)];
        var uvs = new Vector2[verts.Length];
        var normals = new Vector3[verts.Length];
        float y = GroundLevel + 0.02f;

        for (int zi = 0; zi <= segZ; zi++)
        {
            float z = -PathWidth * 0.5f + zi / (float)segZ * PathWidth;
            for (int xi = 0; xi < n; xi++)
            {
                float x = Mathf.Lerp(x0, x1, xi / (float)segX);
                int i = zi * n + xi;
                verts[i] = new Vector3(x, y, z);
                uvs[i] = new Vector2(x / 10f, z / 10f);
                normals[i] = Vector3.up;
            }
        }

        var tris = new int[segX * segZ * 6];
        int t = 0;
        for (int zi = 0; zi < segZ; zi++)
        {
            for (int xi = 0; xi < segX; xi++)
            {
                int i = zi * n + xi;
                tris[t++] = i;
                tris[t++] = i + n;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + n;
                tris[t++] = i + n + 1;
            }
        }

        var mesh = new Mesh { name = name };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        var b = mesh.bounds;
        b.Expand(new Vector3(0f, 2f, 0f));
        mesh.bounds = b;

        string meshPath = OutDir + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(StoneMatPath);
        if (mat != null) mr.sharedMaterial = mat;
        else Debug.LogWarning("No se encontro " + StoneMatPath);
    }

    static void SetupOcean()
    {
        var ocean = Object.FindFirstObjectByType<OceanMesh>();
        if (ocean == null)
        {
            var go = new GameObject("Ocean");
            Undo.RegisterCreatedObjectUndo(go, "Crear Ocean");
            ocean = go.AddComponent<OceanMesh>();
        }

        var mr = ocean.GetComponent<MeshRenderer>();
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(OceanMatPath);

        Undo.RecordObject(ocean.transform, "Acomodar Ocean");
        ocean.transform.SetPositionAndRotation(new Vector3(0f, waterLevel, 0f), Quaternion.identity);
        ocean.transform.localScale = Vector3.one;

        var so = new SerializedObject(ocean);
        so.FindProperty("size").floatValue = TerrainSize;
        so.ApplyModifiedProperties();

        ocean.enabled = false;
        ocean.enabled = true;
    }

    static void SetupReflectionProbe(Transform parent)
    {
        var go = new GameObject("ReflectionProbe");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(0f, waterLevel + 3f, 0f);
        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.size = new Vector3(80f, 30f, 80f);
    }

    static void SetupCamera(Vector3 target)
    {
        var cam = Camera.main;
        if (cam == null) return;

        Undo.RecordObject(cam.transform, "Acomodar camara");
        Undo.RecordObject(cam, "Acomodar camara");
        cam.transform.position = new Vector3(-28f, GroundLevel + 7f, -22f);
        cam.transform.LookAt(target + Vector3.up);
        cam.farClipPlane = 500f;

        if (cam.GetComponent<EnableDepthTexture>() == null)
            Undo.AddComponent<EnableDepthTexture>(cam.gameObject);
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        if (!AssetDatabase.IsValidFolder(OutDir))
            AssetDatabase.CreateFolder("Assets/Scenes", "Generated");
    }
}
