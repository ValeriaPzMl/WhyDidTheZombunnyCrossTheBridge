using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class ShowcaseBuilder
{
    const string ScenePath = "Assets/Scenes/ShaderShowcase.unity";
    const string Folder = "Assets/Showcase";
    const string ZombunnyModelPath = "Assets/Zombunny/Zombunny.fbx";
    const string SkyPath = "Assets/AllSkyFree/Cartoon Base BlueSky/Day_BlueSky_Nothing.mat";
    const string GrassLayerPath = "Assets/MapMagic/Demo/TerrainLayers/GrassGreen.terrainlayer";
    const string StoneMatPath = "Assets/shaders/tesselation/StonePathMat.mat";
    const string OceanMatPath = "Assets/shaders/water/OceanMat.mat";
    const string StrandMatPath = "Assets/bunny/materials/strand.mat";

    static readonly Vector3 TessPos = new Vector3(0f, 0f, 0f);
    static readonly Vector3 WaterPos = new Vector3(40f, 0f, 0f);
    static readonly Vector3 FurPos = new Vector3(80f, 0f, 0f);
    static readonly Vector3 HolePos = new Vector3(120f, 0f, 0f);
    static readonly Vector3 CloudPos = new Vector3(1000f, 0f, 0f);

    [MenuItem("ACG/Crear escena de showcase (un Zombunny por shader)")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ZombunnyModelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog("Falta Zombunny", "No se encontro " + ZombunnyModelPath, "OK");
            return;
        }

        EnsureFolder();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sun = new GameObject("Sol").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky != null) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.sun = sun;

        var root = new GameObject("Showcase");
        var grass = GrassMaterial();
        var cams = new Camera[5];

        cams[0] = BuildTessellation(root.transform, model, grass);
        cams[1] = BuildWater(root.transform, model, grass);
        cams[2] = BuildFur(root.transform, model, grass);
        cams[3] = BuildClouds(root.transform, model);
        cams[4] = BuildBlackHole(root.transform, model, grass);

        root.AddComponent<ShowcaseCameras>().cameras = cams;
        for (int i = 1; i < cams.Length; i++) cams[i].gameObject.SetActive(false);

        DynamicGI.UpdateEnvironment();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = root;
        EditorUtility.DisplayDialog("Showcase listo",
            "Escena guardada en " + ScenePath + ".\n\nEn Play: teclas 1-5 o flechas para cambiar de camara, P para guardar una captura en la carpeta Capturas.",
            "OK");
    }

    static Camera BuildTessellation(Transform parent, GameObject model, Material grass)
    {
        var station = Station(parent, "1 Tessellation", TessPos);
        var stone = AssetDatabase.LoadAssetAtPath<Material>(StoneMatPath);
        Ground(station, Vector3.zero, 6f, stone != null ? stone : grass);

        var mat = CopyMaterial(stone, "ZombunnyStoneMat");
        if (mat != null)
        {
            mat.SetFloat("_Displacement", 0.03f);
            mat.SetFloat("_Tiling", 3f);
            mat.SetFloat("_Tess", 32f);
            mat.SetFloat("_MinDist", 1f);
            mat.SetFloat("_MaxDist", 10f);
        }

        var zb = Zombunny(station, model, Vector3.zero, mat);
        return Cam(station, "Cam_1_Tessellation", zb, new Vector3(1.2f, 1.1f, -3f), 0.6f, 40f);
    }

    static Camera BuildWater(Transform parent, GameObject model, Material grass)
    {
        var station = Station(parent, "2 Water", WaterPos);
        Ground(station, new Vector3(0f, -1.4f, 0f), 12f, grass);

        var water = new GameObject("Estanque");
        water.transform.SetParent(station, false);
        var ocean = water.AddComponent<OceanMesh>();
        ocean.size = 18f;
        ocean.resolution = 180;
        ocean.boundsPadding = 2f;

        var mat = CopyMaterial(AssetDatabase.LoadAssetAtPath<Material>(OceanMatPath), "PondWaterMat");
        if (mat != null)
        {
            mat.SetVector("_WaveA", new Vector4(1f, 0.3f, 0.12f, 3f));
            mat.SetVector("_WaveB", new Vector4(-0.5f, 1f, 0.1f, 2f));
            mat.SetVector("_WaveC", new Vector4(0.3f, -1f, 0.08f, 1.2f));
            mat.SetFloat("_DepthMax", 1.5f);
            mat.SetFloat("_FoamHeight", 0.08f);
            water.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        water.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var zb = Zombunny(station, model, new Vector3(0f, -0.6f, 0f), null);
        return Cam(station, "Cam_2_Water", zb, new Vector3(1.5f, 1.1f, -3.5f), 0.4f, 45f);
    }

    static Camera BuildFur(Transform parent, GameObject model, Material grass)
    {
        var station = Station(parent, "3 Fur", FurPos);
        Ground(station, Vector3.zero, 6f, grass);

        var zb = Zombunny(station, model, Vector3.zero, null);
        var fur = zb.transform.Find("Visual").gameObject.AddComponent<FurStrands>();
        fur.strandMaterial = AssetDatabase.LoadAssetAtPath<Material>(StrandMatPath);
        fur.length = 0.08f;
        fur.enabled = false;
        fur.enabled = true;

        return Cam(station, "Cam_3_Fur", zb, new Vector3(1f, 1f, -2.6f), 0.6f, 35f);
    }

    static Camera BuildClouds(Transform parent, GameObject model)
    {
        var station = Station(parent, "4 Volumetric Clouds", CloudPos);

        var clouds = GameObject.CreatePrimitive(PrimitiveType.Cube);
        clouds.name = "Nubes";
        Object.DestroyImmediate(clouds.GetComponent<Collider>());
        clouds.transform.SetParent(station, false);
        clouds.transform.localScale = new Vector3(400f, 30f, 400f);
        var cmr = clouds.GetComponent<MeshRenderer>();
        cmr.sharedMaterial = ShaderSceneSetup.GetCloudMaterial();
        cmr.shadowCastingMode = ShadowCastingMode.Off;
        cmr.receiveShadows = false;

        var island = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        island.name = "Isla";
        Object.DestroyImmediate(island.GetComponent<Collider>());
        island.transform.SetParent(station, false);
        island.transform.localPosition = new Vector3(0f, 11f, 0f);
        island.transform.localScale = new Vector3(3f, 0.4f, 3f);
        island.GetComponent<MeshRenderer>().sharedMaterial = SimpleMaterial("IslaMat", new Color(0.55f, 0.52f, 0.5f));

        var zb = Zombunny(station, model, new Vector3(0f, 11.4f, 0f), null);
        return Cam(station, "Cam_4_Clouds", zb, new Vector3(2.5f, 2.2f, -6f), 0.3f, 55f);
    }

    static Camera BuildBlackHole(Transform parent, GameObject model, Material grass)
    {
        var station = Station(parent, "5 Black Hole", HolePos);
        Ground(station, Vector3.zero, 30f, grass);

        var hole = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hole.name = "BlackHole";
        Object.DestroyImmediate(hole.GetComponent<Collider>());
        hole.transform.SetParent(station, false);
        hole.transform.localPosition = new Vector3(0f, 4f, 7f);
        hole.transform.localScale = Vector3.one * 8f;
        var hmr = hole.GetComponent<MeshRenderer>();
        hmr.sharedMaterial = ShaderSceneSetup.GetBlackHoleMaterial();
        hmr.shadowCastingMode = ShadowCastingMode.Off;

        var zb = Zombunny(station, model, Vector3.zero, null);
        return Cam(station, "Cam_5_BlackHole", zb, new Vector3(-1.5f, 1.2f, -4f), 1.6f, 55f);
    }

    static Transform Station(Transform parent, string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        return go.transform;
    }

    static GameObject Zombunny(Transform station, GameObject model, Vector3 localPos, Material overrideMat)
    {
        var zb = CinematicBuilder.BuildZombunny(model, station);
        var follower = zb.GetComponent<PathFollower>();
        if (follower != null) Object.DestroyImmediate(follower);
        zb.transform.localPosition = localPos;

        if (overrideMat != null)
        {
            foreach (var r in zb.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = overrideMat;
                r.sharedMaterials = mats;
            }
        }
        return zb;
    }

    static Camera Cam(Transform station, string name, GameObject zb, Vector3 offset, float lookHeight, float fov)
    {
        var go = new GameObject(name);
        go.transform.SetParent(station, false);
        Vector3 target = zb.transform.position + Vector3.up * lookHeight;
        go.transform.position = zb.transform.position + offset;
        go.transform.rotation = Quaternion.LookRotation(target - go.transform.position);

        Vector3 face = go.transform.position - zb.transform.position;
        face.y = 0f;
        if (face.sqrMagnitude > 0.0001f) zb.transform.rotation = Quaternion.LookRotation(face);

        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1000f;
        go.AddComponent<EnableDepthTexture>();
        go.AddComponent<AudioListener>();
        return cam;
    }

    static void Ground(Transform station, Vector3 localPos, float radius, Material mat)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
        g.name = "Suelo";
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(station, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = Vector3.one * (radius * 2f / 10f);
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Material GrassMaterial()
    {
        string path = Folder + "/PastoMat.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Standard"));
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GrassLayerPath);
        if (layer != null && layer.diffuseTexture != null)
        {
            mat.mainTexture = layer.diffuseTexture;
            mat.mainTextureScale = new Vector2(6f, 6f);
        }
        else
        {
            mat.color = new Color(0.3f, 0.5f, 0.2f);
        }
        mat.SetFloat("_Glossiness", 0.1f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material SimpleMaterial(string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Standard")) { color = color };
        mat.SetFloat("_Glossiness", 0.15f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material CopyMaterial(Material source, string name)
    {
        if (source == null) return null;
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(source);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "Showcase");
    }
}
