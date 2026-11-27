using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CinematicBuilder
{
    const string RootName = "Cinematica";
    const string ZombunnyModelPath = "Assets/Zombunny/Zombunny.fbx";
    const string ZombunnyTexDir = "Assets/Zombunny/ZombunnyTextures/";
    const string ZombunnyMatPath = "Assets/Zombunny/ZombunnyMat.mat";
    const string BlackHoleMatPath = "Assets/Cinematic/BlackHolePlaceholder.mat";
    const float ZombunnyHeight = 1.2f;
    const float BridgeSag = 0.86f;

    static Terrain terrain;

    [MenuItem("ACG/Crear cinematica")]
    public static void Build()
    {
        var bridge = GameObject.Find("Escenario/Puente");
        if (bridge == null)
        {
            EditorUtility.DisplayDialog("Falta el escenario", "Primero corre ACG > Construir escenario del puente.", "OK");
            return;
        }
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ZombunnyModelPath);
        if (modelAsset == null)
        {
            EditorUtility.DisplayDialog("Falta Zombunny", "No se encontro " + ZombunnyModelPath, "OK");
            return;
        }
        var cam = Camera.main;
        if (cam == null)
        {
            EditorUtility.DisplayDialog("Falta la camara", "No hay una camara con tag MainCamera.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Crear cinematica",
                "Se va a (re)crear el objeto '" + RootName + "' (Zombunny, waypoints, camaras y hoyo negro) " +
                "y se configurara el CinematicDirector de la Main Camera.\n\n¿Continuar?", "Si", "Cancelar"))
            return;

        terrain = Object.FindFirstObjectByType<Terrain>();
        float g = BridgeSceneBuilder.GroundLevel;
        float waterLevel = g - BridgeSceneBuilder.WaterBelowGround;
        Bounds bb = BridgeSceneBuilder.GetBounds(bridge);
        float xW = bb.min.x;
        float xE = bb.max.x;

        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Crear cinematica");

        var zombunny = BuildZombunny(modelAsset, root.transform);
        var follower = zombunny.GetComponent<PathFollower>();
        var lookPoint = Child(zombunny.transform, "LookPoint", new Vector3(0f, ZombunnyHeight * 0.6f, 0f));

        var wp = Child(root.transform, "Waypoints", Vector3.zero);
        AddWaypoint(follower, wp, "W00_Bosque", -65f, 40f, 0f);
        AddWaypoint(follower, wp, "W01_Bosque", -55f, 22f, 7f);
        AddWaypoint(follower, wp, "W02_SaleDelBosque", -44f, 4f, 13f);
        AddWaypoint(follower, wp, "W03_Camino", -36f, 0f, 17f);
        AddWaypoint(follower, wp, "W04_Camino", -20f, 0f, 23f);
        AddWaypoint(follower, wp, "W05_VeElPuente", xW - 3f, 0f, 27f);
        AddWaypoint(follower, wp, "W05b_Pausa", xW - 3f, 0f, 33f);
        AddWaypoint(follower, wp, "W06_InicioPuente", xW + 0.5f, 0f, 35f, g);
        AddWaypoint(follower, wp, "W07_MitadPuente", 0f, 0f, 40f, g - BridgeSag);
        AddWaypoint(follower, wp, "W08_FinPuente", xE - 0.5f, 0f, 45f, g);
        AddWaypoint(follower, wp, "W09_OtroLado", xE + 7f, 0f, 48f);
        AddWaypoint(follower, wp, "W10_Alto", xE + 14f, 0f, 51f);

        var hole = BuildBlackHole(root.transform, new Vector3(xE + 32f, g + ShaderSceneSetup.HoleHeight, 0f), zombunny.transform, follower);

        var anchors = Child(root.transform, "CameraAnchors", Vector3.zero);
        var bridgeFocus = Child(anchors, "BridgeFocus", new Vector3(0f, g + 1.5f, 0f));
        var a1From = Child(anchors, "Cam1_GruaInicio", new Vector3(-45f, GroundAt(-45f, 55f) + 85f, 55f));
        var a1To = Child(anchors, "Cam1_GruaFin", new Vector3(-48f, GroundAt(-48f, 30f) + 4f, 30f));
        var behind = Child(zombunny.transform, "Cam_Detras", new Vector3(0f, 1.8f, -4.5f));
        var side = Child(zombunny.transform, "Cam_Lado", new Vector3(3.5f, 1.1f, 0.5f));
        var shoulderFrom = Child(zombunny.transform, "Cam_HombroInicio", new Vector3(-0.9f, 1.4f, -2.2f));
        var shoulderTo = Child(zombunny.transform, "Cam_HombroFin", new Vector3(-0.6f, 1.3f, -1.6f));
        var front = Child(zombunny.transform, "Cam_Frente", new Vector3(0f, 0.9f, 2f));
        var riverFrom = Child(anchors, "Cam6_RioInicio", new Vector3(-3f, waterLevel + ShaderSceneSetup.RiverCamHeight, -13f));
        var riverTo = Child(anchors, "Cam6_RioFin", new Vector3(3f, waterLevel + ShaderSceneSetup.RiverCamHeight, -13f));
        float gStop = GroundAt(xE + 14f, 0f);
        var holeFrom = Child(anchors, "Cam8_HoyoInicio", new Vector3(xE + 10f, gStop + 3f, -6f));
        var holeTo = Child(anchors, "Cam8_HoyoFin", new Vector3(xE + 12f, gStop + 3.5f, -3.5f));
        var final = Child(anchors, "Cam9_Final", new Vector3(xE + 14f, gStop, 0f) + ShaderSceneSetup.FinalCamOffset);
        var finalLook = Child(zombunny.transform, "Cam_MiraFinal", ShaderSceneSetup.FinalLookOffset);

        follower.faceTarget = final;
        follower.faceStartTime = 55f;
        zombunny.transform.position = follower.Evaluate(0f);

        var director = cam.GetComponent<CinematicDirector>();
        if (director == null) director = Undo.AddComponent<CinematicDirector>(cam.gameObject);
        Undo.RecordObject(director, "Configurar cinematica");
        director.shots = new List<CinematicDirector.Shot>
        {
            Shot("1 Bosque (grua)", 8f, a1From, a1To, lookPoint, 55f, 0f, false, 0f),
            Shot("2 Sigue por detras", 9f, behind, null, lookPoint, 55f, 0f, false, 2f),
            Shot("3 Lado en el camino", 8f, side, null, lookPoint, 50f, 0f, false, 1.5f),
            Shot("4 Ve el puente", 5f, shoulderFrom, shoulderTo, bridgeFocus, 50f, 0f, false, 1.5f),
            Shot("5 Cara de Zombunny", 4f, front, null, lookPoint, 40f, 0f, false, 0f),
            Shot("6 Cruza el puente", 12f, riverFrom, riverTo, lookPoint, 50f, 0f, false, 0f),
            Shot("7 Otro lado", 6f, behind, null, lookPoint, 55f, 0f, false, 0f),
            Shot("8 Hoyo negro", 5f, holeFrom, holeTo, hole, 50f, 0.03f, false, 2f),
            Shot("9 Final", 11f, final, null, finalLook, ShaderSceneSetup.FinalFov, 0.25f, true, 0f)
        };
        director.previewStartTime = 0f;
        director.tintStartTime = 57f;
        director.fadeStartTime = 66f;
        director.finalAudioTime = 57f;

        if (cam.GetComponent<EnableDepthTexture>() == null)
            Undo.AddComponent<EnableDepthTexture>(cam.gameObject);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = zombunny;
        Debug.Log($"Cinematica lista: {director.TotalDuration:F0} s. Prueba el final con previewStartTime = 55 en el CinematicDirector.");
    }

    static CinematicDirector.Shot Shot(string name, float duration, Transform from, Transform to, Transform lookAt,
        float fov, float shake, bool rampShake, float blendIn)
    {
        return new CinematicDirector.Shot
        {
            name = name,
            duration = duration,
            from = from,
            to = to,
            lookAt = lookAt,
            fov = fov,
            shake = shake,
            rampShake = rampShake,
            blendIn = blendIn
        };
    }

    static float GroundAt(float x, float z)
    {
        if (terrain == null) return BridgeSceneBuilder.GroundLevel;
        return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
    }

    static Transform Child(Transform parent, string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return go.transform;
    }

    static void AddWaypoint(PathFollower follower, Transform parent, string name, float x, float z, float time, float? fixedY = null)
    {
        bool snap = !fixedY.HasValue;
        float y = snap ? GroundAt(x, z) : fixedY.Value;
        var point = Child(parent, name, new Vector3(x, y, z));
        follower.waypoints.Add(new PathFollower.Waypoint { point = point, time = time, snapToGround = snap });
    }

    public static GameObject BuildZombunny(GameObject modelAsset, Transform parent)
    {
        var root = new GameObject("Zombunny");
        root.transform.SetParent(parent, false);
        var visual = Child(root.transform, "Visual", Vector3.zero);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.transform.SetParent(visual, false);

        var mat = GetZombunnyMaterial();
        if (mat != null)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        Bounds b = BridgeSceneBuilder.GetBounds(model);
        model.transform.localScale *= ZombunnyHeight / Mathf.Max(b.size.y, 0.001f);
        b = BridgeSceneBuilder.GetBounds(model);
        model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z) - root.transform.position;

        var follower = root.AddComponent<PathFollower>();
        follower.visual = visual;
        return root;
    }

    static Material GetZombunnyMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ZombunnyMatPath);
        if (mat != null) return mat;

        string normalPath = ZombunnyTexDir + "ZomBunnyNormals.png";
        if (AssetImporter.GetAtPath(normalPath) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }

        mat = new Material(Shader.Find("Standard"));
        var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(ZombunnyTexDir + "ZomBunnyDiffuse.png");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        var occlusion = AssetDatabase.LoadAssetAtPath<Texture2D>(ZombunnyTexDir + "ZomBunnyOcclusion.png");
        var emissive = AssetDatabase.LoadAssetAtPath<Texture2D>(ZombunnyTexDir + "ZomBunnyEmissive.png");

        if (diffuse != null) mat.SetTexture("_MainTex", diffuse);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }
        if (occlusion != null) mat.SetTexture("_OcclusionMap", occlusion);
        if (emissive != null)
        {
            mat.SetTexture("_EmissionMap", emissive);
            mat.SetColor("_EmissionColor", Color.white);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        mat.SetFloat("_Glossiness", 0.3f);

        AssetDatabase.CreateAsset(mat, ZombunnyMatPath);
        return mat;
    }

    static Transform BuildBlackHole(Transform parent, Vector3 position, Transform target, PathFollower follower)
    {
        var hole = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hole.name = "BlackHole";
        hole.transform.SetParent(parent, false);
        hole.transform.position = position;
        hole.transform.localScale = Vector3.one * ShaderSceneSetup.HoleScale;
        Object.DestroyImmediate(hole.GetComponent<Collider>());

        var mat = ShaderSceneSetup.GetBlackHoleMaterial();
        if (mat == null) mat = AssetDatabase.LoadAssetAtPath<Material>(BlackHoleMatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Unlit/Color"));
            mat.SetColor("_Color", Color.black);
            AssetDatabase.CreateAsset(mat, BlackHoleMatPath);
        }
        hole.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var pull = hole.AddComponent<BlackHolePull>();
        pull.target = target;
        pull.follower = follower;
        return hole.transform;
    }
}
