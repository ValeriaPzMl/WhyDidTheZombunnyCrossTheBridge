using UnityEngine;

[DefaultExecutionOrder(10)]
public class BlackHolePull : MonoBehaviour
{
    public Transform target;
    public PathFollower follower;
    public Vector3 selfRotation = Vector3.zero;

    [Header("Goodbye Wobble")]
    public float wobbleStart = 57f;
    public float wobbleAngle = 15f;
    public float wobbleSpeed = 6f;

    [Header("Pull")]
    public float pullStart = 61f;
    public float pullDuration = 4f;
    public float spiralTurns = 1.5f;
    public float spiralRadius = 1.5f;
    public float spinDegrees = 900f;
    public float liftHeight = 2.5f;

    bool pulling;
    Vector3 startPos;
    Quaternion startRot;
    Vector3 startScale;

    void Update()
    {
        transform.Rotate(selfRotation * Time.deltaTime, Space.World);
        if (target == null) return;

        float t = CinematicDirector.Now;

        if (follower != null && !pulling)
        {
            float w = t >= wobbleStart ? Mathf.Clamp01((t - wobbleStart) / 0.5f) : 0f;
            follower.extraRoll = Mathf.Sin((t - wobbleStart) * wobbleSpeed) * wobbleAngle * w;
        }

        if (t < pullStart) return;

        if (!pulling)
        {
            pulling = true;
            if (follower != null)
            {
                follower.extraRoll = 0f;
                follower.enabled = false;
            }
            startPos = target.position;
            startRot = target.rotation;
            startScale = target.localScale;
        }

        float p = Mathf.Clamp01((t - pullStart) / Mathf.Max(pullDuration, 0.01f));
        float e = p * p * p;

        Vector3 center = transform.position;
        Vector3 axis = (center - startPos).normalized;
        Vector3 side = Vector3.Cross(axis, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(side, axis);

        float angle = p * spiralTurns * Mathf.PI * 2f;
        Vector3 swirl = (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * spiralRadius * Mathf.Sin(p * Mathf.PI);

        Vector3 pos = Vector3.Lerp(startPos, center, e) + swirl + Vector3.up * liftHeight * Mathf.Sin(p * Mathf.PI);
        pos.y = Mathf.Max(pos.y, startPos.y);
        target.position = pos;
        target.rotation = startRot * Quaternion.Euler(e * spinDegrees * 0.3f, e * spinDegrees, e * spinDegrees * 0.5f);
        target.localScale = startScale * Mathf.Max(1f - e, 0.001f);

        if (p >= 1f) target.gameObject.SetActive(false);
    }
}
