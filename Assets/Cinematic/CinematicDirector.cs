using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Camera))]
public class CinematicDirector : MonoBehaviour
{
    [System.Serializable]
    public class Shot
    {
        public string name;
        public float duration = 5f;
        public Transform from;
        public Transform to;
        public Transform lookAt;
        public float fov = 60f;
        public float shake = 0f;
        public bool rampShake = false;
        public float blendIn = 0f;
    }

    public List<Shot> shots = new List<Shot>();
    public float previewStartTime = 0f;

    [Header("Tint and Fade")]
    public Color tintColor = new Color(0.25f, 0.5f, 0.85f, 0.35f);
    public float tintStartTime = 57f;
    public float tintFadeIn = 2f;
    public float fadeStartTime = 66f;
    public float fadeDuration = 2f;

    [Header("Audio")]
    public AudioClip finalAudio;
    public float finalAudioTime = 57f;
    [Range(0f, 1f)] public float finalAudioVolume = 1f;
    public AudioClip backgroundMusic;
    [Range(0f, 1f)] public float musicVolume = 0.5f;
    [Range(0f, 1f)] public float musicVolumeDuringFinal = 0.15f;

    public static CinematicDirector Instance { get; private set; }
    public static float Now => Instance != null ? Instance.CurrentTime : Time.timeSinceLevelLoad;

    public float CurrentTime { get; private set; }

    public float TotalDuration
    {
        get
        {
            float total = 0f;
            foreach (var s in shots) total += s.duration;
            return total;
        }
    }

    Camera cam;
    Image overlay;
    AudioSource musicSource;
    AudioSource finalSource;
    bool finalPlayed;

    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        CurrentTime = previewStartTime;
        CreateOverlay();

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        finalSource = gameObject.AddComponent<AudioSource>();
        finalSource.playOnAwake = false;
    }

    void Start()
    {
        if (backgroundMusic != null)
        {
            musicSource.clip = backgroundMusic;
            musicSource.volume = musicVolume;
            musicSource.time = Mathf.Min(previewStartTime, backgroundMusic.length - 0.01f);
            musicSource.Play();
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        CurrentTime += Time.deltaTime;
        UpdateAudio();
    }

    void LateUpdate()
    {
        if (shots.Count == 0) return;

        float t = CurrentTime;
        int k = 0;
        float local = t;
        while (k < shots.Count - 1 && local >= shots[k].duration)
        {
            local -= shots[k].duration;
            k++;
        }
        Shot shot = shots[k];
        local = Mathf.Min(local, shot.duration);

        EvaluateShot(shot, local, out Vector3 pos, out Quaternion rot);
        float fov = shot.fov;

        if (k > 0 && shot.blendIn > 0f && local < shot.blendIn)
        {
            Shot prev = shots[k - 1];
            EvaluateShot(prev, prev.duration, out Vector3 prevPos, out Quaternion prevRot);
            float w = Mathf.SmoothStep(0f, 1f, local / shot.blendIn);
            pos = Vector3.Lerp(prevPos, pos, w);
            rot = Quaternion.Slerp(prevRot, rot, w);
            fov = Mathf.Lerp(prev.fov, fov, w);
        }

        float shake = shot.shake * (shot.rampShake ? local / Mathf.Max(shot.duration, 0.01f) : 1f);
        if (shake > 0f)
        {
            float s = t * 12f;
            pos += new Vector3(Mathf.PerlinNoise(s, 0.1f) - 0.5f, Mathf.PerlinNoise(0.2f, s) - 0.5f, Mathf.PerlinNoise(s, s) - 0.5f) * shake;
            rot *= Quaternion.Euler((Mathf.PerlinNoise(s, 3.3f) - 0.5f) * shake * 10f, (Mathf.PerlinNoise(4.4f, s) - 0.5f) * shake * 10f, (Mathf.PerlinNoise(s, 5.5f) - 0.5f) * shake * 20f);
        }

        transform.SetPositionAndRotation(pos, rot);
        cam.fieldOfView = fov;
        UpdateOverlay(t);
    }

    void EvaluateShot(Shot shot, float local, out Vector3 pos, out Quaternion rot)
    {
        pos = transform.position;
        rot = transform.rotation;

        if (shot.from != null)
        {
            float p = shot.duration > 0f ? Mathf.SmoothStep(0f, 1f, local / shot.duration) : 1f;
            pos = shot.to != null ? Vector3.Lerp(shot.from.position, shot.to.position, p) : shot.from.position;
            rot = shot.to != null ? Quaternion.Slerp(shot.from.rotation, shot.to.rotation, p) : shot.from.rotation;
        }

        if (shot.lookAt != null)
        {
            Vector3 dir = shot.lookAt.position - pos;
            if (dir.sqrMagnitude > 0.0001f) rot = Quaternion.LookRotation(dir, Vector3.up);
        }
    }

    void UpdateAudio()
    {
        if (finalAudio == null || finalPlayed || CurrentTime < finalAudioTime) return;

        finalPlayed = true;
        float offset = CurrentTime - finalAudioTime;
        if (offset >= finalAudio.length) return;

        finalSource.clip = finalAudio;
        finalSource.volume = finalAudioVolume;
        finalSource.time = offset;
        finalSource.Play();
        if (musicSource.isPlaying) musicSource.volume = musicVolumeDuringFinal;
    }

    void CreateOverlay()
    {
        var canvasGO = new GameObject("CinematicOverlay");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var imageGO = new GameObject("Tint");
        imageGO.transform.SetParent(canvasGO.transform, false);
        overlay = imageGO.AddComponent<Image>();
        overlay.raycastTarget = false;
        overlay.color = Color.clear;

        var rt = overlay.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void UpdateOverlay(float t)
    {
        float tint = Mathf.SmoothStep(0f, 1f, (t - tintStartTime) / Mathf.Max(tintFadeIn, 0.01f));
        float fade = Mathf.SmoothStep(0f, 1f, (t - fadeStartTime) / Mathf.Max(fadeDuration, 0.01f));

        Color c = Color.Lerp(tintColor, Color.black, fade);
        c.a = Mathf.Lerp(tintColor.a * tint, 1f, fade);
        overlay.color = c;
    }
}
