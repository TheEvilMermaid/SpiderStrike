using UnityEngine;

/// <summary>
/// Flies from behind the play plane toward the camera.
/// The circle on Z = 0 is a rough forecast, not the exact crossing point.
/// </summary>
public class Insect : MonoBehaviour
{
    public enum FlightState
    {
        Flying,
        Stuck,
        Gone
    }

    const int MarkerSteps = 28;
    const float CatchRadius = 0.5f;

    WebBody web;
    FlightState state = FlightState.Flying;
    Vector2 fromXy;
    Vector2 crossXy;
    Vector2 forecastError;
    float phase;
    float age;
    float z;
    float zStart;
    float flySpeed;
    bool crossed;
    int stuckSegment;
    float stuckT;
    LineRenderer marker;
    Color color;

    public FlightState State => state;

    void Awake()
    {
        EnsureMarker();
    }

    void EnsureMarker()
    {
        if (marker != null)
            return;

        marker = gameObject.GetComponent<LineRenderer>();
        if (marker == null)
            marker = gameObject.AddComponent<LineRenderer>();
        marker.useWorldSpace = true;
        marker.loop = true;
        marker.alignment = LineAlignment.View;
        marker.positionCount = MarkerSteps;
        marker.startWidth = 0.045f;
        marker.endWidth = 0.045f;
        marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        marker.receiveShadows = false;
        marker.numCapVertices = 2;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            var material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            marker.sharedMaterial = material;
        }
    }

    public void Launch(WebBody target, Vector2 from, Vector2 cross, float startZ, Color bodyColor, float speed, Vector2 error)
    {
        EnsureMarker();
        web = target;
        fromXy = from;
        crossXy = cross;
        zStart = startZ;
        z = startZ;
        flySpeed = speed;
        forecastError = error;
        phase = Random.Range(0f, 6.28f);
        color = bodyColor;
        state = FlightState.Flying;

        var renderer = GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            var material = new Material(renderer.sharedMaterial);
            material.color = bodyColor;
            material.hideFlags = HideFlags.HideAndDontSave;
            renderer.sharedMaterial = material;
        }

        Color ring = bodyColor;
        ring.a = 0.45f;
        marker.startColor = ring;
        marker.endColor = ring;
        marker.enabled = true;
    }

    void Update()
    {
        Tick(Time.deltaTime);
    }

    public void Tick(float dt)
    {
        if (state == FlightState.Gone)
            return;

        age += dt;

        if (state == FlightState.Stuck)
        {
            marker.enabled = false;
            if (web == null)
                return;

            Vector2 point = web.GetPoint(stuckSegment, stuckT);
            transform.position = new Vector3(point.x, point.y, 0.12f);
            return;
        }

        float previousZ = z;
        z += flySpeed * dt;
        float approach = Mathf.Clamp01(Mathf.InverseLerp(zStart, 0f, z));
        float along = Mathf.SmoothStep(0f, 1f, approach);
        Vector2 xy = z < 0f ? Vector2.Lerp(fromXy, crossXy, along) : crossXy;
        transform.position = new Vector3(xy.x, xy.y, z);

        if (z < 0f)
            DrawForecast(approach);
        else
            marker.enabled = false;

        if (!crossed && previousZ < 0f && z >= 0f)
        {
            crossed = true;
            TryCatch();
        }

        if (state == FlightState.Flying && z > 4.5f)
        {
            state = FlightState.Gone;
            Destroy(gameObject);
        }
    }

    void TryCatch()
    {
        if (web == null || !web.TryGetClosest(crossXy, CatchRadius, out int segment, out float t))
            return;

        state = FlightState.Stuck;
        stuckSegment = segment;
        stuckT = t;
        marker.enabled = false;

        Vector2 travel = crossXy - fromXy;
        Vector2 kick = Vector2.down * 0.12f;
        if (travel.sqrMagnitude > 0.01f)
            kick += travel.normalized * 0.05f;
        web.StickLoad(segment, t, 16f, kick);
        transform.position = new Vector3(web.GetPoint(segment, t).x, web.GetPoint(segment, t).y, 0.12f);
    }

    void DrawForecast(float approach)
    {
        float shaped = Mathf.SmoothStep(0f, 1f, approach);
        float remain = 1f - shaped;
        Vector2 drift = new Vector2(
            Mathf.Sin(age * 1.1f + phase),
            Mathf.Cos(age * 0.75f + phase * 1.6f));
        Vector2 center = crossXy + forecastError * Mathf.Lerp(1f, 0.22f, shaped) + drift * (0.32f * remain + 0.05f);
        float radius = Mathf.Lerp(1.7f, 0.32f, shaped);

        marker.enabled = true;
        for (int i = 0; i < MarkerSteps; i++)
        {
            float angle = i / (float)MarkerSteps * Mathf.PI * 2f;
            marker.SetPosition(i, new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius,
                0f));
        }
    }
}
