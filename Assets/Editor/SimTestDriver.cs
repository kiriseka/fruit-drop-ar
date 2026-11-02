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
