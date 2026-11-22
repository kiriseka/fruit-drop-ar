using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Object = UnityEngine.Object;

/// <summary>
/// Development tool, not part of the game. Runs scripted checks in XR Simulation by steering the simulated
/// camera, and writes the results to the console and to Temp/SimTest.log.
/// Call <see cref="RunInPlayMode"/> from Edit mode, or <see cref="Run"/> while already playing.
/// The <b>Fruit Drop > Sim</b> menu runs one scenario, or all of them one after another, each in its own
/// Play mode session; the log ends with "All done".
/// </summary>
[InitializeOnLoad]
public static class SimTestDriver
{
    const string k_LogPath = "Temp/SimTest.log";
    const string k_PendingKey = "SimTestDriver.Pending";
    const string k_QueueKey = "SimTestDriver.Queue";
    const string k_QueueEnd = "#end";
    const string k_BestScoreKey = "FruitDropAR.HighScore";
    const string k_SeenHowToPlayKey = "FruitDropAR.SeenHowToPlay";
    const string k_RestoreSizeKey = "SimTestDriver.RestoreGameViewSize";

    static void RestoreGameViewSize()
    {
        string size = SessionState.GetString(k_RestoreSizeKey, "");
        if (size == "")
            return;

        SessionState.EraseString(k_RestoreSizeKey);
        var parts = size.Split('x');
        if (parts.Length == 2 && uint.TryParse(parts[0], out uint width) && uint.TryParse(parts[1], out uint height) && width > 0 && height > 0)
            PlayModeWindow.SetCustomRenderingResolution(width, height, "Editor");
    }

    // Every scenario, in the order the regression run plays them.
    const string k_AllScenarios = "floor-edge,drop,marker,spawner,play,states,flow,hud,score-round,best,curve,picks,variety,feedback,onboarding,onboarding-again,point-memory,portrait,mobile,pause-menu,pause-menu-again,hud-feedback,screens,scene-polish,long-round";

    static IEnumerator<float> s_Routine;
    static float s_ResumeTime;
    static bool s_ExitWhenDone;

    // Entering Play mode reloads scripts, so a scenario requested from Edit mode is kept in SessionState
    // and started here once the reload is over.
    static SimTestDriver()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                RestoreGameViewSize();

                // Not from inside the callback: Play mode cannot be entered again while it is still being left.
                OnNextUpdate(NextInQueue);
                return;
            }

            string pending = SessionState.GetString(k_PendingKey, "");
            if (state != PlayModeStateChange.EnteredPlayMode || pending == "")
                return;

            SessionState.EraseString(k_PendingKey);
            Run(pending);
            s_ExitWhenDone = true;

            // An unknown scenario never starts, so nothing would end Play mode for it.
            if (s_Routine == null)
                Stop();
        };

        // A queue that had to wait for changed scripts to compile starts here, after the reload.
        OnNextUpdate(NextInQueue);
    }

    // EditorApplication.delayCall is not run while the editor is in the background, and a test run has to
    // carry on there. EditorApplication.update keeps being called, so that is used instead.
    static void OnNextUpdate(Action action)
    {
        EditorApplication.CallbackFunction once = null;
        once = () =>
        {
            EditorApplication.update -= once;
            action();
        };
        EditorApplication.update += once;
    }

    [MenuItem("Fruit Drop/Sim/All")] static void MenuAll() => RunQueue(k_AllScenarios);
    [MenuItem("Fruit Drop/Sim/floor-edge")] static void MenuFloorEdge() => RunQueue("floor-edge");
    [MenuItem("Fruit Drop/Sim/drop")] static void MenuDrop() => RunQueue("drop");
    [MenuItem("Fruit Drop/Sim/marker")] static void MenuMarker() => RunQueue("marker");
    [MenuItem("Fruit Drop/Sim/spawner")] static void MenuSpawner() => RunQueue("spawner");
    [MenuItem("Fruit Drop/Sim/play")] static void MenuPlay() => RunQueue("play");
    [MenuItem("Fruit Drop/Sim/states")] static void MenuStates() => RunQueue("states");
    [MenuItem("Fruit Drop/Sim/flow")] static void MenuFlow() => RunQueue("flow");
    [MenuItem("Fruit Drop/Sim/hud")] static void MenuHud() => RunQueue("hud");
    [MenuItem("Fruit Drop/Sim/score-round, then best")] static void MenuScoreRound() => RunQueue("score-round,best");
    [MenuItem("Fruit Drop/Sim/curve, then picks")] static void MenuCurve() => RunQueue("curve,picks");
    [MenuItem("Fruit Drop/Sim/variety")] static void MenuVariety() => RunQueue("variety");
    [MenuItem("Fruit Drop/Sim/long-round")] static void MenuLongRound() => RunQueue("long-round");
    [MenuItem("Fruit Drop/Sim/feedback")] static void MenuFeedback() => RunQueue("feedback");
    [MenuItem("Fruit Drop/Sim/onboarding, then again")] static void MenuOnboarding() => RunQueue("onboarding,onboarding-again");
    [MenuItem("Fruit Drop/Sim/portrait")] static void MenuPortrait() => RunQueue("portrait");
    [MenuItem("Fruit Drop/Sim/mobile")] static void MenuMobile() => RunQueue("mobile");
    [MenuItem("Fruit Drop/Sim/pause-menu, then again")] static void MenuPauseMenu() => RunQueue("pause-menu,pause-menu-again");
    [MenuItem("Fruit Drop/Sim/hud-feedback")] static void MenuHudFeedback() => RunQueue("hud-feedback");
    [MenuItem("Fruit Drop/Sim/screens")] static void MenuScreens() => RunQueue("screens");
    [MenuItem("Fruit Drop/Sim/scene-polish")] static void MenuScenePolish() => RunQueue("scene-polish");

    // Not part of All: it is a measurement of a few minutes, not a pass or fail check.
    [MenuItem("Fruit Drop/Sim/human-round (balance)")] static void MenuHumanRound() => RunQueue("human-round");

    // Any other selection: the comma-separated list in Temp/SimTest.queue.
    [MenuItem("Fruit Drop/Sim/From Queue File")] static void MenuFromFile() => RunQueue(File.ReadAllText("Temp/SimTest.queue").Trim());

    /// <summary>
    /// Runs scenarios one after another, each in its own Play mode session, so every one starts from a fresh
    /// scan. <paramref name="scenarios"/> is a comma-separated list. Changed scripts are compiled first.
    /// </summary>
    public static void RunQueue(string scenarios)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[SimTest] Leave Play mode before starting a run");
            return;
        }

        File.WriteAllText(k_LogPath, "");
        SessionState.EraseString(k_PendingKey);
        SessionState.SetString(k_QueueKey, scenarios);
        AssetDatabase.Refresh();

        // A compile ends in a script reload, and the static constructor carries on from there. If the compile
        // fails there is no reload, so that case is watched for here.
        if (EditorApplication.isCompiling)
            EditorApplication.update += WaitForFailedCompile;
        else
            NextInQueue();
    }

    static void WaitForFailedCompile()
    {
        if (EditorApplication.isCompiling)
            return;

        EditorApplication.update -= WaitForFailedCompile;
        if (EditorUtility.scriptCompilationFailed)
            NextInQueue();
    }

    static void NextInQueue()
    {
        // isPlaying is checked as well, because isPlayingOrWillChangePlaymode is already false once leaving Play
        // mode has been asked for. Asking to enter at that moment cancels the exit and the run would hang.
        string queue = SessionState.GetString(k_QueueKey, "");
        if (queue == "" || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;

        if (queue == k_QueueEnd || EditorUtility.scriptCompilationFailed)
        {
            SessionState.EraseString(k_QueueKey);
            File.AppendAllText(k_LogPath, (queue == k_QueueEnd ? "" : "FAIL: the scripts do not compile\n") + "All done\n");
            return;
        }

        int comma = queue.IndexOf(',');
        string scenario = comma < 0 ? queue : queue.Substring(0, comma);
        SessionState.SetString(k_QueueKey, comma < 0 ? k_QueueEnd : queue.Substring(comma + 1));
        SessionState.SetString(k_PendingKey, scenario);

        // The game reads the saved best score as Play mode starts, so the scenarios that check the best-score
        // line get a known one here: none for hud, 40 for score-round. best then finds what score-round left.
        if (scenario == "hud")
            PlayerPrefs.DeleteKey(k_BestScoreKey);
        else if (scenario == "score-round" || scenario == "screens")
            PlayerPrefs.SetInt(k_BestScoreKey, 40);

        // The first-run panel covers the Start button, so it is marked as read for every scenario but the ones
        // that show it. onboarding-again runs right after onboarding and finds what that one left.
        if (scenario == "onboarding" || scenario == "portrait")
            PlayerPrefs.DeleteKey(k_SeenHowToPlayKey);
        else if (scenario != "onboarding-again")
            PlayerPrefs.SetInt(k_SeenHowToPlayKey, 1);

        // The portrait scenarios play on a Realme 7-sized Game view; the size it had is put back afterwards.
        if (scenario == "portrait" || scenario == "screens")
        {
            PlayModeWindow.GetRenderingResolution(out uint width, out uint height);
            SessionState.SetString(k_RestoreSizeKey, $"{width}x{height}");
            PlayModeWindow.SetCustomRenderingResolution(1080, 2400, "Realme 7");
        }

        // Sound and vibration start on in every scenario but pause-menu-again, which checks what pause-menu left.
        if (scenario != "pause-menu-again")
        {
            PlayerPrefs.DeleteKey("FruitDropAR.SoundOn");
            PlayerPrefs.DeleteKey("FruitDropAR.VibrationOn");
        }

        PlayerPrefs.Save();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>
    /// Enters Play mode, runs the scenario, then leaves Play mode again.
    /// </summary>
    public static void RunInPlayMode(string scenario)
    {
        if (EditorApplication.isPlaying)
        {
            Run(scenario);
            return;
        }

        File.WriteAllText(k_LogPath, "");
        SessionState.SetString(k_PendingKey, scenario);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>
    /// Starts a scenario. Each scenario yields the seconds of play time to wait before it continues.
    /// </summary>
    public static void Run(string scenario)
    {
        // A queued run keeps one log for all of its scenarios.
        if (SessionState.GetString(k_QueueKey, "") == "")
            File.WriteAllText(k_LogPath, "");

        if (!EditorApplication.isPlaying)
        {
            Log("FAIL: enter Play mode first");
            return;
        }

        s_Routine = scenario switch
        {
            "floor-edge" => FloorEdge(),
            "drop" => Drop(),
            "marker" => Marker(),
            "spawner" => Spawner(false),
            "play" => Spawner(true),
            "states" => States(),
            "flow" => Flow(),
            "hud" => Hud(),
            "score-round" => ScoreRound(),
            "best" => Best(),
            "curve" => Curve(),
            "picks" => Picks(),
            "long-round" => LongRound(),
            "variety" => Variety(),
            "feedback" => Feedback(),
            "onboarding" => Onboarding(true),
            "onboarding-again" => Onboarding(false),
            "point-memory" => PointMemory(),
            "portrait" => Portrait(),
            "mobile" => Mobile(),
            "pause-menu" => PauseMenuRound(),
            "pause-menu-again" => PauseMenuAgain(),
            "hud-feedback" => HudFeedback(),
            "screens" => Screens(),
            "scene-polish" => ScenePolish(),
            "human-round" => HumanRounds(3),
            _ => null,
        };

        if (s_Routine == null)
        {
            Log($"FAIL: unknown scenario '{scenario}'");
            return;
        }

        Log($"Running {scenario}");
        s_ResumeTime = 0f;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || s_Routine == null)
        {
            Stop();
            return;
        }

        if (Time.time < s_ResumeTime)
            return;

        bool running;
        try
        {
            running = s_Routine.MoveNext();
        }
        catch (Exception e)
        {
            Log($"FAIL: {e}");
            Stop();
            return;
        }

        if (!running)
        {
            // The frame rate shows whether the editor was held back, which makes the timings unreliable.
            Log($"Done: {Time.time:F0} s of play in {Time.realtimeSinceStartup:F0} s, {Time.frameCount / Mathf.Max(1f, Time.realtimeSinceStartup):F0} frames a second");
            Stop();
            return;
        }

        s_ResumeTime = Time.time + s_Routine.Current;
    }

    static void Stop()
    {
        EditorApplication.update -= Tick;
        s_Routine = null;
        if (s_ExitWhenDone && EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();

        s_ExitWhenDone = false;
    }

    static void Log(string message)
    {
        Debug.Log($"[SimTest] {message}");
        File.AppendAllText(k_LogPath, message + "\n");
    }

    // The simulated device. XR Simulation reads this transform's pose every input update,
    // so setting it moves the AR camera as if the phone moved.
    static Transform simCamera => GameObject.Find("SimulationCamera").transform;

    static void LookAt(Vector3 point)
    {
        simCamera.rotation = Quaternion.LookRotation(point - Camera.main.transform.position);
    }

    static IEnumerator<float> WaitForPlaneFloor(FloorBasketFollower follower)
    {
        // XR Simulation only rescans the room when the camera moves, so sweep it slowly across the floor.
        // Now and then the simulation hands out no planes at all in a session (2 of 25 scenarios in one full run,
        // both fine when run again). After 15 s the AR session is reset once, and the log says so.
        float start = Time.time;
        float startYaw = simCamera.eulerAngles.y;
        bool reset = false;
        while (!follower.hasPlaneFloor && Time.time - start < 45f)
        {
            if (!reset && Time.time - start > 15f)
            {
                Log("No plane after 15 s: resetting the AR session once");
                Object.FindAnyObjectByType<ARSession>().Reset();
                reset = true;
            }

            simCamera.rotation = Quaternion.Euler(40f, startYaw + 25f * Mathf.Sin(Time.time - start), 0f);
            yield return 0.25f;
        }

        simCamera.rotation = Quaternion.Euler(40f, startYaw, 0f);

        Log(follower.hasPlaneFloor
            ? $"Plane floor after {Time.time - start:F1} s{(reset ? " (after the reset)" : "")}"
            : "FAIL: no plane floor after 45 s, even after a session reset");
    }

    /// <summary>
    /// Sweeps the camera left and right, near and far, like a player scanning the room before a round,
    /// so the detected floor grows beyond the first patch. Ends facing the starting direction.
    /// </summary>
    static IEnumerator<float> ScanRoom(float seconds)
    {
        float start = Time.time;
        float startYaw = simCamera.eulerAngles.y;
        while (Time.time - start < seconds)
        {
            float t = Time.time - start;
            simCamera.rotation = Quaternion.Euler(35f + 15f * Mathf.Sin(t * 1.3f), startYaw + 60f * Mathf.Sin(t * 0.8f), 0f);
            yield return 0.1f;
        }

        simCamera.rotation = Quaternion.Euler(40f, startYaw, 0f);
        float area = 0f;
        int count = 0;
        foreach (var plane in Object.FindAnyObjectByType<ARPlaneManager>().trackables)
        {
            if (plane.alignment != PlaneAlignment.HorizontalUp || plane.subsumedBy != null)
                continue;

            area += plane.size.x * plane.size.y;
            count++;
        }

        Log($"Scanned for {seconds:F0} s: {count} upward planes, about {area:F1} m² (bounding sizes)");
    }

    /// <summary>
    /// V0.3 regression check: the floor height from planes, and where the basket stops when the screen centre
    /// points far past the edge of the detected floor in four directions.
    /// </summary>
    static IEnumerator<float> FloorEdge()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var planeManager = Object.FindAnyObjectByType<ARPlaneManager>();
        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        // Let the floor height glide onto the plane estimate.
        yield return 3f;
        var camera = Camera.main.transform;
        Log($"Floor y {follower.floorHeight:F3}, phone {camera.position.y - follower.floorHeight:F2} m above it");

        var startRotation = simCamera.rotation;
        for (int i = 0; i < 4; i++)
        {
            var direction = Quaternion.Euler(0f, 90f * i, 0f) * Vector3.forward;
            var farPoint = camera.position + direction * 10f;
            farPoint.y = follower.floorHeight;
            LookAt(farPoint);
            yield return 2f;

            var basket = follower.basket.transform.position;
            var flat = basket - camera.position;
            flat.y = 0f;
            Log($"Yaw {90 * i}: basket {flat.magnitude:F2} m away, {DescribeEdgeDistance(planeManager, follower, basket)}");
            Log($"  far point on floor: {follower.IsOnDetectedFloor(farPoint)}, clamped far point on floor: {follower.IsOnDetectedFloor(follower.ClampToFloor(farPoint))}");
        }

        // A spot on the floor in front of the start pose should count as floor.
        simCamera.rotation = startRotation;
        yield return 2f;
        Log($"Basket at start pose on floor: {follower.IsOnDetectedFloor(follower.basket.transform.position)}");
    }

    /// <summary>
    /// V0.4 steps 3 and 4: single fruit dropped from the rooftop onto the basket, beside it, and with a pause.
    /// </summary>
    static IEnumerator<float> Drop()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        yield return 2f;
        var fruitType = AssetDatabase.LoadAssetAtPath<FruitType>("Assets/Data/RedApple.asset");
        var fruit = Object.Instantiate(fruitType.prefab).GetComponent<Fruit>();
        fruit.gameObject.SetActive(false);
        const float rooftop = 2.5f;

        bool? caught = null;
        float launchTime = 0f;
        float landTime = 0f;
        Vector3 landPosition = default;
        void Landed(Fruit f, bool c)
        {
            caught = c;
            landTime = Time.time;
            landPosition = f.transform.position;
        }

        // 1. Straight above the basket: caught.
        var basket = follower.basket.transform.position;
        caught = null;
        launchTime = Time.time;
        fruit.Launch(fruitType, new Vector3(basket.x, follower.floorHeight + rooftop, basket.z), 1f, follower, Landed);
        while (caught == null && Time.time - launchTime < 6f)
            yield return 0.1f;
        Log($"Above basket: caught {caught}, fell for {landTime - launchTime:F2} s");

        // 2. 0.4 m beside the basket: missed on the floor.
        basket = follower.basket.transform.position;
        caught = null;
        launchTime = Time.time;
        fruit.Launch(fruitType, new Vector3(basket.x + 0.4f, follower.floorHeight + rooftop, basket.z), 1f, follower, Landed);
        while (caught == null && Time.time - launchTime < 6f)
            yield return 0.1f;
        Log($"0.4 m beside basket: caught {caught}, fell for {landTime - launchTime:F2} s, landed {landPosition.y - follower.floorHeight:F3} m above the floor");

        // 3. Paused for 2 s halfway down: hangs still, then is caught.
        basket = follower.basket.transform.position;
        caught = null;
        launchTime = Time.time;
        fruit.Launch(fruitType, new Vector3(basket.x, follower.floorHeight + rooftop, basket.z), 1f, follower, Landed);
        yield return 1f;
        fruit.isPaused = true;
        float pausedY = fruit.transform.position.y;
        yield return 2f;
        Log($"Paused: moved {Mathf.Abs(fruit.transform.position.y - pausedY):F4} m in 2 s");
        fruit.isPaused = false;
        while (caught == null && Time.time - launchTime < 8f)
            yield return 0.1f;
        Log($"After pause: caught {caught}, fell for {landTime - launchTime:F2} s including the pause");

        Object.Destroy(fruit.gameObject);
    }

    // Colour the ring is drawn with: the property block's if the marker set one, otherwise the material's.
    static Color RingColor(LandingMarker marker)
    {
        var renderer = marker.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        int id = Shader.PropertyToID("_BaseColor");
        return block.HasProperty(id) ? block.GetColor(id) : renderer.sharedMaterial.GetColor(id);
    }

    /// <summary>
    /// V0.4 step 5 and V0.6 step 4: the landing ring stays on the floor under a falling fruit, closes in from
    /// twice the catch size to the catch size as the fruit comes down, is red under rotten fruit, and disappears
    /// with the fruit. A red apple and a rotten apple fall side by side; a Game view capture is saved with both
    /// held low in view.
    /// </summary>
    static IEnumerator<float> Marker()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        yield return 2f;
        const float rooftop = 2.5f;
        const float catchSize = 0.30f;
        string[] names = { "RedApple", "RottenApple" };
        var types = new FruitType[names.Length];
        var fruits = new Fruit[names.Length];
        var markers = new LandingMarker[names.Length];
        var landed = new bool[names.Length];
        var startSize = new float[names.Length];
        var lastSize = new float[names.Length];
        var camera = Camera.main.transform;
        var forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, forward);
        for (int i = 0; i < names.Length; i++)
        {
            types[i] = AssetDatabase.LoadAssetAtPath<FruitType>($"Assets/Data/{names[i]}.asset");
            fruits[i] = Object.Instantiate(types[i].prefab).GetComponent<Fruit>();
            markers[i] = fruits[i].GetComponentInChildren<LandingMarker>(true);
            if (markers[i] == null)
            {
                Log($"FAIL: the {names[i]} prefab has no LandingMarker");
                yield break;
            }

            var spot = camera.position + forward * 1.0f + right * (i == 0 ? 0.3f : -0.3f);
            spot.y = follower.floorHeight;
            int index = i;
            fruits[i].Launch(types[i], spot + Vector3.up * rooftop, 1f, follower, (f, c) => landed[index] = true);
        }

        // Two frames, so the rings have been placed at least once.
        yield return 0f;
        yield return 0f;
        for (int i = 0; i < names.Length; i++)
            startSize[i] = markers[i].transform.lossyScale.x;

        while (fruits[0].transform.position.y > follower.floorHeight + 0.5f && fruits[1].transform.position.y > follower.floorHeight + 0.5f)
            yield return 0f;

        foreach (var fruit in fruits)
            fruit.isPaused = true;

        yield return 0.5f;
        for (int i = 0; i < names.Length; i++)
        {
            var markerPosition = markers[i].transform.position;
            var fruitPosition = fruits[i].transform.position;
            float height = fruitPosition.y - follower.floorHeight;
            float expected = catchSize * (2f - (rooftop - height) / (rooftop - types[i].radius));
            Log($"{types[i].displayName}: ring {Vector2.Distance(new Vector2(markerPosition.x, markerPosition.z), new Vector2(fruitPosition.x, fruitPosition.z)):F4} m from under the fruit, " +
                $"{markerPosition.y - follower.floorHeight:F4} m above the floor, {markers[i].transform.lossyScale.x:F3} m across with the fruit {height:F2} m up " +
                $"(worked by hand {expected:F3}), colour #{ColorUtility.ToHtmlStringRGB(RingColor(markers[i]))}");
        }

        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-marker.png"));
        yield return 1f;

        foreach (var fruit in fruits)
            fruit.isPaused = false;

        while (!landed[0] || !landed[1])
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (!landed[i])
                    lastSize[i] = markers[i].transform.lossyScale.x;
            }

            yield return 0f;
        }

        for (int i = 0; i < names.Length; i++)
        {
            Log($"{types[i].displayName}: ring {startSize[i]:F3} m across on the rooftop, {lastSize[i]:F3} m just before landing; " +
                $"after landing visible {markers[i].gameObject.activeInHierarchy}");
            Object.Destroy(fruits[i].gameObject);
        }
    }

    /// <summary>
    /// V0.6 step 5: every fruit type held in the air in a row ahead of the camera, each over its ring, for one
    /// Game view capture of how they look next to each other.
    /// </summary>
    static IEnumerator<float> Variety()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        yield return 2f;
        var types = SpawnerTypes(Object.FindAnyObjectByType<FruitSpawner>());
        var camera = Camera.main.transform;
        var forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, forward);
        var fruits = new List<Fruit>();
        var parts = new List<string>();
        for (int i = 0; i < types.Length; i++)
        {
            var fruit = Object.Instantiate(types[i].prefab).GetComponent<Fruit>();
            var spot = camera.position + forward * 1.3f + right * ((i - (types.Length - 1) * 0.5f) * 0.36f);
            spot.y = follower.floorHeight;
            fruit.Launch(types[i], spot + Vector3.up * 2.5f, 1f, follower, null);

            // Held at a different height each, so the rings show different stages of closing in.
            fruit.isPaused = true;
            fruit.transform.position = spot + Vector3.up * (0.30f + 0.06f * i);
            fruits.Add(fruit);
        }

        yield return 0.5f;

        // The start screen's title would cover the row in the editor's short landscape view.
        var title = FindDeep("Game Title");
        if (title != null)
            title.gameObject.SetActive(false);

        foreach (var fruit in fruits)
        {
            var marker = fruit.GetComponentInChildren<LandingMarker>(true);
            parts.Add($"{fruit.type.displayName} (ring {marker.transform.lossyScale.x:F2} m, #{ColorUtility.ToHtmlStringRGB(RingColor(marker))})");
        }

        Log($"In view: {string.Join(", ", parts)}");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-variety.png"));
        yield return 1f;
        foreach (var fruit in fruits)
            Object.Destroy(fruit.gameObject);
    }

    /// <summary>
    /// V0.4 step 6: 20 s with the phone turned so the basket goes under each new landing spot (all caught),
    /// then 12 s looking straight down so the basket stays 0.4 m away (all missed).
    /// Every landing spot is checked against the distance band and angle limit.
    /// With <paramref name="fullRound"/> (V0.4 step 9) the round starts from the Start button once the game is
    /// Ready, a frame is captured mid-round, and errors logged during the run are counted.
    /// </summary>
    static IEnumerator<float> Spawner(bool fullRound)
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        if (spawner == null)
        {
            Log("FAIL: no FruitSpawner in the scene");
            yield break;
        }

        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        yield return 2f;
        var seen = new HashSet<Fruit>();
        var typeCounts = new SortedDictionary<string, int>();
        var camera = Camera.main.transform;
        float minDistance = float.MaxValue, maxDistance = 0f, maxAngle = 0f;
        int spawned = 0;
        int errors = 0;
        bool captured = !fullRound;
        Application.LogCallback countErrors = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors++;
        };
        Application.logMessageReceived += countErrors;

        IEnumerator<float> Watch(float seconds, bool aim)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                // Rotten fruit is left alone, as a player would.
                var nextToLand = NextToLand(spawner, follower, true);
                foreach (var fruit in spawner.GetComponentsInChildren<Fruit>())
                {
                    if (!seen.Add(fruit))
                        continue;

                    spawned++;
                    typeCounts.TryGetValue(fruit.type.displayName, out int typeCount);
                    typeCounts[fruit.type.displayName] = typeCount + 1;
                    var spot = fruit.transform.position;
                    spot.y = follower.floorHeight;
                    var flat = spot - camera.position;
                    flat.y = 0f;
                    var forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
                    if (forward.sqrMagnitude < 0.0001f)
                        forward = Vector3.ProjectOnPlane(camera.up, Vector3.up);
                    minDistance = Mathf.Min(minDistance, flat.magnitude);
                    maxDistance = Mathf.Max(maxDistance, flat.magnitude);
                    maxAngle = Mathf.Max(maxAngle, Vector3.Angle(forward, flat));
                }

                // Fruit falls for longer than the gap between drops, so follow the one that lands first.
                if (aim && nextToLand != null)
                {
                    var spot = nextToLand.transform.position;
                    spot.y = follower.floorHeight;
                    LookAt(spot);

                    // Capture once a fruit is low enough to be in view above its marker and the basket.
                    if (!captured && nextToLand.transform.position.y < follower.floorHeight + 0.5f)
                    {
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-play.png"));
                        captured = true;
                    }
                }

                // A pooled fruit is reused for a later drop, so forget fruit that has landed.
                seen.RemoveWhere(f => !f.gameObject.activeSelf);
                yield return 0f;
            }
        }

        if (fullRound)
        {
            var game = Object.FindAnyObjectByType<GameStateManager>();
            float readyWait = Time.time;
            while (game.state != GameStateManager.GameState.Ready && Time.time - readyWait < 10f)
                yield return 0.25f;

            if (game.state != GameStateManager.GameState.Ready)
            {
                Log($"FAIL: game is {game.state}, not Ready, after scanning");
                Application.logMessageReceived -= countErrors;
                yield break;
            }

            FindButton("Start Button").onClick.Invoke();
            var countdown = WaitForPlaying(game);
            while (countdown.MoveNext())
                yield return countdown.Current;

            Log($"Pressed Start: game {game.state}, spawning {spawner.isSpawning}");
        }
        else
        {
            StopScoring();
            spawner.StartSpawning();
        }

        var aimed = Watch(20f, true);
        while (aimed.MoveNext())
            yield return aimed.Current;

        Log($"Aimed for 20 s: {spawned} dropped, {spawner.caughtCount} caught, {spawner.missedCount} missed");
        int caughtBefore = spawner.caughtCount, missedBefore = spawner.missedCount, spawnedBefore = spawned;

        simCamera.rotation = Quaternion.Euler(89f, simCamera.eulerAngles.y, 0f);
        var away = Watch(12f, false);
        while (away.MoveNext())
            yield return away.Current;

        // Let the last fruit finish falling.
        yield return 3.5f;
        Log($"Looking down for 12 s: {spawned - spawnedBefore} dropped, {spawner.caughtCount - caughtBefore} caught, {spawner.missedCount - missedBefore} missed");
        Log($"Landing spots {minDistance:F2}-{maxDistance:F2} m away, at most {maxAngle:F1} degrees off straight ahead");
        var typeList = new List<string>();
        foreach (var pair in typeCounts)
            typeList.Add($"{pair.Key} {pair.Value}");

        Log($"Types dropped: {string.Join(", ", typeList)}");
        Application.logMessageReceived -= countErrors;
        Log($"Errors logged during the round: {errors}");
        spawner.StopSpawning();
    }

    /// <summary>
    /// V0.4 step 7: Scanning until there is floor ahead, Ready with the Start button, Playing after pressing it,
    /// and paused fruit while tracking is lost (simulated by turning the AR session off).
    /// </summary>
    static IEnumerator<float> States()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var button = FindButton("Start Button");
        yield return 0.5f;
        Log($"At start: {game.state}, Start button shown {button.gameObject.activeInHierarchy}, hint \"{Hint()}\"");

        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        yield return 0.5f;
        Log($"Just after the first plane: {game.state}, Start button shown {button.gameObject.activeInHierarchy}, hint \"{Hint()}\"");
        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        yield return 1f;
        Log($"After scanning: {game.state}, Start button shown {button.gameObject.activeInHierarchy}, hint \"{Hint()}\"");
        if (game.state != GameStateManager.GameState.Ready)
            yield break;

        button.onClick.Invoke();
        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        Log($"After pressing Start: {game.state}, spawning {spawner.isSpawning}, Start button shown {button.gameObject.activeInHierarchy}, hint \"{Hint()}\"");

        // Wait for a fruit to be part-way down, then lose tracking.
        Fruit falling = null;
        float waitStart = Time.time;
        while (falling == null && Time.time - waitStart < 6f)
        {
            foreach (var fruit in spawner.GetComponentsInChildren<Fruit>())
            {
                if (fruit.transform.position.y < follower.floorHeight + 2f)
                    falling = fruit;
            }

            yield return 0f;
        }

        if (falling == null)
        {
            Log("FAIL: no fruit fell within 6 s of starting");
            yield break;
        }

        var session = Object.FindAnyObjectByType<ARSession>();
        session.enabled = false;
        yield return 2f;
        float heldY = falling.transform.position.y;
        yield return 2f;
        Log($"Tracking off for 4 s: session {ARSession.state}, basket shown {follower.isBasketVisible}, paused {game.isPaused}, " +
            $"fruit moved {Mathf.Abs(falling.transform.position.y - heldY):F4} m in the last 2 s, hint \"{Hint()}\"");

        session.enabled = true;
        float resumeStart = Time.time;
        while (game.isPaused && Time.time - resumeStart < 15f)
            yield return 0.05f;

        // Tracking is back: a 3-2-1 first, with the fruit still held.
        float resumingAfter = Time.time - resumeStart;
        heldY = falling.transform.position.y;
        yield return 1f;
        Log($"Tracking back after {resumingAfter:F1} s: {game.state}, {game.countdownRemaining:F2} s to go, fruit moved {Mathf.Abs(falling.transform.position.y - heldY):F4} m in 1 s, " +
            $"countdown \"{HudText("Countdown Text")}\", hint \"{Hint()}\"");
        while (game.state == GameStateManager.GameState.Resuming && Time.time - resumeStart < 15f)
            yield return 0f;

        float playingAfter = Time.time - resumeStart - resumingAfter;
        heldY = falling.transform.position.y;
        yield return 0.5f;
        Log($"Resume countdown over after {playingAfter:F2} s: {game.state}, paused {game.isPaused}, fruit moved {Mathf.Abs(falling.transform.position.y - heldY):F3} m in 0.5 s, hint \"{Hint()}\"");
        Log($"Debug lines: {StatusText().Replace('\n', '|')}");
        spawner.StopSpawning();
    }

    /// <summary>
    /// Waits out the 3-2-1 countdown after Start or Restart, and logs how long it took.
    /// </summary>
    static IEnumerator<float> WaitForPlaying(GameStateManager game)
    {
        float start = Time.time;
        while (game.state == GameStateManager.GameState.Countdown && Time.time - start < 10f)
            yield return 0f;

        Log($"Countdown over after {Time.time - start:F2} s: {game.state}");
    }

    /// <summary>
    /// V0.5 step 2: the whole flow. Start refused before a plane floor; countdown, held while tracking is lost;
    /// Playing and Paused; Game Over after the third miss; Restart into a fresh countdown with full lives.
    /// </summary>
    static IEnumerator<float> Flow()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();
        var session = Object.FindAnyObjectByType<ARSession>();
        if (score == null)
        {
            Log("FAIL: no ScoreManager in the scene");
            yield break;
        }

        yield return 0.5f;
        game.StartRound();
        yield return 0.1f;
        Log($"Start before any plane: {game.state}, floor {follower.floorSource}");

        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        yield return 1f;
        if (game.state != GameStateManager.GameState.Ready)
        {
            Log($"FAIL: game is {game.state}, not Ready, after scanning");
            yield break;
        }

        Log($"Floor y {follower.floorHeight:F3}");
        var startRotation = simCamera.rotation;
        FindButton("Start Button").onClick.Invoke();
        yield return 0f;
        Log($"Pressed Start: {game.state}, {game.countdownRemaining:F2} s to go, spawning {spawner.isSpawning}, score {score.score}, lives {score.lives}");

        // The status text refreshes every 0.25 s.
        yield return 0.3f;
        Log($"  hint \"{Hint()}\"");

        // Lose tracking 1 s into the countdown: it should hold, then finish once tracking is back.
        yield return 0.7f;
        session.enabled = false;
        yield return 2f;
        float held = game.countdownRemaining;
        yield return 1f;
        Log($"Tracking off during countdown: {game.state}, paused {game.isPaused}, countdown moved {held - game.countdownRemaining:F3} s in 1 s, spawning {spawner.isSpawning}");
        session.enabled = true;
        float resumeStart = Time.time;
        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        Log($"  (tracking back to Playing took {Time.time - resumeStart:F2} s), spawning {spawner.isSpawning}");

        // Start pressed again mid-round does nothing.
        game.StartRound();
        Log($"Start during the round: {game.state}");

        // Pause and resume as its own state.
        yield return 1.5f;
        session.enabled = false;
        float pauseStart = Time.time;
        while (game.state != GameStateManager.GameState.Paused && Time.time - pauseStart < 10f)
            yield return 0.1f;
        float pausedAfter = Time.time - pauseStart;
        yield return 0.3f;
        Log($"Tracking off while playing: {game.state} after {pausedAfter:F1} s, spawner paused {spawner.isPaused}, hint \"{Hint()}\"");
        session.enabled = true;
        resumeStart = Time.time;
        while (game.state == GameStateManager.GameState.Paused && Time.time - resumeStart < 15f)
            yield return 0.1f;
        Log($"Tracking back: {game.state} after {Time.time - resumeStart:F1} s, {game.countdownRemaining:F2} s to go, spawner paused {spawner.isPaused}");

        // Tracking lost again in the middle of the resume countdown: back to Paused, and a fresh 3-2-1 after.
        yield return 1f;
        session.enabled = false;
        pauseStart = Time.time;
        while (game.state != GameStateManager.GameState.Paused && Time.time - pauseStart < 10f)
            yield return 0.1f;
        Log($"Tracking off during the resume countdown: {game.state}, spawner paused {spawner.isPaused}");
        session.enabled = true;
        resumeStart = Time.time;
        while (game.state != GameStateManager.GameState.Resuming && Time.time - resumeStart < 15f)
            yield return 0.05f;
        float freshCountdown = game.countdownRemaining;
        while (game.state == GameStateManager.GameState.Resuming && Time.time - resumeStart < 15f)
            yield return 0f;
        Log($"Tracking back: a fresh countdown of {freshCountdown:F2} s, then {game.state} after {Time.time - resumeStart:F1} s, spawner paused {spawner.isPaused}");

        // The floor falls back to a rougher estimate while tracking carries on. The follower keeps a plane floor
        // once it has one and only starts over after a long tracking loss, which the simulation cannot produce
        // mid-round. So it is staged: the follower is switched off with its floor source set by hand, first
        // for a flicker shorter than the grace time, then for good, and then switched on again.
        var sourceField = typeof(FloorBasketFollower).GetField("m_FloorSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        yield return 1f;
        follower.enabled = false;
        sourceField.SetValue(follower, FloorBasketFollower.FloorSource.Assumed);
        yield return 0.3f;
        var afterFlicker = game.state;
        sourceField.SetValue(follower, FloorBasketFollower.FloorSource.Plane);
        yield return 0.3f;
        sourceField.SetValue(follower, FloorBasketFollower.FloorSource.Assumed);
        pauseStart = Time.time;
        while (game.state != GameStateManager.GameState.Paused && Time.time - pauseStart < 5f)
            yield return 0f;
        Log($"Floor not a plane for 0.3 s: still {afterFlicker}. Not a plane for longer: {game.state} after {Time.time - pauseStart:F2} s, " +
            $"basket shown {follower.isBasketVisible}, floor {follower.floorSource}, spawner paused {spawner.isPaused}");
        follower.enabled = true;
        resumeStart = Time.time;
        while (game.state != GameStateManager.GameState.Playing && Time.time - resumeStart < 15f)
            yield return 0f;
        Log($"Plane floor back: {game.state} after {Time.time - resumeStart:F1} s, floor {follower.floorSource}, spawner paused {spawner.isPaused}");

        // Look straight down so every fruit misses, until the lives run out.
        simCamera.rotation = Quaternion.Euler(89f, simCamera.eulerAngles.y, 0f);
        float roundStart = Time.time;
        while (game.state != GameStateManager.GameState.GameOver && Time.time - roundStart < 30f)
            yield return 0.1f;

        yield return 0.5f;
        Log($"Looking down: {game.state} after {Time.time - roundStart - 0.5f:F1} s, {spawner.missedCount} missed, {spawner.caughtCount} caught, " +
            $"lives {score.lives}, score {score.score}, best {score.highScore}, spawning {spawner.isSpawning}, " +
            $"fruit left {spawner.GetComponentsInChildren<Fruit>().Length}, hint \"{Hint()}\"");

        // Nothing more is dropped after Game Over.
        yield return 3f;
        Log($"3 s after Game Over: {game.state}, fruit left {spawner.GetComponentsInChildren<Fruit>().Length}, missed {spawner.missedCount}");

        game.StartRound();
        Log($"Start at Game Over: {game.state}");

        simCamera.rotation = startRotation;
        yield return 1f;
        game.Restart();
        yield return 0f;
        Log($"Restart: {game.state}, {game.countdownRemaining:F2} s to go, score {score.score}, lives {score.lives}, streak {score.streak}");
        countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        Log($"  spawning {spawner.isSpawning}, caught {spawner.caughtCount}, missed {spawner.missedCount}");
        yield return 0.3f;
        Log($"  hint \"{Hint()}\"");
        spawner.StopSpawning();
    }

    /// <summary>
    /// V0.5 step 3: the HUD through a round. Taps reach the Start and Restart buttons; the countdown, score,
    /// multiplier and hearts show what ScoreManager holds; the Game Over panel shows the score and the best;
    /// Restart clears it all. Saves Game view captures of the countdown, the round and Game Over.
    /// </summary>
    static IEnumerator<float> Hud()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();
        var hud = Object.FindAnyObjectByType<GameHUD>();
        if (hud == null)
        {
            Log("FAIL: no GameHUD in the scene");
            yield break;
        }

        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        yield return 1f;
        if (game.state != GameStateManager.GameState.Ready)
        {
            Log($"FAIL: game is {game.state}, not Ready, after scanning");
            yield break;
        }

        var startButton = FindButton("Start Button");
        var restartButton = FindButton("Restart Button");
        Log($"Ready: a tap on Start reaches it {CanTap(startButton)}, round bar shown {FindActive("Round Bar")}");
        int bestBefore = score.highScore;

        // Red apples only, so the score can be worked out by hand from the number of catches.
        spawner.QueueTypes(Repeat("RedApple", 60));
        startButton.onClick.Invoke();
        yield return 0.5f;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-hud-countdown.png"));
        Log($"Countdown: number \"{HudText("Countdown Text")}\", round bar shown {FindActive("Round Bar")}, score \"{HudText("Score Text")}\", " +
            $"combo \"{HudText("Combo Text")}\", hearts {Hearts()}");

        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        // Follow the fruit that lands next, so every fruit is caught and the combo builds.
        var camera = Camera.main.transform;
        float aimEnd = Time.time + 16f;
        while (Time.time < aimEnd)
        {
            var nextToLand = NextToLand(spawner, follower, false);
            if (nextToLand != null)
            {
                var spot = nextToLand.transform.position;
                spot.y = follower.floorHeight;
                LookAt(spot);
            }

            yield return 0f;
        }

        // The score text counts up over 0.3 s since V1.0 polish step 3, so let it catch up before reading it.
        float settle = Time.time;
        while (HudText("Score Text") != score.score.ToString() && Time.time - settle < 1f)
            yield return 0f;

        // The hand-worked total: catches 1-5 score 10 each, catches 6-10 score 20 each, and so on.
        int caught = spawner.caughtCount;
        int expected = 0;
        for (int i = 0; i < caught; i++)
            expected += 10 * Mathf.Min(5, 1 + i / 5);

        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-hud-playing.png"));
        yield return 0f;
        Log($"Aimed for 16 s: {caught} caught, {spawner.missedCount} missed, score {score.score} (worked by hand {expected}), multiplier {score.multiplier}");
        Log($"  HUD score \"{HudText("Score Text")}\", combo \"{HudText("Combo Text")}\", hearts {Hearts()}");

        simCamera.rotation = Quaternion.Euler(89f, simCamera.eulerAngles.y, 0f);
        int missesSeen = 0;
        float roundStart = Time.time;
        while (game.state != GameStateManager.GameState.GameOver && Time.time - roundStart < 30f)
        {
            if (spawner.missedCount != missesSeen)
            {
                missesSeen = spawner.missedCount;
                yield return 0f;
                Log($"  miss {missesSeen}: lives {score.lives}, hearts {Hearts()}, combo \"{HudText("Combo Text")}\"");
            }

            yield return 0f;
        }

        yield return 0.3f;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-hud-gameover.png"));
        yield return 0f;
        Log($"Game Over: panel shown {FindActive("Game Over Panel")}, round bar shown {FindActive("Round Bar")}, final \"{HudText("Final Score Text")}\", " +
            $"best line \"{HudText("Best Text")}\" (best before the round {bestBefore}, now {score.highScore}), a tap on Restart reaches it {CanTap(restartButton)}");

        // A fresh session keeps the best score, which is what the Best line then shows.
        restartButton.onClick.Invoke();
        yield return 0.5f;
        Log($"Restart: {game.state}, panel shown {FindActive("Game Over Panel")}, round bar shown {FindActive("Round Bar")}, score \"{HudText("Score Text")}\", " +
            $"combo \"{HudText("Combo Text")}\", hearts {Hearts()}, countdown \"{HudText("Countdown Text")}\"");
        countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        spawner.StopSpawning();
    }

    /// <summary>
    /// Scans, waits for Ready and presses Start, then waits out the countdown. Shared by the V0.5 step 4 scenarios.
    /// </summary>
    static IEnumerator<float> ScanAndStart(FloorBasketFollower follower, GameStateManager game)
    {
        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        float readyWait = Time.time;
        while (game.state != GameStateManager.GameState.Ready && Time.time - readyWait < 10f)
            yield return 0.25f;

        if (game.state != GameStateManager.GameState.Ready)
        {
            Log($"FAIL: game is {game.state}, not Ready, after scanning");
            yield break;
        }

        FindButton("Start Button").onClick.Invoke();
        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;
    }

    /// <summary>
    /// Plays a round to a plan, one letter per fruit in landing order: C to catch it, M to let it fall.
    /// Catching means turning the phone so the basket sits under the fruit; missing means looking straight down.
    /// Returns once the round is over or the plan runs out.
    /// </summary>
    static IEnumerator<float> PlayPlan(string plan, FloorBasketFollower follower, FruitSpawner spawner, GameStateManager game, System.Text.StringBuilder outcomes, float holdAfterLanding = 0f)
    {
        // With a hold, the camera stays where it was for a moment after each fruit lands, so a capture taken
        // then still shows the spot.
        float holdUntil = 0f;
        void Landed(Fruit fruit, bool caught)
        {
            outcomes.Append(caught ? 'C' : 'M');
            holdUntil = Time.time + holdAfterLanding;
        }

        spawner.fruitLanded += Landed;
        float start = Time.time;
        while (outcomes.Length < plan.Length && game.state != GameStateManager.GameState.GameOver && Time.time - start < 60f)
        {
            if (Time.time < holdUntil)
            {
                yield return 0f;
                continue;
            }

            var nextToLand = NextToLand(spawner, follower, false);
            if (nextToLand != null && plan[outcomes.Length] == 'C')
            {
                var spot = nextToLand.transform.position;
                spot.y = follower.floorHeight;
                LookAt(spot);
            }
            else if (nextToLand != null)
            {
                simCamera.rotation = Quaternion.Euler(89f, simCamera.eulerAngles.y, 0f);
            }

            yield return 0f;
        }

        spawner.fruitLanded -= Landed;
    }

    /// <summary>
    /// The falling fruit that reaches the floor first, going by height and speed, or null when none is falling.
    /// </summary>
    static Fruit NextToLand(FruitSpawner spawner, FloorBasketFollower follower, bool skipRotten)
    {
        Fruit next = null;
        float nextSeconds = float.MaxValue;
        foreach (var fruit in spawner.GetComponentsInChildren<Fruit>())
        {
            if (skipRotten && fruit.type.isHazard)
                continue;

            float seconds = (fruit.transform.position.y - follower.floorHeight) / Mathf.Max(0.01f, fruit.speed);
            if (seconds < nextSeconds)
            {
                nextSeconds = seconds;
                next = fruit;
            }
        }

        return next;
    }

    // For scenarios that start the spawner without a round. The score would still count every fruit, and a
    // long run would leave the editor with a best score no real round could reach.
    static void StopScoring()
    {
        var score = Object.FindAnyObjectByType<ScoreManager>();
        if (score != null)
            score.enabled = false;
    }

    static FruitType LoadType(string name) => AssetDatabase.LoadAssetAtPath<FruitType>($"Assets/Data/{name}.asset");

    static IEnumerable<FruitType> Repeat(string name, int count)
    {
        var fruitType = LoadType(name);
        for (int i = 0; i < count; i++)
            yield return fruitType;
    }

    /// <summary>
    /// V0.5 step 4 and V0.6 step 5: a scripted round of planned fruit, catches and misses. The score must match
    /// the total worked by hand, the third lost life must end the round, and Restart must reset everything but
    /// the best score. The fruit is lined up in this order, with what the camera does about each:
    /// <code>
    ///  1 red     catch  10          streak 1
    ///  2 red     catch  20          streak 2
    ///  3 golden  catch  120         streak 5 (golden adds 2 more), so the next catch scores x2
    ///  4 red     catch  140  (x2)   streak 6
    ///  5 rotten  let fall: nothing changes
    ///  6 red     catch  160  (x2)   streak 7
    ///  7 rotten  catch  140  (-20)  streak 0, 2 lives
    ///  8 red     miss               1 life
    ///  9 green   catch  155         streak 1
    /// 10 orange  catch  175         streak 2
    /// 11 lemon   catch  205         streak 3
    /// 12 red     miss               0 lives: Game Over on 205
    /// </code>
    /// </summary>
    static IEnumerator<float> ScoreRound()
    {
        const string plan = "CCCCMCCMCCCM";
        const int expected = 205;
        string[] order = { "RedApple", "RedApple", "GoldenApple", "RedApple", "RottenApple", "RedApple", "RottenApple", "RedApple", "GreenApple", "Orange", "Lemon", "RedApple" };
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();
        Log($"Best score at start: {score.highScore}");
        var planned = new List<FruitType>();
        foreach (string name in order)
            planned.Add(LoadType(name));

        spawner.QueueTypes(planned);

        var start = ScanAndStart(follower, game);
        while (start.MoveNext())
            yield return start.Current;

        if (game.state != GameStateManager.GameState.Playing)
            yield break;

        var outcomes = new System.Text.StringBuilder();
        var play = PlayPlan(plan, follower, spawner, game, outcomes);
        while (play.MoveNext())
            yield return play.Current;

        yield return 0.3f;
        Log($"Planned {plan}, got {outcomes}: {(outcomes.ToString() == plan ? "as planned" : "FAIL: not as planned")}");
        Log($"End: {game.state}, score {score.score} (worked by hand {expected}) {(score.score == expected ? "match" : "FAIL: mismatch")}, " +
            $"lives {score.lives}, best {score.highScore}, panel final \"{HudText("Final Score Text")}\", best line \"{HudText("Best Text")}\"");

        FindButton("Restart Button").onClick.Invoke();
        yield return 0.5f;
        Log($"Restart: {game.state}, score {score.score}, streak {score.streak}, lives {score.lives}, multiplier {score.multiplier}, best {score.highScore}, " +
            $"HUD score \"{HudText("Score Text")}\", hearts {Hearts()}");
        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        Log($"  caught {spawner.caughtCount}, missed {spawner.missedCount}");
        spawner.StopSpawning();
    }

    /// <summary>
    /// V0.5 step 4, run in a fresh Play mode after score-round: the best score was saved, and a weaker round
    /// shows it on the Game Over panel instead of a new best.
    /// </summary>
    static IEnumerator<float> Best()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();
        Log($"Fresh Play mode: best {score.highScore}, saved {PlayerPrefs.GetInt("FruitDropAR.HighScore", -1)}");
        spawner.QueueTypes(Repeat("RedApple", 8));

        var start = ScanAndStart(follower, game);
        while (start.MoveNext())
            yield return start.Current;

        if (game.state != GameStateManager.GameState.Playing)
            yield break;

        const string plan = "CMMM";
        var outcomes = new System.Text.StringBuilder();
        var play = PlayPlan(plan, follower, spawner, game, outcomes);
        while (play.MoveNext())
            yield return play.Current;

        yield return 0.3f;
        Log($"Planned {plan}, got {outcomes}; {game.state}, score {score.score} (worked by hand 10), best {score.highScore}, " +
            $"panel final \"{HudText("Final Score Text")}\", best line \"{HudText("Best Text")}\"");
    }

    /// <summary>
    /// V0.7: the right feedback for each event of a short planned round. A red apple is caught (pop), a red
    /// apple missed (thud), a rotten apple caught (buzz), a rotten apple left to fall (nothing), and a last red
    /// apple missed (thud), which ends the round (jingle). Every fruit gets a cue where it appears.
    /// </summary>
    static IEnumerator<float> Feedback()
    {
        const string plan = "CMCMM";
        const string expectedSounds = "CatchPop, MissThud, RottenBuzz, MissThud, GameOver";
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var audio = Object.FindAnyObjectByType<GameAudio>();
        if (audio == null)
        {
            Log("FAIL: no GameAudio in the scene");
            yield break;
        }

        var listeners = Object.FindObjectsByType<AudioListener>();
        Log($"Audio listeners: {listeners.Length}{(listeners.Length > 0 ? " (" + listeners[0].name + ")" : "")}");
        var sounds = new List<string>();
        int cues = 0, fruits = 0;
        float furthestCue = 0f;
        void Played(AudioClip clip)
        {
            if (clip.name == "SpawnCue")
                cues++;
            else
                sounds.Add(clip.name);
        }

        // GameAudio hears about a new fruit first, so by now a placed source should be playing where it is.
        void Spawned(Fruit fruit)
        {
            fruits++;
            float nearest = float.MaxValue;
            foreach (var source in audio.GetComponentsInChildren<AudioSource>())
            {
                if (source.spatialBlend > 0.5f && source.isPlaying)
                    nearest = Mathf.Min(nearest, Vector3.Distance(source.transform.position, fruit.transform.position));
            }

            furthestCue = Mathf.Max(furthestCue, nearest);
        }

        // Effects: the same round should give sparkles and a bounce for the catch, a splash and a buzz for each
        // miss, a splash, a bounce and a buzz for the rotten catch, and only a splash for the rotten apple left
        // to fall. The basket is watched for how far it squashes and whether it comes back.
        const string expectedEffects = "Sparkle, Bounce, Splash, Vibration, Splash, Bounce, Vibration, Splash, Splash, Vibration";
        var effects = Object.FindAnyObjectByType<CatchEffects>();
        var effectList = new List<string>();
        float lowestScale = 1f;
        Transform basketBody = null;
        void EffectPlayed(CatchEffects.Effect effect)
        {
            // A new best's confetti depends on the editor's saved best; the screens scenario checks it.
            if (effect == CatchEffects.Effect.Confetti)
                return;

            effectList.Add(effect.ToString());

            // One capture a moment after the first sparkle and one after the first splash, with particles in the air.
            int seen = effectList.FindAll(e => e == effect.ToString()).Count;
            if (seen == 1 && (effect == CatchEffects.Effect.Sparkle || effect == CatchEffects.Effect.Splash))
                CaptureAt(Time.time + 0.12f, $"Temp/SimTest-feedback-{effect.ToString().ToLowerInvariant()}.png");
        }

        void WatchBasket()
        {
            if (basketBody == null && follower.basket != null)
                basketBody = follower.basket.transform.Find("Basket");

            if (basketBody != null)
                lowestScale = Mathf.Min(lowestScale, basketBody.localScale.y);
        }

        if (effects != null)
            effects.played += EffectPlayed;

        EditorApplication.update += WatchBasket;
        audio.played += Played;
        spawner.fruitSpawned += Spawned;
        spawner.QueueTypes(new[] { LoadType("RedApple"), LoadType("RedApple"), LoadType("RottenApple"), LoadType("RottenApple"), LoadType("RedApple") });
        var start = ScanAndStart(follower, game);
        while (start.MoveNext())
            yield return start.Current;

        if (game.state != GameStateManager.GameState.Playing)
            yield break;

        var outcomes = new System.Text.StringBuilder();
        var play = PlayPlan(plan, follower, spawner, game, outcomes, 0.3f);
        while (play.MoveNext())
            yield return play.Current;

        // Long enough for the Game Over jingle, which waits a moment after the last miss.
        yield return 1.5f;
        audio.played -= Played;
        spawner.fruitSpawned -= Spawned;
        string got = string.Join(", ", sounds);
        Log($"Planned {plan} (red, red, rotten, rotten, red), got {outcomes}; {game.state}");
        Log($"Sounds: {got}: {(got == expectedSounds ? "as planned" : "FAIL: planned " + expectedSounds)}");
        Log($"Cues: {cues} for {fruits} fruits, each within {furthestCue:F3} m of where its fruit appeared");
        var clips = new List<string>();
        var serialized = new SerializedObject(audio);
        foreach (string field in new[] { "m_SpawnClip", "m_CatchClip", "m_MissClip", "m_HazardClip", "m_GameOverClip" })
        {
            var clip = serialized.FindProperty(field).objectReferenceValue as AudioClip;
            clips.Add(clip != null ? $"{clip.name} {clip.length:F2} s" : $"FAIL: {field} not set");
        }

        Log($"Clips: {string.Join(", ", clips)}");

        EditorApplication.update -= WatchBasket;
        if (effects == null)
        {
            Log("FAIL: no CatchEffects in the scene");
            yield break;
        }

        effects.played -= EffectPlayed;
        string gotEffects = string.Join(", ", effectList);
        Log($"Effects: {gotEffects}: {(gotEffects == expectedEffects ? "as planned" : "FAIL: planned " + expectedEffects)}");
        Log($"Basket squashed to {lowestScale:F3} of its height at most; at the end it is {(basketBody != null ? basketBody.localScale.ToString("F3") : "not there")}");
    }

    /// <summary>
    /// V0.7 step 4: on a first run the "How to play" panel is up and keeps taps from the Start button until
    /// "Got it" is tapped; in the next Play mode session it does not come back.
    /// </summary>
    static IEnumerator<float> Onboarding(bool firstRun)
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var panel = Object.FindAnyObjectByType<HowToPlayPanel>();
        if (panel == null)
        {
            Log("FAIL: no HowToPlayPanel in the scene");
            yield break;
        }

        yield return 1f;
        if (!firstRun)
        {
            Log($"Next session: panel shown {panel.isShowing}, read flag {PlayerPrefs.GetInt(k_SeenHowToPlayKey, 0)}");
            yield break;
        }

        Log($"First run: panel shown {panel.isShowing}, read flag {PlayerPrefs.GetInt(k_SeenHowToPlayKey, 0)}");
        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        float readyWait = Time.time;
        while (game.state != GameStateManager.GameState.Ready && Time.time - readyWait < 10f)
            yield return 0.25f;

        var startButton = FindButton("Start Button");
        var gotIt = FindButton("Got It Button");
        Log($"With the panel up: game {game.state}, a tap on Start reaches it {CanTap(startButton)}, a tap on Got it reaches it {CanTap(gotIt)}");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-onboarding.png"));
        yield return 0.5f;

        gotIt.onClick.Invoke();
        yield return 0.3f;
        Log($"After Got it: panel shown {panel.isShowing}, read flag {PlayerPrefs.GetInt(k_SeenHowToPlayKey, 0)}, a tap on Start reaches it {CanTap(startButton)}");
    }

    /// <summary>
    /// V0.8 step 2: the set of point IDs that FloorBasketFollower remembers as sampled is emptied once it passes
    /// 5,000. The simulation only has a handful of points, so the set is filled by hand, through reflection.
    /// </summary>
    static IEnumerator<float> PointMemory()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        yield return 2f;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var ids = (HashSet<ulong>)typeof(FloorBasketFollower).GetField("m_SampledPointIds", flags).GetValue(follower);
        var sample = typeof(FloorBasketFollower).GetMethod("TryGetPointFloorHeight", flags);
        int real = ids.Count;
        foreach (int fill in new[] { 5000 - real, 1 })
        {
            // IDs far above anything the simulation hands out.
            for (int i = 0; i < fill; i++)
                ids.Add(ulong.MaxValue - (ulong)ids.Count);

            int before = ids.Count;
            sample.Invoke(follower, new object[] { 0f });
            Log($"{before} remembered points: {ids.Count} after the next sampling {(before > 5000 ? "(emptied, then the points in view again)" : "(kept)")}");
        }

        Log($"Floor still {follower.floorSource}, basket shown {follower.isBasketVisible}");
    }

    // On a Screen Space Overlay canvas, world space is screen pixels.
    static Rect ScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    /// <summary>
    /// Logs where each named UI part is on screen, flags any that reach outside <paramref name="safe"/>, and
    /// notes any two that overlap. Parts that are not showing are skipped.
    /// </summary>
    static void CheckLayout(string when, Rect safe, params string[] names)
    {
        var shown = new List<(string name, Rect rect)>();
        foreach (string name in names)
        {
            var transform = FindDeep(name) as RectTransform;
            if (transform == null || !transform.gameObject.activeInHierarchy)
                continue;

            var rect = ScreenRect(transform);
            bool inside = rect.xMin >= safe.xMin - 0.5f && rect.yMin >= safe.yMin - 0.5f && rect.xMax <= safe.xMax + 0.5f && rect.yMax <= safe.yMax + 0.5f;
            string text = "";
            var label = transform.GetComponentInChildren<TMPro.TMP_Text>();
            if (label != null && label.gameObject.activeInHierarchy && label.text.Length > 0)
            {
                // How much room the text needs against the room its box gives it.
                var needed = label.GetRenderedValues(true);
                var box = label.rectTransform.rect.size;
                bool fits = needed.x <= box.x + 1f && needed.y <= box.y + 1f;
                text = $", text {needed.x:F0}x{needed.y:F0} in {box.x:F0}x{box.y:F0} at size {label.fontSize:F0}{(fits ? "" : " DOES NOT FIT")}";
            }

            Log($"  {when}: {name} x {rect.xMin:F0}-{rect.xMax:F0}, y {rect.yMin:F0}-{rect.yMax:F0} px{(inside ? "" : " FAIL: outside the safe area")}{text}");
            shown.Add((name, rect));
        }

        for (int i = 0; i < shown.Count; i++)
        {
            for (int j = i + 1; j < shown.Count; j++)
            {
                if (!shown[i].rect.Overlaps(shown[j].rect))
                    continue;

                float height = Mathf.Min(shown[i].rect.yMax, shown[j].rect.yMax) - Mathf.Max(shown[i].rect.yMin, shown[j].rect.yMin);
                Log($"  {when}: {shown[i].name} and {shown[j].name} overlap by {height:F0} px");
            }
        }
    }

    /// <summary>
    /// Before the phone test: the game as a Realme 7 shows it, 1080 x 2400 in portrait, with the debug lines off
    /// as on a phone and the top 96 px kept clear as for its camera hole. Captures the first-run panel, Ready,
    /// the countdown, a round and Game Over, and checks that each part of the HUD is on screen, inside the safe
    /// area, and clear of the others.
    /// </summary>
    static IEnumerator<float> Portrait()
    {
        const float cameraHole = 96f;
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        yield return 0.5f;
        Log($"Game view {Screen.width}x{Screen.height}");
        if (Screen.width != 1080 || Screen.height != 2400)
        {
            Log("FAIL: the Game view is not 1080x2400");
            yield break;
        }

        ARStatusText.showDebugInEditor = false;

        // What SafeAreaFitter would set on a phone whose camera hole takes the top 96 px.
        var safeAreaRect = Object.FindAnyObjectByType<SafeAreaFitter>().GetComponent<RectTransform>();
        safeAreaRect.anchorMax = new Vector2(1f, 1f - cameraHole / Screen.height);
        var safe = new Rect(0f, 0f, Screen.width, Screen.height - cameraHole);
        string[] parts = { "Status Background", "Pause Button", "Round Bar", "Countdown Text", "Start Button", "Game Over Panel", "Card", "Got It Button" };

        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        float readyWait = Time.time;
        while (game.state != GameStateManager.GameState.Ready && Time.time - readyWait < 10f)
            yield return 0.25f;

        yield return 0.5f;
        CheckLayout("first run", safe, parts);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-portrait-1-howto.png"));
        yield return 0.3f;

        FindButton("Got It Button").onClick.Invoke();
        yield return 0.5f;
        CheckLayout("ready", safe, parts);
        Log($"  ready: hint \"{Hint()}\"");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-portrait-2-ready.png"));
        yield return 0.3f;

        // Red apples only, so the combo builds and the bar shows a multiplier.
        spawner.QueueTypes(Repeat("RedApple", 40));
        FindButton("Start Button").onClick.Invoke();
        yield return 0.5f;
        CheckLayout("countdown", safe, parts);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-portrait-3-countdown.png"));
        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        // One capture just after the combo call-out, with the points still rising from the basket.
        var popups = Object.FindAnyObjectByType<ScorePopups>();
        bool calloutCaptured = false;
        void Shown(string text)
        {
            if (calloutCaptured || !text.StartsWith("Combo"))
                return;

            calloutCaptured = true;
            CaptureAt(Time.time + 0.12f, "Temp/SimTest-portrait-7-popups.png");
        }

        if (popups != null)
            popups.shown += Shown;

        // Follow the fruit for a while, then capture with one low in view above its ring.
        bool captured = false;
        float playStart = Time.time;
        while (!captured && Time.time - playStart < 30f)
        {
            var next = NextToLand(spawner, follower, true);
            if (next != null)
            {
                var spot = next.transform.position;
                spot.y = follower.floorHeight;
                LookAt(spot);
                if (Time.time - playStart > 14f && next.transform.position.y < follower.floorHeight + 0.6f)
                {
                    CheckLayout("playing", safe, parts);
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-portrait-4-playing.png"));
                    captured = true;
                }
            }

            yield return 0f;
        }

        if (popups != null)
            popups.shown -= Shown;

        Log($"  pop-ups: a capture after the combo call-out {(calloutCaptured ? "was taken" : "FAIL: was not taken, no call-out in the round")}");
        yield return 0.3f;
        FindButton("Pause Button").onClick.Invoke();
        yield return 0.3f;
        CheckLayout("paused", safe, "Status Background", "Pause Card", "Pause Title", "Resume Button", "Restart Button Pause", "Sound Button", "Vibration Button");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-portrait-6-paused.png"));
        yield return 0.3f;
        FindButton("Resume Button").onClick.Invoke();
        float resumeWait = Time.time;
        while (game.state != GameStateManager.GameState.Playing && Time.time - resumeWait < 10f)
            yield return 0.1f;

        simCamera.rotation = Quaternion.Euler(89f, simCamera.eulerAngles.y, 0f);
        float overStart = Time.time;
        while (game.state != GameStateManager.GameState.GameOver && Time.time - overStart < 30f)
            yield return 0.1f;

        yield return 0.8f;
        CheckLayout("game over", safe, parts);
        CheckLayout("game over panel", safe, "Title", "Score Label", "Final Score Text", "Best Text", "Restart Button");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-portrait-5-gameover.png"));
        yield return 0.3f;
        ARStatusText.showDebugInEditor = true;
    }

    static string SleepName(int timeout)
    {
        return timeout == SleepTimeout.NeverSleep ? "never" : timeout == SleepTimeout.SystemSetting ? "phone's setting" : timeout.ToString();
    }

    // A press or a release of Escape, which is how the Android back key reaches the Input System.
    static void QueueEscape(bool down)
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null)
            return;

        UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
            down ? new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Escape) : new UnityEngine.InputSystem.LowLevel.KeyboardState());
    }

    /// <summary>
    /// Presses and releases the back key through the Input System. Returns false when the press never arrived,
    /// for example because the editor dropped it, in which case the back handler is called directly instead.
    /// </summary>
    static IEnumerator<float> PressBack(GameStateManager game, System.Func<bool> arrived, List<string> notes)
    {
        QueueEscape(true);
        yield return 0f;
        yield return 0.1f;
        QueueEscape(false);
        yield return 0.1f;
        if (arrived())
        {
            notes.Add("Input System");
            yield break;
        }

        notes.Add("direct call");
        game.SendMessage("OnBackPressed");
        yield return 0.1f;
    }

    /// <summary>
    /// V1.0 polish step 1: the screen stays awake while scanning and playing and follows the phone's setting
    /// at Game Over and in a player pause; leaving the app pauses the round and it waits for the player;
    /// the back key resumes and pauses; the pause menu's restart and a pause during the countdown work.
    /// </summary>
    static IEnumerator<float> Mobile()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();

        // Key presses only reach the game when the Game view has focus, which a test run cannot count on.
        var inputSettings = UnityEngine.InputSystem.InputSystem.settings;
        var oldBackground = inputSettings.backgroundBehavior;
        var oldEditorInput = inputSettings.editorInputBehaviorInPlayMode;
        inputSettings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        inputSettings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var backVia = new List<string>();

        yield return 0.5f;
        Log($"Scanning: screen sleep {SleepName(Screen.sleepTimeout)}");
        spawner.QueueTypes(Repeat("RedApple", 60));
        var start = ScanAndStart(follower, game);
        while (start.MoveNext())
            yield return start.Current;

        if (game.state != GameStateManager.GameState.Playing)
            yield break;

        Log($"Playing: screen sleep {SleepName(Screen.sleepTimeout)}");

        // A fruit well up in the air, followed so it would be caught if it fell.
        Fruit falling = null;
        float wait = Time.time;
        while (falling == null && Time.time - wait < 6f)
        {
            foreach (var fruit in spawner.GetComponentsInChildren<Fruit>())
            {
                if (fruit.transform.position.y > follower.floorHeight + 1.2f)
                    falling = fruit;
            }

            yield return 0.05f;
        }

        if (falling == null)
        {
            Log("FAIL: no fruit fell within 6 s");
            yield break;
        }

        // 1. The app goes to the background mid-round and comes back.
        game.SendMessage("OnApplicationPause", true);
        yield return 0f;
        float heldY = falling.transform.position.y;
        yield return 2f;
        Log($"App in the background: {game.state}, paused by the player {game.isPausedByPlayer}, screen sleep {SleepName(Screen.sleepTimeout)}, " +
            $"fruit moved {Mathf.Abs(falling.transform.position.y - heldY):F4} m in 2 s");
        game.SendMessage("OnApplicationPause", false);
        yield return 2f;
        Log($"App back, tracking good, 2 s later: {game.state}, paused by the player {game.isPausedByPlayer}, fruit moved {Mathf.Abs(falling.transform.position.y - heldY):F4} m");

        // 2. The back key resumes: a 3-2-1 with the fruit still held, then the fruit moves again.
        var press = PressBack(game, () => !game.isPausedByPlayer, backVia);
        while (press.MoveNext())
            yield return press.Current;

        float resumeStart = Time.time;
        while (game.state == GameStateManager.GameState.Paused && Time.time - resumeStart < 5f)
            yield return 0f;

        heldY = falling.transform.position.y;
        yield return 1f;
        Log($"Back key: {game.state}, {game.countdownRemaining:F2} s to go, fruit moved {Mathf.Abs(falling.transform.position.y - heldY):F4} m in 1 s, screen sleep {SleepName(Screen.sleepTimeout)}");
        while (game.state == GameStateManager.GameState.Resuming && Time.time - resumeStart < 10f)
            yield return 0f;

        heldY = falling.transform.position.y;
        yield return 0.3f;
        Log($"  then {game.state}, fruit moved {Mathf.Abs(falling.transform.position.y - heldY):F3} m in 0.3 s");

        // 3. The back key pauses a running round.
        press = PressBack(game, () => game.isPausedByPlayer, backVia);
        while (press.MoveNext())
            yield return press.Current;

        Log($"Back key in a round: {game.state}, paused by the player {game.isPausedByPlayer}, spawner paused {spawner.isPaused}");

        // 4. Restart from the pause: a fresh countdown, nothing left falling.
        int scoreBefore = score.score;
        game.RestartFromPause();
        yield return 0.1f;
        Log($"Restart from the pause (score was {scoreBefore}): {game.state}, {game.countdownRemaining:F2} s to go, score {score.score}, lives {score.lives}, " +
            $"fruit falling {spawner.GetComponentsInChildren<Fruit>().Length}, spawning {spawner.isSpawning}, paused by the player {game.isPausedByPlayer}");

        // 5. A pause during the countdown holds it.
        yield return 0.5f;
        game.PauseByPlayer();
        yield return 0f;
        float held = game.countdownRemaining;
        yield return 1f;
        Log($"Paused in the countdown: {game.state}, paused {game.isPaused}, countdown moved {held - game.countdownRemaining:F3} s in 1 s");
        game.ResumeByPlayer();
        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        // 6. Game Over hands the screen back to the phone's setting.
        simCamera.rotation = Quaternion.Euler(89f, simCamera.eulerAngles.y, 0f);
        float overStart = Time.time;
        while (game.state != GameStateManager.GameState.GameOver && Time.time - overStart < 30f)
            yield return 0.1f;

        Log($"{game.state}: screen sleep {SleepName(Screen.sleepTimeout)}");

        // 7. Outside a round the back key does not touch the game (on a phone it sends the app to the background).
        press = PressBack(game, () => true, backVia);
        while (press.MoveNext())
            yield return press.Current;

        Log($"Back key at Game Over: {game.state}. Back key presses arrived through: {string.Join(", ", backVia)}");
        inputSettings.backgroundBehavior = oldBackground;
        inputSettings.editorInputBehaviorInPlayMode = oldEditorInput;
    }

    /// <summary>
    /// V1.0 polish step 2: the pause button during a round, the menu it opens (Resume, Restart, sound and
    /// vibration), the switches taking effect at once, and no vibration for a miss once it is off.
    /// </summary>
    static IEnumerator<float> PauseMenuRound()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();
        var menu = Object.FindAnyObjectByType<PauseMenu>();
        var effects = Object.FindAnyObjectByType<CatchEffects>();
        if (menu == null)
        {
            Log("FAIL: no PauseMenu in the scene");
            yield break;
        }

        yield return 0.5f;
        Log($"Before a round: pause button shown {FindActive("Pause Button")}, menu shown {menu.isShowing}, sound {GameSettings.soundOn}, vibration {GameSettings.vibrationOn}, listener volume {AudioListener.volume:F1}");
        spawner.QueueTypes(Repeat("RedApple", 60));
        var start = ScanAndStart(follower, game);
        while (start.MoveNext())
            yield return start.Current;

        if (game.state != GameStateManager.GameState.Playing)
            yield break;

        Fruit falling = null;
        float wait = Time.time;
        while (falling == null && Time.time - wait < 6f)
        {
            foreach (var fruit in spawner.GetComponentsInChildren<Fruit>())
            {
                if (fruit.transform.position.y > follower.floorHeight + 1.2f)
                    falling = fruit;
            }

            yield return 0.05f;
        }

        var pauseButton = FindButton("Pause Button");
        Log($"In a round: pause button shown {FindActive("Pause Button")}, a tap reaches it {CanTap(pauseButton)}");
        pauseButton.onClick.Invoke();
        yield return 0.1f;
        float heldY = falling != null ? falling.transform.position.y : 0f;
        yield return 2f;
        Log($"Pause button: {game.state}, paused by the player {game.isPausedByPlayer}, menu shown {menu.isShowing}, pause button shown {FindActive("Pause Button")}, " +
            $"fruit moved {(falling != null ? Mathf.Abs(falling.transform.position.y - heldY) : -1f):F4} m in 2 s, hint \"{Hint()}\"");
        Log($"  taps reach Resume {CanTap(FindButton("Resume Button"))}, Restart {CanTap(FindButton("Restart Button Pause"))}, " +
            $"Sound {CanTap(FindButton("Sound Button"))}, Vibration {CanTap(FindButton("Vibration Button"))}");

        FindButton("Sound Button").onClick.Invoke();
        FindButton("Vibration Button").onClick.Invoke();
        yield return 0.1f;
        Log($"Both switched off: sound {GameSettings.soundOn}, vibration {GameSettings.vibrationOn}, listener volume {AudioListener.volume:F1}, " +
            $"labels \"{HudText("Sound Label")}\" and \"{HudText("Vibration Label")}\"");

        FindButton("Resume Button").onClick.Invoke();
        float resumeStart = Time.time;
        while (game.state != GameStateManager.GameState.Playing && Time.time - resumeStart < 10f)
            yield return 0.1f;

        Log($"Resume: {game.state} after {Time.time - resumeStart:F1} s, menu shown {menu.isShowing}, pause button shown {FindActive("Pause Button")}");

        // A miss with vibration off: the splash, and no buzz.
        var effectList = new List<string>();
        void EffectPlayed(CatchEffects.Effect effect) => effectList.Add(effect.ToString());
        effects.played += EffectPlayed;
        simCamera.rotation = Quaternion.Euler(89f, simCamera.eulerAngles.y, 0f);
        int missesBefore = spawner.missedCount;
        float missWait = Time.time;
        while (spawner.missedCount == missesBefore && Time.time - missWait < 10f)
            yield return 0.1f;

        yield return 0.1f;
        effects.played -= EffectPlayed;
        Log($"A miss with vibration off: effects {string.Join(", ", effectList)}");

        // Restart from the menu.
        int scoreBefore = score.score;
        FindButton("Pause Button").onClick.Invoke();
        yield return 0.2f;
        FindButton("Restart Button Pause").onClick.Invoke();
        yield return 0.2f;
        Log($"Restart from the menu (lives were {score.lives}, score {scoreBefore}): {game.state}, score {score.score}, lives {score.lives}, menu shown {menu.isShowing}, " +
            $"pause button shown {FindActive("Pause Button")}, fruit falling {spawner.GetComponentsInChildren<Fruit>().Length}");
    }

    /// <summary>
    /// V1.0 polish step 2, in a new Play mode session after pause-menu: the switches it turned off are still
    /// off and the sound is muted from the start; then both are turned on again.
    /// </summary>
    static IEnumerator<float> PauseMenuAgain()
    {
        yield return 1f;
        Log($"New session: sound {GameSettings.soundOn}, vibration {GameSettings.vibrationOn}, listener volume {AudioListener.volume:F1}");
        GameSettings.soundOn = true;
        GameSettings.vibrationOn = true;
        Log($"Both on again: sound {GameSettings.soundOn}, vibration {GameSettings.vibrationOn}, listener volume {AudioListener.volume:F1}");
    }

    // Reads a value once play time reaches the given moment and adds it to the list.
    static void ReadAt(float time, System.Func<string> read, List<string> into)
    {
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (EditorApplication.isPlaying && Time.time < time)
                return;

            EditorApplication.update -= wait;
            if (EditorApplication.isPlaying)
                into.Add(read());
        };
        EditorApplication.update += wait;
    }

    static string HeartState(string name)
    {
        var heart = FindDeep(name);
        if (heart == null)
            return $"{name} missing";

        var image = heart.GetComponent<UnityEngine.UI.Image>();
        return $"{name} scale {heart.localScale.x:F2}, alpha {image.color.a:F2}";
    }

    /// <summary>
    /// V1.0 polish step 3: the planned round of score-round, watching the feedback. The pop-ups must show the
    /// points worked out by hand, in order, with a call-out when the multiplier reaches ×2; the score must count
    /// up to its new value instead of jumping; the multiplier must swell when it goes up; a lost heart must
    /// shake and shrink, then dim.
    /// </summary>
    static IEnumerator<float> HudFeedback()
    {
        const string plan = "CCCCMCCMCCCM";
        const string expectedPopups = "+10, +10, +100, Combo ×2, +20, +20, -20, +15, +20, +30";
        string[] order = { "RedApple", "RedApple", "GoldenApple", "RedApple", "RottenApple", "RedApple", "RottenApple", "RedApple", "GreenApple", "Orange", "Lemon", "RedApple" };
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();
        var popups = Object.FindAnyObjectByType<ScorePopups>();
        if (popups == null)
        {
            Log("FAIL: no ScorePopups in the scene");
            yield break;
        }

        var planned = new List<FruitType>();
        foreach (string name in order)
            planned.Add(LoadType(name));

        spawner.QueueTypes(planned);
        var shownTexts = new List<string>();
        var readings = new List<string>();
        void Shown(string text)
        {
            shownTexts.Add(text);
            if (text == "+100")
            {
                // The score as shown right after the golden catch, and once it has counted.
                readings.Add($"score text when +100 appears \"{HudText("Score Text")}\" (score {score.score})");
                ReadAt(Time.time + 0.15f, () => $"0.15 s later \"{HudText("Score Text")}\"", readings);
                ReadAt(Time.time + 0.5f, () => $"0.5 s later \"{HudText("Score Text")}\"", readings);
                CaptureAt(Time.time + 0.1f, "Temp/SimTest-hud-feedback.png");
            }
            else if (text.StartsWith("Combo"))
            {
                ReadAt(Time.time + 0.05f, () => $"combo text \"{HudText("Combo Text")}\" scale {FindDeep("Combo Text").localScale.x:F2} after 0.05 s", readings);
                ReadAt(Time.time + 0.4f, () => $"scale {FindDeep("Combo Text").localScale.x:F2} after 0.4 s", readings);
            }
        }

        int misses = 0;
        void Landed(Fruit fruit, bool caught)
        {
            if (caught || fruit.type.isHazard || misses > 0)
                return;

            // The first miss. The score has already taken the life, so the heart lost is the one after the
            // remaining lives (the rotten apple caught earlier took the third).
            misses++;
            string heart = $"Heart {score.lives + 1}";
            ReadAt(Time.time + 0.1f, () => "first miss +0.1 s: " + HeartState(heart), readings);
            ReadAt(Time.time + 0.6f, () => "+0.6 s: " + HeartState(heart), readings);
        }

        popups.shown += Shown;
        spawner.fruitLanded += Landed;
        var start = ScanAndStart(follower, game);
        while (start.MoveNext())
            yield return start.Current;

        if (game.state != GameStateManager.GameState.Playing)
            yield break;

        var outcomes = new System.Text.StringBuilder();
        var play = PlayPlan(plan, follower, spawner, game, outcomes);
        while (play.MoveNext())
            yield return play.Current;

        yield return 1f;
        popups.shown -= Shown;
        spawner.fruitLanded -= Landed;
        string got = string.Join(", ", shownTexts);
        Log($"Planned {plan}, got {outcomes}; {game.state}, score {score.score}");
        Log($"Pop-ups: {got}: {(got == expectedPopups ? "as worked by hand" : "FAIL: worked by hand " + expectedPopups)}");
        foreach (string reading in readings)
            Log($"  {reading}");

        Log($"At Game Over: score text \"{HudText("Score Text")}\", final \"{HudText("Final Score Text")}\"");
    }

    /// <summary>
    /// V1.0 polish step 4, on the Realme 7-sized portrait view: the title, tagline and version before a round,
    /// the Start button's pulse, and after the planned 205-point round (best set to 40 first) the Game Over
    /// stats worked out by hand (8 good fruit caught, best streak 7) and the confetti for the new best.
    /// </summary>
    static IEnumerator<float> Screens()
    {
        const string plan = "CCCCMCCMCCCM";
        string[] order = { "RedApple", "RedApple", "GoldenApple", "RedApple", "RottenApple", "RedApple", "RottenApple", "RedApple", "GreenApple", "Orange", "Lemon", "RedApple" };
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();
        var effects = Object.FindAnyObjectByType<CatchEffects>();
        yield return 0.5f;
        if (Screen.width != 1080 || Screen.height != 2400)
        {
            Log("FAIL: the Game view is not 1080x2400");
            yield break;
        }

        ARStatusText.showDebugInEditor = false;

        // As in the portrait scenario: the top 96 px kept clear as for the Realme 7's camera hole.
        const float cameraHole = 96f;
        Object.FindAnyObjectByType<SafeAreaFitter>().GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f - cameraHole / Screen.height);
        var safe = new Rect(0f, 0f, Screen.width, Screen.height - cameraHole);
        Log($"Scanning: title shown {FindActive("Game Title")}, title \"{HudText("Title Text")}\", tagline \"{HudText("Tagline Text")}\", version \"{HudText("Version Text")}\"");

        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        float readyWait = Time.time;
        while (game.state != GameStateManager.GameState.Ready && Time.time - readyWait < 10f)
            yield return 0.25f;

        // The pulse over a little more than one period.
        var startButton = FindDeep("Start Button");
        float smallest = float.MaxValue, largest = 0f;
        float pulseStart = Time.time;
        while (Time.time - pulseStart < 1.4f)
        {
            smallest = Mathf.Min(smallest, startButton.localScale.x);
            largest = Mathf.Max(largest, startButton.localScale.x);
            yield return 0f;
        }

        Log($"Ready: title shown {FindActive("Game Title")}, Start button scale {smallest:F3}-{largest:F3} over 1.4 s");
        CheckLayout("start screen", safe, "Status Background", "Game Title", "Version Text", "Start Button");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-screens-1-start.png"));
        yield return 0.3f;

        var planned = new List<FruitType>();
        foreach (string name in order)
            planned.Add(LoadType(name));

        spawner.QueueTypes(planned);
        int confetti = 0;
        void EffectPlayed(CatchEffects.Effect effect)
        {
            if (effect == CatchEffects.Effect.Confetti)
                confetti++;
        }

        effects.played += EffectPlayed;
        FindButton("Start Button").onClick.Invoke();
        yield return 0.2f;
        Log($"Countdown: title shown {FindActive("Game Title")}, version shown {FindActive("Version Text")}, Start button scale {startButton.localScale.x:F2}");
        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        var outcomes = new System.Text.StringBuilder();
        var play = PlayPlan(plan, follower, spawner, game, outcomes);
        while (play.MoveNext())
            yield return play.Current;

        // Captured while the confetti is in the air.
        yield return 0.35f;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-screens-2-gameover.png"));
        yield return 0.5f;
        effects.played -= EffectPlayed;
        Log($"Planned {plan}, got {outcomes}; {game.state}, score {score.score}, caught {score.caughtCount} (worked by hand 8), best streak {score.bestStreak} (worked by hand 7), " +
            $"round time {spawner.roundTime:F1} s");
        Log($"Game Over: stats \"{HudText("Stats Text")}\", best line \"{HudText("Best Text")}\", confetti bursts {confetti}");
        CheckLayout("game over", safe, "Game Over Panel");
        CheckLayout("game over panel", safe, "Title", "Final Score Text", "Best Text", "Stats Text", "Restart Button");
        ARStatusText.showDebugInEditor = true;
    }

    // The fill opacity of every plane now, as "0.33" or a range.
    static string PlaneAlphas(PlaneVisibility planes)
    {
        float lowest = float.MaxValue, highest = 0f;
        int count = 0;
        foreach (var plane in Object.FindAnyObjectByType<ARPlaneManager>().trackables)
        {
            float alpha = planes.FillAlphaOf(plane);
            lowest = Mathf.Min(lowest, alpha);
            highest = Mathf.Max(highest, alpha);
            count++;
        }

        return count == 0 ? "no planes" : Mathf.Approximately(lowest, highest) ? $"{lowest:F3} on {count} planes" : $"{lowest:F3}-{highest:F3} on {count} planes";
    }

    /// <summary>
    /// V1.0 polish step 5: the plane overlay fades during the countdown and the round and comes back at Game
    /// Over; the golden apple falls with a sparkle trail and its catch plays the chime; a red apple has neither.
    /// </summary>
    static IEnumerator<float> ScenePolish()
    {
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var audio = Object.FindAnyObjectByType<GameAudio>();
        var planes = Object.FindAnyObjectByType<PlaneVisibility>();
        if (planes == null)
        {
            Log("FAIL: no PlaneVisibility in the scene");
            yield break;
        }

        var sounds = new List<string>();
        void Played(AudioClip clip)
        {
            if (clip.name != "SpawnCue")
                sounds.Add(clip.name);
        }

        audio.played += Played;
        spawner.QueueTypes(new[] { LoadType("GoldenApple"), LoadType("RedApple"), LoadType("RedApple") });
        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        float readyWait = Time.time;
        while (game.state != GameStateManager.GameState.Ready && Time.time - readyWait < 10f)
            yield return 0.25f;

        Log($"Ready: plane fill {PlaneAlphas(planes)}, overlay visibility {planes.visibility:F2}");
        FindButton("Start Button").onClick.Invoke();
        yield return 0.25f;
        Log($"0.25 s into the countdown: plane fill {PlaneAlphas(planes)}, visibility {planes.visibility:F2}");
        yield return 0.5f;
        Log($"0.75 s into the countdown: plane fill {PlaneAlphas(planes)}, visibility {planes.visibility:F2}");
        var countdown = WaitForPlaying(game);
        while (countdown.MoveNext())
            yield return countdown.Current;

        // Follow the fruit: the golden apple comes first, then two red ones.
        Fruit golden = null, red = null;
        int goldenSparkles = 0, redSparkles = 0;
        bool captured = false;
        float start = Time.time;
        while (spawner.caughtCount < 3 && Time.time - start < 20f)
        {
            foreach (var fruit in spawner.GetComponentsInChildren<Fruit>())
            {
                var trail = fruit.GetComponentInChildren<ParticleSystem>();
                int count = trail != null ? trail.particleCount : 0;
                if (fruit.type.streakBonus > 0)
                {
                    golden = fruit;
                    goldenSparkles = Mathf.Max(goldenSparkles, count);
                    // Half way down, look at the apple itself for a moment and capture its trail.
                    if (!captured && fruit.transform.position.y < follower.floorHeight + 1.3f)
                    {
                        LookAt(fruit.transform.position);
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-scene-polish.png"));
                        captured = true;
                        yield return 0f;
                        yield return 0f;
                    }
                }
                else
                {
                    red = fruit;
                    redSparkles = Mathf.Max(redSparkles, count);
                }
            }

            var next = NextToLand(spawner, follower, true);
            if (next != null)
            {
                var spot = next.transform.position;
                spot.y = follower.floorHeight;
                LookAt(spot);
            }

            yield return 0f;
        }

        Log($"Playing: plane fill {PlaneAlphas(planes)}, visibility {planes.visibility:F2}");
        Log($"Golden apple trail: up to {goldenSparkles} sparkles in the air; red apple: {(red != null && red.GetComponentInChildren<ParticleSystem>() != null ? "has a particle system" : "no particle system")}, {redSparkles} sparkles");
        Log($"Sounds for the three catches: {string.Join(", ", sounds)}");

        simCamera.rotation = Quaternion.Euler(89f, simCamera.eulerAngles.y, 0f);
        float overStart = Time.time;
        while (game.state != GameStateManager.GameState.GameOver && Time.time - overStart < 30f)
            yield return 0.1f;

        yield return 0.8f;
        audio.played -= Played;
        Log($"Game Over + 0.8 s: plane fill {PlaneAlphas(planes)}, visibility {planes.visibility:F2}");
    }

    /// <summary>
    /// V1.0 polish step 6: full rounds played by a modelled player instead of the perfect script camera, to see
    /// how long a round lasts for someone who is good but human. The model turns the phone at most 120 degrees
    /// a second, notices a new fruit 0.25 s after it becomes the next to land, aims up to 5 cm off (a fixed
    /// error per fruit, seeded), and leaves rotten fruit alone. It is an estimate, not a playtest.
    /// </summary>
    static IEnumerator<float> HumanRounds(int rounds)
    {
        const float turnSpeed = 120f;
        const float reaction = 0.25f;
        const float aimError = 0.05f;
        const float longestRound = 300f;
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var game = Object.FindAnyObjectByType<GameStateManager>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var score = Object.FindAnyObjectByType<ScoreManager>();
        var random = new System.Random(2026);
        var aimOffsets = new Dictionary<Fruit, Vector3>();
        var lengths = new List<float>();

        var start = ScanAndStart(follower, game);
        while (start.MoveNext())
            yield return start.Current;

        for (int round = 1; round <= rounds; round++)
        {
            if (game.state != GameStateManager.GameState.Playing)
            {
                Log($"FAIL: round {round} did not start ({game.state})");
                yield break;
            }

            var missTimes = new List<float>();
            void Landed(Fruit fruit, bool caught)
            {
                if (!caught && !fruit.type.isHazard)
                    missTimes.Add(spawner.roundTime);
            }

            spawner.fruitLanded += Landed;
            Fruit target = null, candidate = null;
            float candidateSince = 0f;
            while (game.state != GameStateManager.GameState.GameOver && spawner.roundTime < longestRound)
            {
                // The fruit the player is reacting to changes only once the new one has been next for a moment.
                var next = NextToLand(spawner, follower, true);
                if (next != candidate)
                {
                    candidate = next;
                    candidateSince = Time.time;
                }

                if (candidate != null && candidate != target && Time.time - candidateSince >= reaction)
                    target = candidate;

                if (target != null && !target.gameObject.activeInHierarchy)
                    target = null;

                if (target != null)
                {
                    if (!aimOffsets.TryGetValue(target, out var offset))
                    {
                        double angle = random.NextDouble() * 2.0 * Mathf.PI;
                        float distance = (float)random.NextDouble() * aimError;
                        offset = new Vector3(Mathf.Cos((float)angle) * distance, 0f, Mathf.Sin((float)angle) * distance);
                        aimOffsets[target] = offset;
                    }

                    var spot = target.transform.position + offset;
                    spot.y = follower.floorHeight;
                    var wanted = Quaternion.LookRotation(spot - Camera.main.transform.position);
                    simCamera.rotation = Quaternion.RotateTowards(simCamera.rotation, wanted, turnSpeed * Time.deltaTime);
                }

                yield return 0f;
            }

            spawner.fruitLanded -= Landed;
            float length = spawner.roundTime;
            lengths.Add(length);
            var perPart = new List<string>();
            for (float from = 0f; from < length; from += 30f)
                perPart.Add(missTimes.FindAll(t => t >= from && t < from + 30f).Count.ToString());

            Log($"Round {round}: {(game.state == GameStateManager.GameState.GameOver ? "Game Over" : "still going")} after {length:F0} s, score {score.score}, " +
                $"{score.caughtCount} caught, best streak {score.bestStreak}, good fruit missed per 30 s: {string.Join(" / ", perPart)}");
            aimOffsets.Clear();

            if (round < rounds)
            {
                if (game.state != GameStateManager.GameState.GameOver)
                {
                    Log("Round stopped at the time limit; the next round needs Game Over first");
                    break;
                }

                yield return 1f;
                game.Restart();
                var countdown = WaitForPlaying(game);
                while (countdown.MoveNext())
                    yield return countdown.Current;
            }
        }

        lengths.Sort();
        float median = lengths[lengths.Count / 2];
        var curve = AssetDatabase.LoadAssetAtPath<DifficultyCurve>(k_CurvePath);
        Log($"Round lengths {string.Join(", ", lengths.ConvertAll(l => l.ToString("F0")))} s; median {median:F0} s " +
            $"(curve: shortest gap {curve.SpawnInterval(1000f):F2} s, top speed x{curve.SpeedFactor(1000f):F2}). " +
            $"{(median < 75f ? "Under 75 s: ease the late curve, as the plan says" : "75 s or more: keep the accepted numbers")}");
    }

    // Saves a Game view capture once play time reaches the given moment.
    static void CaptureAt(float time, string path)
    {
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (EditorApplication.isPlaying && Time.time < time)
                return;

            EditorApplication.update -= wait;
            if (EditorApplication.isPlaying)
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(path));
        };
        EditorApplication.update += wait;
    }

    const string k_CurvePath = "Assets/Data/DifficultyCurve.asset";

    /// <summary>
    /// V0.6 step 3: the difficulty curve against values worked by hand from the difficulty table, for a phone
    /// that sees 30 degrees to each side.
    /// </summary>
    static IEnumerator<float> Curve()
    {
        var curve = AssetDatabase.LoadAssetAtPath<DifficultyCurve>(k_CurvePath);
        var rows = new (float t, float interval, float speed, float angle, float rotten, float doubles)[]
        {
            (0f, 2.00f, 1.00f, 20f, 0.02f, 0f),
            (30f, 1.55f, 1.24f, 25f, 0.04f, 0.25f),
            (60f, 1.10f, 1.45f, 30f, 0.06f, 0.25f),
            (90f, 0.85f, 1.45f, 30f, 0.08f, 0.25f),
            (120f, 0.85f, 1.45f, 30f, 0.10f, 0.25f),
        };

        int failures = 0;
        foreach (var row in rows)
        {
            float interval = curve.SpawnInterval(row.t);
            float speed = curve.SpeedFactor(row.t);
            float angle = curve.LandingHalfAngle(row.t, 30f);
            float rotten = curve.HazardShare(row.t);
            float doubles = curve.DoubleDropChance(row.t);
            bool ok = Mathf.Abs(interval - row.interval) < 0.001f && Mathf.Abs(speed - row.speed) < 0.001f &&
                Mathf.Abs(angle - row.angle) < 0.01f && Mathf.Abs(rotten - row.rotten) < 0.0001f && Mathf.Abs(doubles - row.doubles) < 0.0001f;
            if (!ok)
                failures++;

            Log($"t = {row.t:F0} s: gap {interval:F2} s, speed x{speed:F2}, spread {angle:F1} deg, rotten {rotten * 100f:F0} %, " +
                $"double {doubles * 100f:F0} %: {(ok ? "as worked by hand" : "FAIL")}");
        }

        Log($"Just before doubles start (29.9 s): double {curve.DoubleDropChance(29.9f) * 100f:F0} %. " +
            $"Phone that sees only 15 deg to each side, at 60 s: spread {curve.LandingHalfAngle(60f, 15f):F1} deg");
        Log(failures == 0 ? "Curve matches the table" : $"FAIL: {failures} rows differ");
        yield break;
    }

    static FruitType[] SpawnerTypes(FruitSpawner spawner)
    {
        var list = new SerializedObject(spawner).FindProperty("m_FruitTypes");
        var types = new FruitType[list.arraySize];
        for (int i = 0; i < types.Length; i++)
            types[i] = (FruitType)list.GetArrayElementAtIndex(i).objectReferenceValue;

        return types;
    }

    /// <summary>
    /// V0.6 step 2: how often each fruit type is picked, at the start of a round and once the share of rotten
    /// fruit has finished rising, against shares worked by hand from the table. Seeded, so it repeats exactly.
    /// </summary>
    static IEnumerator<float> Picks()
    {
        var types = SpawnerTypes(Object.FindAnyObjectByType<FruitSpawner>());
        var curve = AssetDatabase.LoadAssetAtPath<DifficultyCurve>(k_CurvePath);

        // Red, green, orange, lemon, golden, rotten. At 120 s the rotten share is 10 %, and the other five
        // split the remaining 90 % in their 40 : 25 : 20 : 10 : 3 proportions.
        var cases = new (float t, float[] shares)[]
        {
            (0f, new[] { 40f, 25f, 20f, 10f, 3f, 2f }),
            (120f, new[] { 36.73f, 22.96f, 18.37f, 9.18f, 2.76f, 10f }),
        };

        foreach (var (t, shares) in cases)
        {
            foreach (int count in new[] { 1000, 100000 })
            {
                var random = new System.Random(2026);
                var counts = new int[types.Length];
                for (int i = 0; i < count; i++)
                    counts[Array.IndexOf(types, FruitSpawner.PickType(types, curve.HazardShare(t), (float)random.NextDouble()))]++;

                float worst = 0f;
                var parts = new List<string>();
                for (int i = 0; i < types.Length; i++)
                {
                    float share = 100f * counts[i] / count;
                    worst = Mathf.Max(worst, Mathf.Abs(share - shares[i]));
                    parts.Add($"{types[i].displayName} {share:F1} ({shares[i]:F1})");
                }

                Log($"t = {t:F0} s, {count} picks: {string.Join(", ", parts)}. Furthest from the table: {worst:F1} points " +
                    $"{(worst <= 2f ? "(within 2)" : "FAIL: more than 2")}");
            }
        }

        yield break;
    }

    /// <summary>
    /// V0.6 step 3: three minutes of drops with the camera following the fruit, measured against the difficulty
    /// curve in 30-second parts. The spawner is started directly, without lives, so the round cannot end early.
    /// The camera goes to whichever fruit reaches the floor next and leaves rotten fruit alone.
    /// </summary>
    static IEnumerator<float> LongRound()
    {
        const float duration = 180f;
        const float part = 30f;
        var follower = Object.FindAnyObjectByType<FloorBasketFollower>();
        var spawner = Object.FindAnyObjectByType<FruitSpawner>();
        var curve = AssetDatabase.LoadAssetAtPath<DifficultyCurve>(k_CurvePath);

        var wait = WaitForPlaneFloor(follower);
        while (wait.MoveNext())
            yield return wait.Current;

        if (!follower.hasPlaneFloor)
            yield break;

        var scan = ScanRoom(10f);
        while (scan.MoveNext())
            yield return scan.Current;

        yield return 2f;
        var camera = Camera.main;
        float halfFov = Mathf.Atan(1f / camera.projectionMatrix.m00) * Mathf.Rad2Deg;
        Log($"The view is {halfFov:F1} deg to each side");

        var drops = new List<(float t, int frame, FruitType type, float speedFactor, float fallSeconds, Vector3 spot, float angle)>();
        var landings = new List<(float t, bool caught, bool rotten)>();
        int skipped = 0, errors = 0;
        void Spawned(Fruit fruit)
        {
            var spot = fruit.transform.position;
            float height = spot.y - follower.floorHeight;
            spot.y = follower.floorHeight;
            var flat = spot - camera.transform.position;
            flat.y = 0f;
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(camera.transform.up, Vector3.up);

            drops.Add((spawner.roundTime, Time.frameCount, fruit.type, fruit.speed / fruit.type.fallSpeed, height / fruit.speed, spot, Vector3.Angle(forward, flat)));
        }

        void Landed(Fruit fruit, bool caught) => landings.Add((spawner.roundTime, caught, fruit.type.isHazard));
        Application.LogCallback watchLog = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors++;
            else if (message.Contains("skipping this drop"))
                skipped++;
        };

        spawner.fruitSpawned += Spawned;
        spawner.fruitLanded += Landed;
        Application.logMessageReceived += watchLog;
        var game = Object.FindAnyObjectByType<GameStateManager>();
        void StateChanged(GameStateManager.GameState state) => Log($"  game state {state} at round time {spawner.roundTime:F1} s");
        game.stateChanged += StateChanged;
        StopScoring();
        spawner.StartSpawning();
        bool captured = false;
        float lastRoundTime = 0f;
        while (spawner.roundTime < duration)
        {
            // This scenario drives the spawner alone; anything that restarts it spoils the measurement.
            if (spawner.roundTime < lastRoundTime)
                Log($"FAIL: the round clock went back from {lastRoundTime:F1} s to {spawner.roundTime:F1} s (game {game.state}, spawning {spawner.isSpawning})");

            lastRoundTime = spawner.roundTime;
            Fruit next = null;
            float nextSeconds = float.MaxValue;
            int falling = 0;
            foreach (var fruit in spawner.GetComponentsInChildren<Fruit>())
            {
                falling++;
                float seconds = (fruit.transform.position.y - follower.floorHeight) / fruit.speed;
                if (!fruit.type.isHazard && seconds < nextSeconds)
                {
                    nextSeconds = seconds;
                    next = fruit;
                }
            }

            if (next != null)
            {
                var spot = next.transform.position;
                spot.y = follower.floorHeight;
                LookAt(spot);
            }

            // One frame late in the round, with several fruits in the air and one about to land.
            if (!captured && spawner.roundTime > 100f && falling >= 3 && nextSeconds < 0.4f)
            {
                ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-long-round.png"));
                captured = true;
            }

            yield return 0f;
        }

        spawner.fruitSpawned -= Spawned;
        spawner.fruitLanded -= Landed;
        Application.logMessageReceived -= watchLog;
        game.stateChanged -= StateChanged;
        spawner.StopSpawning();

        // A double drop is two fruits in one frame; the first of each frame counts as the drop.
        float worstSpeed = 0f, worstAngle = 0f;
        int pairs = 0, pairsTooClose = 0, pairsTooSoon = 0;
        float minSeparation = float.MaxValue, minLandingGap = float.MaxValue;
        for (int p = 0; p * part < duration; p++)
        {
            float from = p * part, to = from + part;
            int events = 0, doubles = 0, rotten = 0, fruits = 0, gaps = 0;
            float gapSum = 0f, expectedGapSum = 0f, speedSum = 0f, maxAngle = 0f;
            float lastEvent = -1f;
            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                bool second = i > 0 && drops[i - 1].frame == drop.frame;
                if (!second)
                {
                    if (lastEvent >= from && lastEvent < to)
                    {
                        gapSum += drop.t - lastEvent;
                        expectedGapSum += curve.SpawnInterval(lastEvent);
                        gaps++;
                    }

                    lastEvent = drop.t;
                }

                if (drop.t < from || drop.t >= to)
                    continue;

                fruits++;
                speedSum += drop.speedFactor;
                maxAngle = Mathf.Max(maxAngle, drop.angle);
                worstSpeed = Mathf.Max(worstSpeed, Mathf.Abs(drop.speedFactor - curve.SpeedFactor(drop.t)));
                worstAngle = Mathf.Max(worstAngle, drop.angle - curve.LandingHalfAngle(drop.t, halfFov));
                if (drop.type.isHazard)
                    rotten++;

                if (!second)
                {
                    events++;
                    continue;
                }

                doubles++;
                pairs++;
                var first = drops[i - 1];
                float separation = Vector3.Distance(first.spot, drop.spot);
                float landingGap = drop.fallSeconds - first.fallSeconds;
                minSeparation = Mathf.Min(minSeparation, separation);
                minLandingGap = Mathf.Min(minLandingGap, landingGap);
                if (separation < 0.5f - 0.001f)
                    pairsTooClose++;

                if (landingGap < 0.7f - 0.001f)
                    pairsTooSoon++;
            }

            int caught = 0, missed = 0, rottenCaught = 0;
            foreach (var landing in landings)
            {
                if (landing.t < from || landing.t >= to)
                    continue;

                if (landing.rotten)
                    rottenCaught += landing.caught ? 1 : 0;
                else if (landing.caught)
                    caught++;
                else
                    missed++;
            }

            float mid = from + part * 0.5f;
            Log($"{from:F0}-{to:F0} s: {events} drops ({doubles} double), {fruits} fruits, {rotten} rotten; " +
                $"gap {(gaps > 0 ? gapSum / gaps : 0f):F2} s (curve {(gaps > 0 ? expectedGapSum / gaps : 0f):F2}); " +
                $"speed x{(fruits > 0 ? speedSum / fruits : 0f):F2} (curve x{curve.SpeedFactor(mid):F2} mid-part); " +
                $"widest {maxAngle:F1} deg (limit {curve.LandingHalfAngle(to, halfFov):F1}); " +
                $"caught {caught}, missed {missed}, rotten caught {rottenCaught}");
        }

        Log($"Speed factor furthest from the curve: {worstSpeed:F3}. Landing spots past the spread limit by at most {Mathf.Max(0f, worstAngle):F2} deg");
        Log(pairs > 0
            ? $"Double drops: {pairs}; closest pair {minSeparation:F2} m apart ({pairsTooClose} under 0.5 m); " +
                $"shortest time between the two landings {minLandingGap:F2} s ({pairsTooSoon} under 0.7 s)"
            : "FAIL: no double drops in three minutes");
        Log($"Drops skipped for lack of floor: {skipped}. Errors logged: {errors}");
    }

    static Transform FindDeep(string name)
    {
        foreach (var transform in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include))
        {
            if (transform.name == name)
                return transform;
        }

        return null;
    }

    static UnityEngine.UI.Button FindButton(string name)
    {
        return FindDeep(name).GetComponent<UnityEngine.UI.Button>();
    }

    static bool FindActive(string name)
    {
        return FindDeep(name).gameObject.activeInHierarchy;
    }

    static string HudText(string name)
    {
        return FindDeep(name).GetComponent<TMPro.TMP_Text>().text;
    }

    // Opacity of each heart, left to right: 1 for a life left, dimmed for a life lost.
    static string Hearts()
    {
        var parts = new List<string>();
        for (int i = 1; FindDeep($"Heart {i}") is { } heart; i++)
            parts.Add(heart.GetComponent<UnityEngine.UI.Image>().color.a.ToString("F2"));

        return string.Join("/", parts);
    }

    /// <summary>
    /// True when a tap on the middle of the button would land on it, as the EventSystem sees it.
    /// Pressing a button through onClick skips this, which once hid a canvas without a GraphicRaycaster.
    /// </summary>
    static bool CanTap(UnityEngine.UI.Button button)
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        var pointer = new UnityEngine.EventSystems.PointerEventData(eventSystem)
        {
            position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position),
        };
        var hits = new List<UnityEngine.EventSystems.RaycastResult>();
        eventSystem.RaycastAll(pointer, hits);
        return hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform);
    }

    static string StatusText()
    {
        return GameObject.Find("Status Text").GetComponent<TMPro.TMP_Text>().text;
    }

    // The first line of the status text is the hint; the rest are debug lines.
    static string Hint()
    {
        return StatusText().Split('\n')[0];
    }

    static string DescribeEdgeDistance(ARPlaneManager planeManager, FloorBasketFollower follower, Vector3 point)
    {
        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != PlaneAlignment.HorizontalUp || plane.subsumedBy != null ||
                Mathf.Abs(plane.center.y - follower.floorHeight) > 0.1f || plane.boundary.Length < 3)
                continue;

            var local = plane.transform.InverseTransformPoint(point);
            var planePoint = new Vector2(local.x, local.z);
            if (IsInsidePolygon(plane.boundary, planePoint))
                return $"inside plane {plane.trackableId}, {DistanceToEdge(plane.boundary, planePoint):F3} m from its edge";
        }

        return "outside every floor plane";
    }

    static bool IsInsidePolygon(NativeArray<Vector2> polygon, Vector2 point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }

        return inside;
    }

    static float DistanceToEdge(NativeArray<Vector2> polygon, Vector2 point)
    {
        float closest = float.MaxValue;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var start = polygon[j];
            var edge = polygon[i] - start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, edge) / Mathf.Max(edge.sqrMagnitude, 0.000001f));
            closest = Mathf.Min(closest, (point - (start + edge * t)).magnitude);
        }

        return closest;
    }
}
