using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class ShaderSceneSetup
{
    const string CloudShader = "ACG/VolumetricCloudShader";
    const string BlackHoleShader = "ACG/BlackHoleShader";
    const string CloudMatPath = "Assets/shaders/clouds/CloudMat.mat";
    const string BlackHoleMatPath = "Assets/shaders/blackhole/BlackHoleMat.mat";
    const string OceanMatPath = "Assets/shaders/water/OceanMat.mat";

    public const float HoleHeight = 9f;
    public const float HoleScale = 10f;
    public const float RiverCamHeight = 3f;
    public static readonly Vector3 FinalCamOffset = new Vector3(-4f, 0.6f, 0.3f);
    public static readonly Vector3 FinalLookOffset = new Vector3(0f, 1.6f, 0f);
    public const float FinalFov = 50f;

    [MenuItem("ACG/Aplicar nubes, hoyo negro y ajustes")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("Aplicar shaders y ajustes",
                "Se van a:\n- crear las nubes (objeto 'Nubes')\n- poner el Black Hole Shader al hoyo negro, subirlo y agrandarlo\n" +
                "- subir la camara del rio\n- reacomodar la camara final\n- hacer mas chicas las olas del OceanMat\n\n" +
                "Lo demas de la escena no se toca. ¿Continuar?", "Si", "Cancelar"))
            return;

        SetupClouds();
        SetupBlackHole();
        RaiseRiverCamera();
        SetupFinalCamera();
        CalmWaves();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Nubes, hoyo negro y ajustes aplicados.");
    }

    public static Material GetCloudMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(CloudMatPath);
        if (mat != null) return mat;

        var shader = Shader.Find(CloudShader);
        if (shader == null) return null;
        var noise = AssetDatabase.LoadAssetAtPath<Texture3D>(CloudNoiseBaker.NoisePath);
        if (noise == null) noise = CloudNoiseBaker.Bake();

        mat = new Material(shader);
        mat.SetTexture("_NoiseTex", noise);
        AssetDatabase.CreateAsset(mat, CloudMatPath);
        return mat;
    }

    public static Material GetBlackHoleMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BlackHoleMatPath);
        if (mat != null) return mat;

        var shader = Shader.Find(BlackHoleShader);
        if (shader == null) return null;
        mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, BlackHoleMatPath);
        return mat;
    }

    static void SetupClouds()
    {
        var mat = GetCloudMaterial();
        if (mat == null)
        {
            Debug.LogWarning("No se encontro el shader " + CloudShader);
            return;
        }

        var old = GameObject.Find("Nubes");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var clouds = GameObject.CreatePrimitive(PrimitiveType.Cube);
        clouds.name = "Nubes";
        Object.DestroyImmediate(clouds.GetComponent<Collider>());
        Undo.RegisterCreatedObjectUndo(clouds, "Crear nubes");

        var scenery = GameObject.Find("Escenario");
        if (scenery != null) clouds.transform.SetParent(scenery.transform, false);
        clouds.transform.position = new Vector3(0f, 60f, 0f);
        clouds.transform.localScale = new Vector3(400f, 30f, 400f);

        var mr = clouds.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    static void SetupBlackHole()
    {
        var hole = GameObject.Find("Cinematica/BlackHole");
        if (hole == null)
        {
            Debug.LogWarning("No se encontro Cinematica/BlackHole. Corre primero ACG > Crear cinematica.");
            return;
        }

        Undo.RecordObject(hole.transform, "Ajustar hoyo negro");
        var pos = hole.transform.position;
        pos.y = BridgeSceneBuilder.GroundLevel + HoleHeight;
        hole.transform.position = pos;
        hole.transform.localScale = Vector3.one * HoleScale;

        var mat = GetBlackHoleMaterial();
        var mr = hole.GetComponent<MeshRenderer>();
        if (mat != null)
        {
            Undo.RecordObject(mr, "Material hoyo negro");
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        var pull = hole.GetComponent<BlackHolePull>();
        if (pull != null)
        {
            Undo.RecordObject(pull, "Ajustar jalon");
            pull.selfRotation = Vector3.zero;
            pull.liftHeight = 2.5f;
        }
    }

    static void RaiseRiverCamera()
    {
        float waterLevel = BridgeSceneBuilder.GroundLevel - BridgeSceneBuilder.WaterBelowGround;
        foreach (var name in new[] { "Cam6_RioInicio", "Cam6_RioFin" })
        {
            var anchor = GameObject.Find("Cinematica/CameraAnchors/" + name);
            if (anchor == null) continue;
            Undo.RecordObject(anchor.transform, "Subir camara del rio");
            var p = anchor.transform.position;
            p.y = waterLevel + RiverCamHeight;
            anchor.transform.position = p;
        }
    }

    static void SetupFinalCamera()
    {
        var stop = GameObject.Find("Cinematica/Waypoints/W10_Alto");
        var finalCam = GameObject.Find("Cinematica/CameraAnchors/Cam9_Final");
        var zombunny = GameObject.Find("Cinematica/Zombunny");
        var cam = Camera.main;
        if (stop == null || finalCam == null || zombunny == null || cam == null) return;

        Undo.RecordObject(finalCam.transform, "Camara final");
        finalCam.transform.position = stop.transform.position + FinalCamOffset;

        var look = zombunny.transform.Find("Cam_MiraFinal");
        if (look == null)
        {
            var go = new GameObject("Cam_MiraFinal");
            Undo.RegisterCreatedObjectUndo(go, "Camara final");
            go.transform.SetParent(zombunny.transform, false);
            look = go.transform;
        }
        look.localPosition = FinalLookOffset;

        var director = cam.GetComponent<CinematicDirector>();
        if (director == null) return;
        Undo.RecordObject(director, "Camara final");
        foreach (var shot in director.shots)
        {
            if (shot.from != finalCam.transform) continue;
            shot.lookAt = look;
            shot.fov = FinalFov;
        }
    }

    static void CalmWaves()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(OceanMatPath);
        if (mat == null) return;

        Undo.RecordObject(mat, "Olas mas chicas");
        mat.SetVector("_WaveA", new Vector4(0.2f, 1f, 0.1f, 10f));
        mat.SetVector("_WaveB", new Vector4(-0.4f, 1f, 0.08f, 6f));
        mat.SetVector("_WaveC", new Vector4(0.6f, 1f, 0.06f, 3.5f));
        mat.SetFloat("_FoamHeight", 0.15f);
        mat.SetFloat("_FoamSoftness", 0.1f);
        EditorUtility.SetDirty(mat);
    }
}
