using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends a few insects from behind the play plane toward the camera.
/// </summary>
public class InsectSpawner : MonoBehaviour
{
    [SerializeField] WebBody web;
    [SerializeField] float interval = 6f;
    [SerializeField] int maxFlying = 3;
    [SerializeField] float firstDelay = 2f;

    readonly List<Insect> insects = new List<Insect>();
    float timer;
    static readonly Color[] colors =
    {
        new Color(0.95f, 0.78f, 0.2f),
        new Color(0.45f, 0.85f, 0.4f),
        new Color(0.95f, 0.45f, 0.25f)
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInScene()
    {
        if (FindAnyObjectByType<InsectSpawner>() != null)
            return;

        var go = new GameObject("Insects");
        go.AddComponent<InsectSpawner>();
    }

    void Awake()
    {
        if (web == null)
            web = FindAnyObjectByType<WebBody>();
        timer = firstDelay;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        timer = interval;
        if (FlyingCount() >= maxFlying)
            return;

        Spawn();
    }

    void Spawn()
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        var cross = new Vector2(Random.Range(-7.5f, 5.5f), Random.Range(1.5f, 6.2f));
        var from = cross + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(1.4f, 2.6f);
        float errorAngle = angle + Random.Range(0.8f, 2.4f);
        var error = new Vector2(Mathf.Cos(errorAngle), Mathf.Sin(errorAngle)) * Random.Range(1.15f, 2.05f);

        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Insect";
        body.transform.SetParent(transform, false);
        body.transform.localScale = Vector3.one * 0.42f;
        Destroy(body.GetComponent<Collider>());

        Insect insect = body.AddComponent<Insect>();
        insect.Launch(web, from, cross, -6.2f, colors[Random.Range(0, colors.Length)], 1.35f, error);
        insects.Add(insect);
    }

    int FlyingCount()
    {
        int count = 0;
        for (int i = insects.Count - 1; i >= 0; i--)
        {
            if (insects[i] == null)
            {
                insects.RemoveAt(i);
                continue;
            }

            if (insects[i].State == Insect.FlightState.Flying)
                count++;
        }

        return count;
    }
}
