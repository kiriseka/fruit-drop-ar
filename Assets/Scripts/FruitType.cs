using UnityEngine;

/// <summary>
/// One kind of falling fruit: how it looks, how fast it falls, what it is worth and how often it appears.
/// The values come from the proposed scoring table and are meant to be tuned in playtests.
/// </summary>
[CreateAssetMenu(fileName = "FruitType", menuName = "Fruit Drop AR/Fruit Type")]
public class FruitType : ScriptableObject
{
    [SerializeField]
    [Tooltip("Name shown to the player.")]
    string m_DisplayName = "Fruit";

    [SerializeField]
    [Tooltip("Model that falls. Its pivot should be at its centre.")]
    GameObject m_Prefab;

    [SerializeField]
    [Tooltip("Radius of the fruit, in meters. Decides when it touches the floor.")]
    float m_Radius = 0.055f;

    [SerializeField]
    [Tooltip("Points for a catch, before the combo multiplier. Negative for fruit to avoid.")]
    int m_Points = 10;

    [SerializeField]
    [Tooltip("Speed of the fall at the start of a round, in meters per second.")]
    float m_FallSpeed = 0.8f;

    [SerializeField]
    [Tooltip("How often this fruit appears compared with the other types. Weights are relative, not percentages.")]
    float m_SpawnWeight = 1f;

    [SerializeField]
    [Tooltip("Extra catches added to the combo streak when this fruit is caught.")]
    int m_StreakBonus;

    [SerializeField]
    [Tooltip("Fruit to avoid: catching it costs a life, and letting it fall costs nothing.")]
    bool m_IsHazard;

    public string displayName => m_DisplayName;
    public GameObject prefab => m_Prefab;
    public float radius => m_Radius;
    public int points => m_Points;
    public float fallSpeed => m_FallSpeed;
    public float spawnWeight => m_SpawnWeight;
    public int streakBonus => m_StreakBonus;
    public bool isHazard => m_IsHazard;
}
