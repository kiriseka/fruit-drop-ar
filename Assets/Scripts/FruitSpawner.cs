using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Drops fruit from a virtual rooftop above the detected floor while a round is running.
/// Every fruit gets a landing spot the player can reach: on the detected floor, ahead of the phone and inside
/// the band the basket covers. Fruit is reused from a pool instead of being created for every drop.
/// </summary>
public class FruitSpawner : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Keeps the basket on the floor. Gives the floor height and which spots are on the detected floor.")]
    FloorBasketFollower m_Floor;

    [SerializeField]
    [Tooltip("Fruit to drop.")]
    FruitType m_FruitType;

    [SerializeField]
    [Tooltip("Height of the rooftop above the floor, in meters, where fruit appears. " +
        "Above the phone, so fruit falls into view rather than appearing in it.")]
    float m_RooftopHeight = 2.5f;

    [SerializeField]
    [Tooltip("Seconds between drops.")]
    float m_SpawnInterval = 2f;

    [SerializeField]
    [Tooltip("Seconds from the start of a round to the first drop.")]
    float m_FirstSpawnDelay = 0.5f;

    [SerializeField]
    [Tooltip("Closest a landing spot can be to the player, in meters, measured along the floor.")]
    float m_MinLandingDistance = 0.8f;

    [SerializeField]
    [Tooltip("Farthest a landing spot can be from the player, in meters, measured along the floor.")]
    float m_MaxLandingDistance = 2.5f;

    [SerializeField]
    [Tooltip("How far to either side of straight ahead, in degrees, a landing spot can be.")]
    float m_LandingHalfAngle = 20f;

    [SerializeField]
    [Tooltip("Random spots tried for each drop before skipping it, for when little floor has been detected.")]
    int m_LandingAttempts = 10;

    readonly List<Fruit> m_ActiveFruit = new List<Fruit>();
    readonly Dictionary<FruitType, Stack<Fruit>> m_Pools = new Dictionary<FruitType, Stack<Fruit>>();
    Camera m_Camera;
    bool m_IsSpawning;
    bool m_IsPaused;
    float m_TimeUntilSpawn;
    int m_CaughtCount;
    int m_MissedCount;

    /// <summary>
    /// Called when a fruit is caught (true) or reaches the floor (false).
    /// </summary>
    public event Action<Fruit, bool> fruitLanded;

    /// <summary>
    /// True between <see cref="StartSpawning"/> and <see cref="StopSpawning"/>.
    /// </summary>
    public bool isSpawning => m_IsSpawning;

    /// <summary>
    /// Fruit caught since the round started.
    /// </summary>
    public int caughtCount => m_CaughtCount;

    /// <summary>
    /// Fruit that reached the floor since the round started.
    /// </summary>
    public int missedCount => m_MissedCount;

    /// <summary>
    /// While true, no fruit is dropped and fruit already falling hangs in the air.
    /// </summary>
    public bool isPaused
    {
        get => m_IsPaused;
        set
        {
            m_IsPaused = value;
            foreach (var fruit in m_ActiveFruit)
                fruit.isPaused = value;
        }
    }

    void Awake()
    {
        if (m_Floor != null)
            m_Camera = m_Floor.GetComponent<XROrigin>().Camera;
    }

    /// <summary>
    /// Starts a round: counts reset and the first fruit drops shortly after.
    /// </summary>
    public void StartSpawning()
    {
        m_IsSpawning = true;
        m_CaughtCount = 0;
        m_MissedCount = 0;
        m_TimeUntilSpawn = m_FirstSpawnDelay;
        isPaused = false;
    }

    /// <summary>
    /// Ends a round and clears away any fruit still falling, without counting it as caught or missed.
    /// </summary>
    public void StopSpawning()
    {
        m_IsSpawning = false;
        foreach (var fruit in m_ActiveFruit)
        {
            fruit.gameObject.SetActive(false);
            GetPool(fruit.type).Push(fruit);
        }

        m_ActiveFruit.Clear();
    }

    void Update()
    {
        if (!m_IsSpawning || m_IsPaused || m_FruitType == null || m_Camera == null)
            return;

        // Counting down with deltaTime rather than comparing against Time.time means a pause also stops the clock.
        m_TimeUntilSpawn -= Time.deltaTime;
        if (m_TimeUntilSpawn > 0f)
            return;

        m_TimeUntilSpawn += m_SpawnInterval;
        if (TryPickLandingSpot(out var spot))
            Spawn(m_FruitType, spot);
        else
            Debug.Log("[FruitSpawner] No reachable spot on the detected floor, skipping this drop");
    }

    /// <summary>
    /// Picks a random spot on the detected floor ahead of the phone, within the landing distance band.
    /// </summary>
    bool TryPickLandingSpot(out Vector3 spot)
    {
        var cameraTransform = m_Camera.transform;
        var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);

        // Looking straight down has no forward direction, so use the top of the phone, as the basket does.
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);

        forward.Normalize();
        for (int i = 0; i < m_LandingAttempts; i++)
        {
            var direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-m_LandingHalfAngle, m_LandingHalfAngle), Vector3.up) * forward;
            spot = cameraTransform.position + direction * UnityEngine.Random.Range(m_MinLandingDistance, m_MaxLandingDistance);
            spot.y = m_Floor.floorHeight;
            if (m_Floor.IsOnDetectedFloor(spot))
                return true;
        }

        spot = default;
        return false;
    }

    void Spawn(FruitType fruitType, Vector3 spot)
    {
        var pool = GetPool(fruitType);
        var fruit = pool.Count > 0 ? pool.Pop() : Instantiate(fruitType.prefab, transform).GetComponent<Fruit>();
        m_ActiveFruit.Add(fruit);
        fruit.Launch(fruitType, spot + Vector3.up * m_RooftopHeight, 1f, m_Floor, OnFruitLanded);
    }

    void OnFruitLanded(Fruit fruit, bool caught)
    {
        m_ActiveFruit.Remove(fruit);
        GetPool(fruit.type).Push(fruit);
        if (caught)
            m_CaughtCount++;
        else
            m_MissedCount++;

        Debug.Log($"[FruitSpawner] {fruit.type.displayName} {(caught ? "caught" : "missed")} ({m_CaughtCount} caught, {m_MissedCount} missed)");
        fruitLanded?.Invoke(fruit, caught);
    }

    Stack<Fruit> GetPool(FruitType fruitType)
    {
        if (!m_Pools.TryGetValue(fruitType, out var pool))
        {
            pool = new Stack<Fruit>();
            m_Pools.Add(fruitType, pool);
        }

        return pool;
    }
}
