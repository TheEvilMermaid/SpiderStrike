using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PrototypeSceneSetup
{
    static bool platformsPlaced;

    static PrototypeSceneSetup()
    {
        EditorApplication.delayCall += PlacePlatformsInOpenScene;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                PlacePlatformsInOpenScene();
        };
    }

    public static void Create()
    {
        Build();
        EditorApplication.Exit(0);
    }

    [MenuItem("SpiderStrike/Create Prototype Scene")]
    public static void CreateFromMenu()
    {
        Build();
    }

    static void Build()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Materials");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.38f, 0.4f, 0.44f);

        var lightGo = new GameObject("Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.color = new Color(1f, 0.97f, 0.92f);
        lightGo.transform.rotation = Quaternion.Euler(48f, -28f, 0f);

        GameObject background = GameObject.CreatePrimitive(PrimitiveType.Cube);
        background.name = "Background";
        background.transform.position = new Vector3(0f, 4.5f, -8f);
        background.transform.localScale = new Vector3(40f, 24f, 1f);
        Object.DestroyImmediate(background.GetComponent<Collider>());
        Paint(background, "Background", new Color(0.09f, 0.1f, 0.13f));

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(22f, 1f, 4f);
        Paint(ground, "Ground", new Color(0.34f, 0.36f, 0.32f));

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall";
        wall.transform.position = new Vector3(8f, 5f, 0f);
        wall.transform.localScale = new Vector3(1f, 10f, 4f);
        Paint(wall, "Wall", new Color(0.48f, 0.44f, 0.4f));

        CenterPlatforms.Create();
        PaintPlatforms();

        var webGo = new GameObject("Web");
        WebBody web = webGo.AddComponent<WebBody>();

        var insects = new GameObject("Insects");
        InsectSpawner spawner = insects.AddComponent<InsectSpawner>();
        var spawnerData = new SerializedObject(spawner);
        spawnerData.FindProperty("web").objectReferenceValue = web;
        spawnerData.ApplyModifiedPropertiesWithoutUndo();

        GameObject spider = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        spider.name = "Spider";
        spider.transform.position = new Vector3(-3f, 0.4f, 0f);
        spider.transform.localScale = Vector3.one * 0.7f;
        Object.DestroyImmediate(spider.GetComponent<Collider>());
        Paint(spider, "Spider", new Color(0.18f, 0.1f, 0.1f));
        SpiderController controller = spider.AddComponent<SpiderController>();

        var spiderData = new SerializedObject(controller);
        spiderData.FindProperty("web").objectReferenceValue = web;
        spiderData.ApplyModifiedPropertiesWithoutUndo();

        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.fieldOfView = 42f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 80f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
        cameraGo.AddComponent<AudioListener>();
        cameraGo.transform.position = new Vector3(-3f, 1.8f, 14f);
        cameraGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        GameCamera gameCamera = cameraGo.AddComponent<GameCamera>();

        var cameraData = new SerializedObject(gameCamera);
        cameraData.FindProperty("target").objectReferenceValue = spider.transform;
        cameraData.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Prototype.unity");
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Prototype.unity", true)
        };
        AssetDatabase.SaveAssets();
        ValidateWebSag();
    }

    static void ValidateWebSag()
    {
        Physics.SyncTransforms();
        if (!Physics.Raycast(new Vector3(-3f, 2f, 0f), Vector3.down, out RaycastHit groundHit, 5f))
            throw new System.InvalidOperationException("Ground raycast failed.");
        if (groundHit.point.y > 0.05f || groundHit.point.y < -0.05f)
            throw new System.InvalidOperationException("Ground height was " + groundHit.point.y);

        if (!Physics.Raycast(new Vector3(0f, 2f, 0f), Vector3.right, out RaycastHit wallHit, 10f))
            throw new System.InvalidOperationException("Wall raycast failed.");
        Debug.Log("Scene colliders ok. Ground y=" + groundHit.point.y + " wall x=" + wallHit.point.x);

        WebBody web = Object.FindAnyObjectByType<WebBody>();
        web.StartAnchor(new Vector2(-2f, 4f));
        if (!web.FinishAnchor(new Vector2(2f, 4f)))
            throw new System.InvalidOperationException("Web anchor was rejected.");

        float startY = web.GetPoint(10, 0f).y;
        for (int i = 0; i < 200; i++)
            web.Simulate(0.02f);

        float endY = web.GetPoint(10, 0f).y;
        if (float.IsNaN(endY) || endY < -2f || endY > startY - 0.15f)
            throw new System.InvalidOperationException("Web did not hang. startY=" + startY + " endY=" + endY);

        Debug.Log("Web sag check ok. startY=" + startY + " endY=" + endY);

        int count = web.SegmentCount;
        if (!web.BeginLay(new Vector2(-2f, 4f), 0.8f, false))
            throw new System.InvalidOperationException("Web did not start from the existing anchor.");
        web.Lay(new Vector2(-2f, 6.5f));
        web.Lay(new Vector2(2f, 5.2f));
        if (!web.ConnectToNearest(new Vector2(2f, 4f), 2f) || web.IsLaying || web.SegmentCount <= count)
            throw new System.InvalidOperationException("Web connection failed. segments=" + web.SegmentCount);

        Debug.Log("Web connection ok. segments=" + web.SegmentCount);
        ValidateInsectCatch(web);
        ValidateSpiderStands();
    }

    static void ValidateInsectCatch(WebBody web)
    {
        Vector2 onWeb = web.GetPoint(10, 0.5f);
        Insect hit = CreateInsect(web, onWeb, onWeb + Vector2.left);
        Insect miss = CreateInsect(web, onWeb + Vector2.up * 5f, onWeb + Vector2.up * 5f + Vector2.right);
        try
        {
            hit.Tick(0.25f);
            miss.Tick(0.25f);
            Debug.Log("Insect check hit=" + hit.State + " miss=" + miss.State);
            if (hit.State != Insect.FlightState.Stuck)
                throw new System.InvalidOperationException("Insect did not stick to the web.");
            if (miss.State != Insect.FlightState.Flying)
                throw new System.InvalidOperationException("Insect stuck far from the web.");
        }
        finally
        {
            if (hit != null)
                Object.DestroyImmediate(hit.gameObject);
            if (miss != null)
                Object.DestroyImmediate(miss.gameObject);
        }
    }

    static Insect CreateInsect(WebBody web, Vector2 cross, Vector2 from)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        Insect insect = body.AddComponent<Insect>();
        insect.Launch(web, from, cross, -0.2f, Color.yellow, 1.2f, Vector2.right);
        return insect;
    }

    static void ValidateSpiderStands()
    {
        SpiderController spider = Object.FindAnyObjectByType<SpiderController>();
        var tick = typeof(SpiderController).GetMethod(
            "FixedUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        for (int i = 0; i < 90; i++)
            tick.Invoke(spider, null);

        float y = spider.transform.position.y;
        Debug.Log("Spider stand check y=" + y + " state=" + spider.State);
        if (y < 0.2f || y > 0.7f || spider.State == SpiderController.SpiderState.Falling)
            throw new System.InvalidOperationException("Spider did not stand. y=" + y + " state=" + spider.State);

        var inputX = typeof(SpiderController).GetField(
            "inputX",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        float xBefore = spider.transform.position.x;
        inputX.SetValue(spider, 1f);
        for (int i = 0; i < 8; i++)
            tick.Invoke(spider, null);
        float xAfterLeft = spider.transform.position.x;
        Debug.Log("Spider left check x=" + xAfterLeft);
        if (xAfterLeft <= xBefore + 0.2f)
            throw new System.InvalidOperationException("A did not move the spider left. from=" + xBefore + " to=" + xAfterLeft);

        inputX.SetValue(spider, 0f);

        var jumpFlag = typeof(SpiderController).GetField(
            "jumpPressed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        jumpFlag.SetValue(spider, true);
        tick.Invoke(spider, null);
        float jumpedY = spider.transform.position.y;
        Debug.Log("Spider jump check y=" + jumpedY + " state=" + spider.State);
        if (jumpedY < y + 0.1f)
            throw new System.InvalidOperationException("Jump stayed on the ground. from=" + y + " to=" + jumpedY);

        spider.transform.position = new Vector3(-3f, 0.4f, 0f);
    }

    static void PlacePlatformsInOpenScene()
    {
        if (platformsPlaced || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "Prototype")
            return;
        bool created = false;
        if (GameObject.Find("PlatformShelf") == null)
        {
            EnsureFolder("Assets/Materials");
            CenterPlatforms.Create();
            PaintPlatforms();
            created = true;
        }

        bool spread = CenterPlatforms.SpreadIfClustered();
        platformsPlaced = true;
        if (!created && !spread)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void PaintPlatforms()
    {
        Paint(GameObject.Find("PlatformShelf"), "PlatformShelf", new Color(0.62f, 0.5f, 0.36f));
        Paint(GameObject.Find("PlatformPillar"), "PlatformPillar", new Color(0.4f, 0.42f, 0.48f));
        Paint(GameObject.Find("PlatformDisc"), "PlatformDisc", new Color(0.38f, 0.52f, 0.42f));
        Paint(GameObject.Find("PlatformDome"), "PlatformDome", new Color(0.55f, 0.46f, 0.32f));
        Paint(GameObject.Find("PlatformRamp"), "PlatformRamp", new Color(0.58f, 0.56f, 0.5f));
    }

    static void Paint(GameObject target, string materialName, Color color)
    {
        string path = "Assets/Materials/" + materialName + ".mat";
        Shader shader = Shader.Find("Standard");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        EditorUtility.SetDirty(material);
        target.GetComponent<Renderer>().sharedMaterial = material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
