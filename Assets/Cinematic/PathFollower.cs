using System.Collections.Generic;
using UnityEngine;

public class PathFollower : MonoBehaviour
{
    [System.Serializable]
    public class Waypoint
    {
        public Transform point;
        public float time;
        public bool snapToGround = true;
    }

    public List<Waypoint> waypoints = new List<Waypoint>();
    public Transform visual;
    public float footOffset = 0.1f;
    public float turnSpeed = 5f;

    [Header("Hop")]
    public float hopHeight = 0.25f;
    public float hopLength = 0.9f;
    public float tiltAngle = 8f;
    public float breathAmount = 0.03f;

    [Header("Face Target")]
    public Transform faceTarget;
    public float faceStartTime = 55f;

    [HideInInspector] public float extraRoll;

    Vector3 lastPos;
    float distance;
    float moving;
    Vector3 visualBasePos;
    Quaternion visualBaseRot;
    Vector3 visualBaseScale;
    bool initialized;

    void Start()
    {
        if (visual != null)
        {
            visualBasePos = visual.localPosition;
            visualBaseRot = visual.localRotation;
            visualBaseScale = visual.localScale;
        }
        float t = CinematicDirector.Now;
        transform.position = Evaluate(t);
        Vector3 ahead = Evaluate(t + 0.1f) - transform.position;
        ahead.y = 0f;
        if (ahead.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(ahead);
        lastPos = transform.position;
        initialized = true;
    }

    void Update()
    {
        if (!initialized || waypoints.Count == 0) return;

        float t = CinematicDirector.Now;
        Vector3 pos = Evaluate(t);
        Vector3 delta = pos - lastPos;
        delta.y = 0f;
        float step = delta.magnitude;
        distance += step;

        float speed = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;
        moving = Mathf.MoveTowards(moving, speed > 0.2f ? 1f : 0f, Time.deltaTime * 4f);

        Quaternion targetRot = transform.rotation;
        if (faceTarget != null && t >= faceStartTime)
        {
            Vector3 dir = faceTarget.position - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) targetRot = Quaternion.LookRotation(dir);
        }
        else if (step > 0.001f)
        {
            targetRot = Quaternion.LookRotation(delta);
        }

        transform.position = pos;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        lastPos = pos;

        AnimateVisual();
    }

    void AnimateVisual()
    {
        if (visual == null) return;

        float phase = distance / Mathf.Max(hopLength, 0.01f) * Mathf.PI;
        float hop = Mathf.Abs(Mathf.Sin(phase)) * hopHeight * moving;
        float tilt = Mathf.Sin(phase) * tiltAngle * moving;
        float breath = 1f + Mathf.Sin(Time.time * 3f) * breathAmount * (1f - moving);

        visual.localPosition = visualBasePos + Vector3.up * hop;
        visual.localRotation = visualBaseRot * Quaternion.Euler(0f, 0f, tilt + extraRoll);
        visual.localScale = new Vector3(visualBaseScale.x, visualBaseScale.y * breath, visualBaseScale.z);
    }

    public Vector3 Evaluate(float t)
    {
        int n = waypoints.Count;
        if (n == 0) return transform.position;
        if (n == 1 || t <= waypoints[0].time) return Place(waypoints[0].point.position, waypoints[0].snapToGround ? 1f : 0f);
        if (t >= waypoints[n - 1].time) return Place(waypoints[n - 1].point.position, waypoints[n - 1].snapToGround ? 1f : 0f);

        int i = 0;
        while (i < n - 2 && t >= waypoints[i + 1].time) i++;

        Waypoint a = waypoints[i];
        Waypoint b = waypoints[i + 1];
        float seg = b.time - a.time;
        float u = seg > 0f ? Mathf.Clamp01((t - a.time) / seg) : 1f;

        Vector3 p0 = waypoints[Mathf.Max(i - 1, 0)].point.position;
        Vector3 p1 = a.point.position;
        Vector3 p2 = b.point.position;
        Vector3 p3 = waypoints[Mathf.Min(i + 2, n - 1)].point.position;
        if ((p2 - p1).sqrMagnitude < 0.0001f) return Place(p1, a.snapToGround ? 1f : 0f);

        Vector3 p = CatmullRom(p0, p1, p2, p3, u);
        float snap = Mathf.Lerp(a.snapToGround ? 1f : 0f, b.snapToGround ? 1f : 0f, u);
        return Place(p, snap);
    }

    Vector3 Place(Vector3 p, float snap)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (snap > 0f && terrain != null)
        {
            float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
            p.y = Mathf.Lerp(p.y, ground, snap);
        }
        p.y += footOffset;
        return p;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    void OnDrawGizmos()
    {
        if (waypoints.Count < 2) return;
        foreach (var w in waypoints) if (w.point == null) return;

        Gizmos.color = Color.yellow;
        float start = waypoints[0].time;
        float end = waypoints[waypoints.Count - 1].time;
        Vector3 prev = Evaluate(start);
        for (float t = start + 0.25f; t <= end; t += 0.25f)
        {
            Vector3 p = Evaluate(t);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }

        Gizmos.color = Color.cyan;
        foreach (var w in waypoints)
        {
            Gizmos.DrawWireSphere(w.point.position, 0.4f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(w.point.position + Vector3.up, w.point.name + "  t=" + w.time + "s");
#endif
        }
    }
}
