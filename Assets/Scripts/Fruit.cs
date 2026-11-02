using System;
using UnityEngine;

/// <summary>
/// A fruit falling from the rooftop. It drops straight down at a steady speed, counts as caught if it passes
/// down through the basket rim, and as missed when it reaches the floor. Either way it hides itself and reports
/// back to whoever launched it, which can reuse it for the next drop.
/// </summary>
public class Fruit : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Degrees per second the fruit turns while it falls.")]
    float m_SpinSpeed = 90f;

    FruitType m_Type;
    FloorBasketFollower m_Floor;
    CatchDetector m_Catcher;
    float m_SpeedFactor;
    Action<Fruit, bool> m_Landed;

    /// <summary>
    /// What kind of fruit this is, set at launch.
    /// </summary>
    public FruitType type => m_Type;

    /// <summary>
    /// The floor the fruit falls toward, set at launch.
    /// </summary>
    public FloorBasketFollower floor => m_Floor;

    /// <summary>
    /// While true the fruit hangs where it is, for example while tracking is lost.
    /// Pausing this way rather than with Time.timeScale keeps the basket following the phone.
    /// </summary>
    public bool isPaused { get; set; }

    /// <summary>
    /// Starts a fall from <paramref name="position"/>. <paramref name="landed"/> is called once, with true for a catch
    /// and false for a miss.
    /// </summary>
    public void Launch(FruitType fruitType, Vector3 position, float speedFactor, FloorBasketFollower floor, Action<Fruit, bool> landed)
    {
        m_Type = fruitType;
        m_SpeedFactor = speedFactor;
        m_Floor = floor;
        m_Landed = landed;
        isPaused = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        gameObject.SetActive(true);
    }

    void Update()
    {
        if (isPaused || m_Type == null || m_Floor == null)
            return;

        var from = transform.position;
        var to = from + Vector3.down * (m_Type.fallSpeed * m_SpeedFactor * Time.deltaTime);
        transform.position = to;
        transform.Rotate(0f, m_SpinSpeed * Time.deltaTime, 0f, Space.World);

        // The basket is created once tracking starts, so look for its catcher until it exists.
        if (m_Catcher == null && m_Floor.basket != null)
            m_Catcher = m_Floor.basket.GetComponent<CatchDetector>();

        if (m_Catcher != null && m_Floor.isBasketVisible && m_Catcher.IsCatch(from, to))
            Land(true);
        else if (to.y - m_Type.radius <= m_Floor.floorHeight)
            Land(false);
    }

    void Land(bool caught)
    {
        gameObject.SetActive(false);
        var landed = m_Landed;
        m_Landed = null;
        landed?.Invoke(this, caught);
    }
}
