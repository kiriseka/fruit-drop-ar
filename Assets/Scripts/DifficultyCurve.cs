using UnityEngine;

/// <summary>
/// How a round gets harder the longer it lasts. Every value is a plain function of the round time in seconds,
/// so the curve can be checked without playing a round. The numbers come from the proposed difficulty table
/// and are meant to be tuned in playtests.
/// </summary>
[CreateAssetMenu(fileName = "DifficultyCurve", menuName = "Fruit Drop AR/Difficulty Curve")]
public class DifficultyCurve : ScriptableObject
{
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
