using UnityEngine;

/// <summary>
/// Solid shapes spread across the level. Built into the scene by the editor,
/// never spawned when the game starts.
/// Gaps are wide enough that the spider's raycasts hit one surface at a time.
/// </summary>
public static class CenterPlatforms
{
    struct Piece
    {
        public string name;
        public PrimitiveType type;
        public Vector3 position;
        public Vector3 scale;
        public Vector3 euler;
        public Color color;
    }

    static Piece[] Layout()
    {
        return new[]
        {
            new Piece
            {
                name = "PlatformDome",
                type = PrimitiveType.Sphere,
                position = new Vector3(-9.2f, 0.65f, 0f),
                scale = new Vector3(1.3f, 1.3f, 1.3f),
                color = new Color(0.55f, 0.46f, 0.32f)
            },
            new Piece
            {
                name = "PlatformShelf",
                type = PrimitiveType.Cube,
                position = new Vector3(-5.15f, 3.2f, 0f),
                scale = new Vector3(2.2f, 0.34f, 2.2f),
                color = new Color(0.62f, 0.5f, 0.36f)
            },
            new Piece
            {
                name = "PlatformDisc",
                type = PrimitiveType.Cylinder,
                position = new Vector3(-1f, 5.25f, 0f),
                scale = new Vector3(1.45f, 0.8f, 1.45f),
                euler = new Vector3(90f, 0f, 0f),
                color = new Color(0.38f, 0.52f, 0.42f)
            },
            new Piece
            {
                name = "PlatformPillar",
                type = PrimitiveType.Cube,
                position = new Vector3(2.5f, 1.35f, 0f),
                scale = new Vector3(0.9f, 2.7f, 1.8f),
                color = new Color(0.4f, 0.42f, 0.48f)
            },
            new Piece
            {
                name = "PlatformRamp",
                type = PrimitiveType.Cube,
                position = new Vector3(5.85f, 0.45f, 0f),
                scale = new Vector3(1.5f, 0.28f, 1.8f),
                euler = new Vector3(0f, 0f, -22f),
                color = new Color(0.58f, 0.56f, 0.5f)
            }
        };
    }

    public static void Create()
    {
        var root = new GameObject("Platforms");
        foreach (Piece piece in Layout())
            Make(root.transform, piece);
    }

    /// <summary>
    /// Moves the saved platforms apart when they are still in the old tight cluster.
    /// </summary>
    public static bool SpreadIfClustered()
    {
        GameObject shelf = GameObject.Find("PlatformShelf");
        if (shelf == null || shelf.transform.position.x < -2f)
            return false;

        foreach (Piece piece in Layout())
        {
            GameObject platform = GameObject.Find(piece.name);
            if (platform == null)
                continue;

            platform.transform.position = piece.position;
            platform.transform.localScale = piece.scale;
            platform.transform.rotation = Quaternion.Euler(piece.euler);
        }

        return true;
    }

    static void Make(Transform parent, Piece piece)
    {
        GameObject platform = GameObject.CreatePrimitive(piece.type);
        platform.name = piece.name;
        platform.transform.SetParent(parent, false);
        platform.transform.position = piece.position;
        platform.transform.localScale = piece.scale;
        platform.transform.rotation = Quaternion.Euler(piece.euler);

        if (piece.type == PrimitiveType.Cylinder)
        {
            // A flat capsule collapses into a sphere and no longer matches the mesh.
            Object.DestroyImmediate(platform.GetComponent<CapsuleCollider>());
            MeshCollider mesh = platform.AddComponent<MeshCollider>();
            mesh.convex = true;
        }

        Shader shader = Shader.Find("Standard");
        if (shader == null)
            return;

        var material = new Material(shader);
        material.color = piece.color;
        platform.GetComponent<Renderer>().sharedMaterial = material;
    }
}
