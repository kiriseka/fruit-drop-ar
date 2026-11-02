using UnityEngine;

/// <summary>
/// Decides whether falling fruit lands in the basket. A fruit counts as caught when its centre passes down
/// through the rim, close enough to the basket's centre. Testing the path a fruit took since the last frame,
/// rather than using trigger colliders, means a fast fruit or a slow frame can never skip past the rim.
/// </summary>
public class CatchDetector : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Height of the basket rim above the basket's pivot, in meters.")]
    float m_RimHeight = 0.07f;

    [SerializeField]
    [Tooltip("How far from the basket's centre, in meters, a fruit can pass the rim and still count. " +
        "A little wider than the opening keeps catching fair while the basket glides after the phone.")]
    float m_CatchRadius = 0.15f;

    /// <summary>
    /// Height of the rim above the basket's pivot.
    /// </summary>
    public float rimHeight => m_RimHeight;

    /// <summary>
    /// Largest distance from the basket's centre at which a fruit passing the rim counts as caught.
    /// </summary>
    public float catchRadius => m_CatchRadius;

    /// <summary>
    /// True when something moving from <paramref name="from"/> to <paramref name="to"/> passes down through the rim
    /// within the catch radius.
    /// </summary>
    public bool IsCatch(Vector3 from, Vector3 to)
    {
        float rimY = transform.position.y + m_RimHeight;
        if (from.y < rimY || to.y >= rimY)
            return false;

        var crossing = Vector3.Lerp(from, to, (from.y - rimY) / (from.y - to.y));
        var offset = crossing - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= m_CatchRadius * m_CatchRadius;
    }
}
