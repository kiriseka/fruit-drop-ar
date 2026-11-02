using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Keeps a single basket on the floor directly under the centre of the screen.
/// The floor height comes from the best source available: a detected plane, then low feature points,
/// then an assumed standing height so the basket shows up as soon as tracking starts.
/// Once floor planes exist, the basket stays inside them, stopping at the edge of the known floor
/// instead of passing through walls or floating past it.
/// </summary>
[RequireComponent(typeof(ARPlaneManager))]
[RequireComponent(typeof(XROrigin))]
public class FloorBasketFollower : MonoBehaviour
{
    /// <summary>
    /// Where the current floor height came from, from least to most reliable.
    /// </summary>
    public enum FloorSource
    {
        None,
        Assumed,
        FeaturePoints,
        Plane,
    }

    [SerializeField]
    [Tooltip("Basket to keep on the floor. Its pivot should be at the bottom.")]
    GameObject m_BasketPrefab;

    [SerializeField]
    [Tooltip("Closest the basket gets to the player, in meters, measured along the floor.")]
    float m_MinDistance = 0.4f;

    [SerializeField]
    [Tooltip("Farthest the basket gets from the player, in meters, measured along the floor.")]
    float m_MaxDistance = 4f;

    [SerializeField]
    [Tooltip("How quickly the basket catches up with the screen centre. Higher is snappier, lower is smoother.")]
    float m_FollowSharpness = 12f;

    [SerializeField]
    [Tooltip("Height of the phone above the floor, in meters, assumed until a better floor estimate exists.")]
    float m_AssumedCameraHeight = 1.3f;

    [SerializeField]
    [Tooltip("Planes and feature points must be at least this far below the phone, in meters, to count as floor.")]
    float m_MinFloorDepth = 0.5f;

    [SerializeField]
    [Tooltip("Planes and feature points more than this far below the phone, in meters, are tracking errors, not floor.")]
    float m_MaxFloorDepth = 2f;

    [SerializeField]
    [Tooltip("Planes within this height of the floor, in meters, count as part of it.")]
    float m_FloorTolerance = 0.1f;

    [SerializeField]
    [Tooltip("How far inside the edge of the detected floor the basket stops, in meters. About the basket's radius.")]
    float m_EdgeInset = 0.15f;

    [SerializeField]
    [Tooltip("Floor planes smaller than this, in square meters, only count when no bigger one is available.")]
    float m_MinFloorPlaneArea = 0.25f;

    [SerializeField]
    [Tooltip("How quickly the basket follows a change in floor height. Lower glides more slowly over brief tracking glitches.")]
    float m_FloorSharpness = 2f;

    [SerializeField]
    [Tooltip("Feature points that must agree on a height, within 10 cm, before it replaces the assumed floor.")]
    int m_FloorClusterPoints = 12;

    [SerializeField]
    [Tooltip("Seconds without tracking before the basket hides and the floor estimate starts over.")]
    float m_TrackingLostGrace = 1f;

    [SerializeField]
    [Tooltip("Hide the plane outlines while the basket is showing.")]
    bool m_HidePlanesWhilePlaying = true;

    const int k_MaxPointSamples = 500;

    // Height band, in meters, that feature points must share to count as one surface.
    const float k_FloorClusterHeight = 0.1f;

    // How far the phone's height can drift, in meters, before an assumed floor is moved to match it.
    // Covers sessions that report the camera at the origin for a frame before its first real pose.
    const float k_AssumedFloorSlack = 0.3f;

    readonly float[] m_PointSamples = new float[k_MaxPointSamples];
    readonly float[] m_SortedSamples = new float[k_MaxPointSamples];
    readonly HashSet<ulong> m_SampledPointIds = new HashSet<ulong>();
    int m_PointSampleCount;
    int m_NextPointSample;

    ARPlaneManager m_PlaneManager;
    ARPointCloudManager m_PointCloudManager;
    Camera m_Camera;
    GameObject m_Basket;
    bool m_IsBasketVisible;
    FloorSource m_FloorSource;
    float m_FloorHeight;
    float m_TargetFloorHeight;
    float m_LoggedFloorHeight;
    float m_LastTrackingTime = float.NegativeInfinity;

    /// <summary>
    /// The basket instance, or null before tracking first starts.
    /// </summary>
    public GameObject basket => m_Basket;

    /// <summary>
    /// True while tracking and the basket is on screen.
    /// </summary>
    public bool isBasketVisible => m_IsBasketVisible;

    /// <summary>
    /// Where the current floor height came from.
    /// </summary>
    public FloorSource floorSource => m_FloorSource;

    /// <summary>
    /// World-space height of the floor the basket rests on.
    /// </summary>
    public float floorHeight => m_FloorHeight;

    /// <summary>
    /// True when the floor height comes from a detected plane, the only source steady enough to play on.
    /// </summary>
    public bool hasPlaneFloor => m_FloorSource == FloorSource.Plane;

    void Awake()
    {
        m_PlaneManager = GetComponent<ARPlaneManager>();
        m_PointCloudManager = GetComponent<ARPointCloudManager>();
        m_Camera = GetComponent<XROrigin>().Camera;
    }

    void Update()
    {
        if (ARSession.state == ARSessionState.SessionTracking)
        {
            m_LastTrackingTime = Time.time;
        }
        else
        {
            // A long tracking loss usually means ARCore reset its map, so old heights no longer apply.
            if (Time.time - m_LastTrackingTime > m_TrackingLostGrace)
            {
                ResetFloor();
                SetBasketVisible(false);
            }

            return;
        }

        if (m_BasketPrefab == null || m_Camera == null)
            return;

        UpdateFloorHeight();

        var target = GetTargetPoint(m_FloorHeight);
        var toCamera = m_Camera.transform.position - target;
        toCamera.y = 0f;

        if (m_Basket == null)
            m_Basket = Instantiate(m_BasketPrefab, target, Quaternion.identity);

        var targetRotation = toCamera.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toCamera) : m_Basket.transform.rotation;

        if (!m_IsBasketVisible)
        {
            // Appear at the target instead of gliding in from wherever it was last hidden.
            m_Basket.transform.SetPositionAndRotation(target, targetRotation);
            SetBasketVisible(true);
        }
        else
        {
            float t = 1f - Mathf.Exp(-m_FollowSharpness * Time.deltaTime);
            m_Basket.transform.SetPositionAndRotation(
                Vector3.Lerp(m_Basket.transform.position, target, t),
                Quaternion.Slerp(m_Basket.transform.rotation, targetRotation, t));
        }

        if (m_HidePlanesWhilePlaying)
            SetPlanesActive(false);
    }

    /// <summary>
    /// Picks the floor height from the most reliable source available this frame.
    /// A source that drops out keeps its last height rather than falling back to a worse guess.
    /// The height the basket uses glides toward the estimate, so brief tracking glitches barely move it.
    /// </summary>
    void UpdateFloorHeight()
    {
        float assumedHeight = m_Camera.transform.position.y - m_AssumedCameraHeight;
        if (TryGetPlaneFloorHeight(out float height))
            SetFloor(height, FloorSource.Plane);
        else if (TryGetPointFloorHeight(out height) && m_FloorSource <= FloorSource.FeaturePoints)
            SetFloor(height, FloorSource.FeaturePoints);
        else if (m_FloorSource == FloorSource.None ||
            (m_FloorSource == FloorSource.Assumed && Mathf.Abs(assumedHeight - m_TargetFloorHeight) > k_AssumedFloorSlack))
            SetFloor(assumedHeight, FloorSource.Assumed);

        m_FloorHeight = Mathf.Lerp(m_FloorHeight, m_TargetFloorHeight, 1f - Mathf.Exp(-m_FloorSharpness * Time.deltaTime));
    }

    /// <summary>
    /// Finds the floor among upward-facing planes: the lowest one big enough to trust, so neither a table
    /// nor a brief scrap of plane wins. Without a big enough plane, the biggest one is used.
    /// </summary>
    bool TryGetPlaneFloorHeight(out float floorHeight)
    {
        bool foundLarge = false;
        float largestArea = 0f;
        floorHeight = 0f;
        float cameraHeight = m_Camera.transform.position.y;
        foreach (var plane in m_PlaneManager.trackables)
        {
            if (!IsUpwardPlane(plane))
                continue;

            // The phone is held well above the floor, so a plane near or above it is not the floor,
            // and one far below it is a tracking error.
            float height = plane.center.y;
            if (height > cameraHeight - m_MinFloorDepth || height < cameraHeight - m_MaxFloorDepth)
                continue;

            float area = plane.size.x * plane.size.y;
            if (area >= m_MinFloorPlaneArea)
            {
                if (!foundLarge || height < floorHeight)
                {
                    floorHeight = height;
                    foundLarge = true;
                }
            }
            else if (!foundLarge && area > largestArea)
            {
                floorHeight = height;
                largestArea = area;
            }
        }

        return foundLarge || largestArea > 0f;
    }

    /// <summary>
    /// Estimates the floor from feature points a plausible distance below the phone. Objects rest on the floor,
    /// so the lowest height that many points agree on is the floor, even when the floor itself is too plain to track.
    /// Scattered points below it are depth errors and are skipped.
    /// </summary>
    bool TryGetPointFloorHeight(out float floorHeight)
    {
        floorHeight = 0f;
        if (m_PointCloudManager == null)
            return false;

        float cameraHeight = m_Camera.transform.position.y;
        float minHeight = cameraHeight - m_MaxFloorDepth;
        float maxHeight = cameraHeight - m_MinFloorDepth;
        foreach (var cloud in m_PointCloudManager.trackables)
        {
            if (!cloud.positions.HasValue || !cloud.identifiers.HasValue)
                continue;

            var positions = cloud.positions.Value;
            var identifiers = cloud.identifiers.Value;
            for (int i = 0; i < positions.Length; i++)
            {
                float height = cloud.transform.TransformPoint(positions[i]).y;

                // ARCore reports the same points every frame. Sampling each one once keeps
                // whatever is currently in view from crowding out the floor seen earlier.
                if (height > minHeight && height < maxHeight && m_SampledPointIds.Add(identifiers[i]))
                    AddPointSample(height);
            }
        }

        if (m_PointSampleCount < m_FloorClusterPoints)
            return false;

        Array.Copy(m_PointSamples, m_SortedSamples, m_PointSampleCount);
        Array.Sort(m_SortedSamples, 0, m_PointSampleCount);

        // Slide a 10 cm window up from the lowest sample and stop at the first one holding enough points.
        int end = 0;
        for (int start = 0; start < m_PointSampleCount; start++)
        {
            while (end < m_PointSampleCount && m_SortedSamples[end] - m_SortedSamples[start] <= k_FloorClusterHeight)
                end++;

            if (end - start >= m_FloorClusterPoints)
            {
                floorHeight = m_SortedSamples[(start + end - 1) / 2];
                return true;
            }
        }

        return false;
    }

    void AddPointSample(float height)
    {
        m_PointSamples[m_NextPointSample] = height;
        m_NextPointSample = (m_NextPointSample + 1) % k_MaxPointSamples;
        m_PointSampleCount = Mathf.Min(m_PointSampleCount + 1, k_MaxPointSamples);
    }

    void SetFloor(float height, FloorSource source)
    {
        if (source != m_FloorSource)
        {
            Debug.Log($"[FloorBasketFollower] Floor from {source} at {height:F2} m, {m_Camera.transform.position.y - height:F2} m below the phone");
            m_LoggedFloorHeight = height;
        }
        else if (Mathf.Abs(height - m_LoggedFloorHeight) > 0.1f)
        {
            Debug.Log($"[FloorBasketFollower] Floor ({source}) moved from {m_LoggedFloorHeight:F2} m to {height:F2} m");
            m_LoggedFloorHeight = height;
        }

        // The first estimate is used directly; later changes glide in UpdateFloorHeight.
        if (m_FloorSource == FloorSource.None)
            m_FloorHeight = height;

        m_FloorSource = source;
        m_TargetFloorHeight = height;
    }

    void ResetFloor()
    {
        m_FloorSource = FloorSource.None;
        m_PointSampleCount = 0;
        m_NextPointSample = 0;
        m_SampledPointIds.Clear();
    }

    static bool IsUpwardPlane(ARPlane plane)
    {
        return plane.alignment == PlaneAlignment.HorizontalUp && plane.subsumedBy == null && plane.trackingState != TrackingState.None;
    }

    /// <summary>
    /// Returns where the line through the screen centre meets the floor, kept between the min and max distance
    /// and, once floor planes exist, inside them.
    /// </summary>
    Vector3 GetTargetPoint(float floorHeight)
    {
        var ray = m_Camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        var origin = ray.origin;
        var flatOffset = Vector3.zero;
        bool reachesFloor = false;

        if (ray.direction.y < -0.001f)
        {
            float distance = (floorHeight - origin.y) / ray.direction.y;
            if (distance > 0f)
            {
                flatOffset = Vector3.ProjectOnPlane(ray.direction * distance, Vector3.up);
                reachesFloor = true;
            }
        }

        if (!reachesFloor)
        {
            // Looking level or upward never meets the floor, so park the basket at the far limit straight ahead.
            flatOffset = Vector3.ProjectOnPlane(ray.direction, Vector3.up).normalized * m_MaxDistance;
        }

        float flatDistance = flatOffset.magnitude;
        if (flatDistance > m_MaxDistance)
        {
            flatOffset *= m_MaxDistance / flatDistance;
        }
        else if (flatDistance < m_MinDistance)
        {
            // Looking almost straight down: push the basket out toward the top of the phone.
            var direction = flatDistance > 0.0001f
                ? flatOffset / flatDistance
                : Vector3.ProjectOnPlane(m_Camera.transform.up, Vector3.up).normalized;
            flatOffset = direction * m_MinDistance;
        }

        var target = new Vector3(origin.x + flatOffset.x, floorHeight, origin.z + flatOffset.z);
        return m_FloorSource == FloorSource.Plane ? ClampToFloor(target) : target;
    }

    /// <summary>
    /// True when the whole basket could rest on the detected floor at a point on the floor level:
    /// its centre and four points the edge inset away from it all lie inside floor planes.
    /// Checking points rather than the distance to a plane's edge also accepts spots where two
    /// neighbouring floor planes meet.
    /// </summary>
    public bool IsOnDetectedFloor(Vector3 point)
    {
        return IsInsideFloorPlane(point) &&
            IsInsideFloorPlane(point + new Vector3(m_EdgeInset, 0f, 0f)) &&
            IsInsideFloorPlane(point - new Vector3(m_EdgeInset, 0f, 0f)) &&
            IsInsideFloorPlane(point + new Vector3(0f, 0f, m_EdgeInset)) &&
            IsInsideFloorPlane(point - new Vector3(0f, 0f, m_EdgeInset));
    }

    bool IsInsideFloorPlane(Vector3 point)
    {
        foreach (var plane in m_PlaneManager.trackables)
        {
            if (!IsUpwardPlane(plane) || Mathf.Abs(plane.center.y - m_TargetFloorHeight) > m_FloorTolerance)
                continue;

            var boundary = plane.boundary;
            if (boundary.Length < 3)
                continue;

            var localPoint = plane.transform.InverseTransformPoint(point);
            if (IsInsidePolygon(boundary, new Vector2(localPoint.x, localPoint.z)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Moves a point on the floor level inside the detected floor planes. A point already inside one is kept;
    /// otherwise it goes to the nearest edge of the floor, pulled in so the whole basket rests on it.
    /// </summary>
    public Vector3 ClampToFloor(Vector3 point)
    {
        bool foundFloor = false;
        float closestDistance = float.MaxValue;
        var closest = point;
        var flatPoint = new Vector2(point.x, point.z);

        foreach (var plane in m_PlaneManager.trackables)
        {
            if (!IsUpwardPlane(plane) || Mathf.Abs(plane.center.y - m_TargetFloorHeight) > m_FloorTolerance)
                continue;

            var boundary = plane.boundary;
            if (boundary.Length < 3)
                continue;

            foundFloor = true;
            var localPoint = plane.transform.InverseTransformPoint(point);
            var planePoint = new Vector2(localPoint.x, localPoint.z);
            if (IsInsidePolygon(boundary, planePoint))
                return point;

            var edgePoint = GetClosestPointOnPolygon(boundary, planePoint);
            edgePoint += Vector2.ClampMagnitude(plane.centerInPlaneSpace - edgePoint, m_EdgeInset);

            var worldPoint = plane.transform.TransformPoint(new Vector3(edgePoint.x, 0f, edgePoint.y));
            float distance = (new Vector2(worldPoint.x, worldPoint.z) - flatPoint).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = worldPoint;
            }
        }

        if (!foundFloor)
            return point;

        closest.y = m_FloorHeight;
        return closest;
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

    static Vector2 GetClosestPointOnPolygon(NativeArray<Vector2> polygon, Vector2 point)
    {
        var closest = polygon[0];
        float closestDistance = float.MaxValue;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var start = polygon[j];
            var edge = polygon[i] - start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, edge) / Mathf.Max(edge.sqrMagnitude, 0.000001f));
            var candidate = start + edge * t;
            float distance = (point - candidate).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = candidate;
            }
        }

        return closest;
    }

    void SetBasketVisible(bool visible)
    {
        if (m_IsBasketVisible == visible)
            return;

        m_IsBasketVisible = visible;
        if (m_Basket != null)
            m_Basket.SetActive(visible);

        if (!visible && m_HidePlanesWhilePlaying)
            SetPlanesActive(true);

        Debug.Log(visible
            ? $"[FloorBasketFollower] Tracking, showing basket (floor from {m_FloorSource})"
            : "[FloorBasketFollower] Tracking lost, hiding basket");
    }

    void SetPlanesActive(bool active)
    {
        // Hidden planes keep being updated by AR Foundation, so the floor height still refines.
        foreach (var plane in m_PlaneManager.trackables)
        {
            if (plane.gameObject.activeSelf != active)
                plane.gameObject.SetActive(active);
        }
    }
}
