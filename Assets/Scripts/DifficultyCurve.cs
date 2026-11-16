using UnityEngine;

/// <summary>
/// How a round gets harder the longer it lasts. Every value is a plain function of the round time in seconds,
/// so the curve can be checked without playing a round. The numbers come from the proposed difficulty table
/// and are meant to be tuned in playtests.
/// </summary>
[CreateAssetMenu(fileName = "DifficultyCurve", menuName = "Fruit Drop AR/Difficulty Curve")]
public class DifficultyCurve : ScriptableObject
{
    [Header("Pace")]
    [SerializeField]
    [Tooltip("Seconds between drops at the start of a round.")]
    float m_StartInterval = 2f;

    [SerializeField]
    [Tooltip("Seconds the gap between drops shrinks by for every second of the round.")]
    float m_IntervalDropPerSecond = 0.015f;

    [SerializeField]
    [Tooltip("Shortest gap between drops, in seconds.")]
    float m_MinInterval = 0.7f;

    [SerializeField]
    [Tooltip("How much the fall speed factor grows for every second of the round. The factor starts at 1.")]
    float m_SpeedGainPerSecond = 0.008f;

    [SerializeField]
    [Tooltip("Highest fall speed factor.")]
    float m_MaxSpeedFactor = 1.6f;

    [Header("Spread")]
    [SerializeField]
    [Tooltip("How far to either side of straight ahead, in degrees, fruit lands at the start of a round.")]
    float m_StartHalfAngle = 20f;

    [SerializeField]
    [Tooltip("Seconds into the round at which landing spots reach the edges of the phone's view.")]
    float m_HalfAngleRampSeconds = 60f;

    [Header("Double drops")]
    [SerializeField]
    [Tooltip("Seconds into the round from which a drop can be two fruits at once.")]
    float m_DoubleDropStartSeconds = 30f;

    [SerializeField]
    [Tooltip("Chance, from 0 to 1, that a drop is a double once doubles have started.")]
    float m_DoubleDropChance = 0.25f;

    [SerializeField]
    [Tooltip("Least distance, in meters, between the two landing spots of a double drop.")]
    float m_DoubleDropMinSeparation = 0.5f;

    [SerializeField]
    [Tooltip("Seconds between the two fruits of a double drop reaching the floor. " +
        "Two fruits half a meter apart that land together cannot both be caught, so the second starts higher.")]
    float m_DoubleDropLandingGap = 0.7f;

    [Header("Fruit to avoid")]
    [SerializeField]
    [Tooltip("Share of drops that are fruit to avoid at the start of a round, from 0 to 1.")]
    float m_StartHazardShare = 0.02f;

    [SerializeField]
    [Tooltip("Share of drops that are fruit to avoid once the ramp is over, from 0 to 1.")]
    float m_EndHazardShare = 0.10f;

    [SerializeField]
    [Tooltip("Seconds into the round at which the share of fruit to avoid stops rising.")]
    float m_HazardRampSeconds = 120f;

    /// <summary>
    /// Least distance, in meters, between the two landing spots of a double drop.
    /// </summary>
    public float doubleDropMinSeparation => m_DoubleDropMinSeparation;

    /// <summary>
    /// Seconds between the two fruits of a double drop reaching the floor.
    /// </summary>
    public float doubleDropLandingGap => m_DoubleDropLandingGap;

    /// <summary>
    /// Seconds from one drop to the next at <paramref name="roundTime"/> seconds into a round.
    /// </summary>
    public float SpawnInterval(float roundTime)
    {
        return Mathf.Max(m_MinInterval, m_StartInterval - m_IntervalDropPerSecond * Mathf.Max(0f, roundTime));
    }

    /// <summary>
    /// What every fruit's fall speed is multiplied by at <paramref name="roundTime"/> seconds into a round.
    /// </summary>
    public float SpeedFactor(float roundTime)
    {
        return Mathf.Min(m_MaxSpeedFactor, 1f + m_SpeedGainPerSecond * Mathf.Max(0f, roundTime));
    }

    /// <summary>
    /// How far to either side of straight ahead, in degrees, fruit can land at <paramref name="roundTime"/> seconds
    /// into a round. It widens to <paramref name="widestHalfAngle"/>, half the phone's horizontal field of view,
    /// and never narrows below the starting angle on a phone with a narrow view.
    /// </summary>
    public float LandingHalfAngle(float roundTime, float widestHalfAngle)
    {
        return Mathf.Lerp(m_StartHalfAngle, Mathf.Max(m_StartHalfAngle, widestHalfAngle), Ramp(roundTime, m_HalfAngleRampSeconds));
    }

    /// <summary>
    /// Chance, from 0 to 1, that a drop at <paramref name="roundTime"/> seconds into a round is two fruits.
    /// </summary>
    public float DoubleDropChance(float roundTime)
    {
        return roundTime >= m_DoubleDropStartSeconds ? m_DoubleDropChance : 0f;
    }

    /// <summary>
    /// Share of drops, from 0 to 1, that are fruit to avoid at <paramref name="roundTime"/> seconds into a round.
    /// </summary>
    public float HazardShare(float roundTime)
    {
        return Mathf.Lerp(m_StartHazardShare, m_EndHazardShare, Ramp(roundTime, m_HazardRampSeconds));
    }

    // 0 at the start of the round, 1 from rampSeconds on. A ramp of no length is over at once.
    static float Ramp(float roundTime, float rampSeconds)
    {
        return rampSeconds > 0f ? Mathf.Clamp01(roundTime / rampSeconds) : 1f;
    }
}
