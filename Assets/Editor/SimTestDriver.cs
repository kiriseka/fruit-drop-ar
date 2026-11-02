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
/// </summary>
[InitializeOnLoad]
public static class SimTestDriver
{
    const string k_LogPath = "Temp/SimTest.log";
    const string k_PendingKey = "SimTestDriver.Pending";

    static IEnumerator<float> s_Routine;
    static float s_ResumeTime;
    static bool s_ExitWhenDone;

    // Entering Play mode reloads scripts, so a scenario requested from Edit mode is kept in SessionState
    // and started here once the reload is over.
    static SimTestDriver()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            string pending = SessionState.GetString(k_PendingKey, "");
            if (state != PlayModeStateChange.EnteredPlayMode || pending == "")
                return;

            SessionState.EraseString(k_PendingKey);
            Run(pending);
            s_ExitWhenDone = true;
        };
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
            Log("Done");
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
        float start = Time.time;
        float startYaw = simCamera.eulerAngles.y;
        while (!follower.hasPlaneFloor && Time.time - start < 30f)
        {
            simCamera.rotation = Quaternion.Euler(40f, startYaw + 25f * Mathf.Sin(Time.time - start), 0f);
            yield return 0.25f;
        }

        simCamera.rotation = Quaternion.Euler(40f, startYaw, 0f);

        Log(follower.hasPlaneFloor
            ? $"Plane floor after {Time.time - start:F1} s"
            : "FAIL: no plane floor after 30 s");
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

    /// <summary>
    /// V0.4 step 5: the landing marker stays on the floor under a falling fruit and disappears with it.
    /// Saves a Game view capture with the fruit held low in view.
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
        var fruitType = AssetDatabase.LoadAssetAtPath<FruitType>("Assets/Data/RedApple.asset");
        var fruit = Object.Instantiate(fruitType.prefab).GetComponent<Fruit>();
        var marker = fruit.GetComponentInChildren<LandingMarker>(true);
        if (marker == null)
        {
            Log("FAIL: the fruit prefab has no LandingMarker");
            yield break;
        }

        var camera = Camera.main.transform;
        var forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, forward);
        var spot = camera.position + forward * 1.0f + right * 0.3f;
        spot.y = follower.floorHeight;

        bool landed = false;
        fruit.Launch(fruitType, spot + Vector3.up * 2.5f, 1f, follower, (f, c) => landed = true);
        while (fruit.transform.position.y > follower.floorHeight + 0.5f)
            yield return 0f;

        fruit.isPaused = true;
        yield return 0.5f;
        var markerPosition = marker.transform.position;
        var fruitPosition = fruit.transform.position;
        Log($"Marker {Vector2.Distance(new Vector2(markerPosition.x, markerPosition.z), new Vector2(fruitPosition.x, fruitPosition.z)):F4} m from under the fruit, " +
            $"{markerPosition.y - follower.floorHeight:F4} m above the floor, active {marker.gameObject.activeInHierarchy}");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/SimTest-marker.png"));
        yield return 1f;

        fruit.isPaused = false;
        while (!landed)
            yield return 0.1f;

        Log($"After landing: marker visible {marker.gameObject.activeInHierarchy}");
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
                Fruit nextToLand = null;
                foreach (var fruit in spawner.GetComponentsInChildren<Fruit>())
                {
                    if (nextToLand == null || fruit.transform.position.y < nextToLand.transform.position.y)
                        nextToLand = fruit;

                    if (!seen.Add(fruit))
                        continue;

                    spawned++;
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

            GameObject.Find("Status Canvas").transform.Find("Start Button").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            var countdown = WaitForPlaying(game);
            while (countdown.MoveNext())
                yield return countdown.Current;

            Log($"Pressed Start: game {game.state}, spawning {spawner.isSpawning}");
        }
        else
        {
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
        var button = GameObject.Find("Status Canvas").transform.Find("Start Button").GetComponent<UnityEngine.UI.Button>();
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
            yield return 0.25f;

        heldY = falling.transform.position.y;
        yield return 0.5f;
        Log($"Tracking back after {Time.time - resumeStart - 0.5f:F1} s: paused {game.isPaused}, fruit moved {Mathf.Abs(falling.transform.position.y - heldY):F3} m in 0.5 s, hint \"{Hint()}\"");
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
        GameObject.Find("Status Canvas").transform.Find("Start Button").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
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
        Log($"Tracking back: {game.state} after {Time.time - resumeStart:F1} s, spawner paused {spawner.isPaused}");

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
