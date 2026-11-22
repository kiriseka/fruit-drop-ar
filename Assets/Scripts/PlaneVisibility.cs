using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Fades the detected-floor overlay once a round starts. While scanning, the planes' cyan fill and white
/// outline show the player how much floor has been found. During a round they would compete with the landing
/// rings and the basket's shadow, so the fill drops to a faint tint and the outline thins. Both come back at
/// Game Over and while scanning. Works on planes found mid-round too.
/// Put this on the XR Origin, next to the AR Plane Manager.
/// </summary>
public class PlaneVisibility : MonoBehaviour
{
    static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

    [SerializeField]
    [Tooltip("The game flow; the overlay fades from the countdown until Game Over. Found in the scene when left empty.")]
    GameStateManager m_Game;

    [SerializeField]
    [Tooltip("The planes to fade. Found on this object when left empty.")]
    ARPlaneManager m_PlaneManager;

    [SerializeField]
    [Tooltip("Share of the fill's own opacity kept during a round. The fill is 33 % opaque, so 0.15 leaves about 5 %.")]
    float m_RoundFillShare = 0.15f;

    [SerializeField]
    [Tooltip("Share of the outline's width kept during a round.")]
    float m_RoundOutlineShare = 0.3f;

    [SerializeField]
    [Tooltip("Seconds the overlay takes to fade out or back in.")]
    float m_FadeSeconds = 0.5f;

    // Each plane's own fill opacity and outline width, read once, so the fade is always a share of those.
    readonly Dictionary<ARPlane, (float fillAlpha, float outlineWidth)> m_Originals = new Dictionary<ARPlane, (float, float)>();
    MaterialPropertyBlock m_Block;
    float m_Visibility = 1f;
    float m_AppliedVisibility = -1f;
    float m_NextRefreshTime;

    /// <summary>
    /// 1 when the overlay is fully shown, 0 when it is as faint as it gets during a round.
    /// </summary>
    public float visibility => m_Visibility;

    void Awake()
    {
        if (m_Game == null)
            m_Game = FindAnyObjectByType<GameStateManager>();

        if (m_PlaneManager == null)
            m_PlaneManager = GetComponent<ARPlaneManager>();

        m_Block = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (m_Game == null || m_PlaneManager == null)
            return;

        var state = m_Game.state;
        bool inRound = state == GameStateManager.GameState.Countdown || state == GameStateManager.GameState.Playing ||
            state == GameStateManager.GameState.Paused || state == GameStateManager.GameState.Resuming;
        m_Visibility = Mathf.MoveTowards(m_Visibility, inRound ? 0f : 1f, Time.deltaTime / Mathf.Max(0.01f, m_FadeSeconds));

        // Every frame while fading; otherwise a few times a second, for planes found since.
        bool fading = !Mathf.Approximately(m_Visibility, m_AppliedVisibility);
        if (!fading && Time.time < m_NextRefreshTime)
            return;

        m_NextRefreshTime = Time.time + 0.25f;
        m_AppliedVisibility = m_Visibility;
        foreach (var plane in m_PlaneManager.trackables)
            Apply(plane);
    }

    void Apply(ARPlane plane)
    {
        var fill = plane.GetComponent<MeshRenderer>();
        var outline = plane.GetComponent<LineRenderer>();
        if (!m_Originals.TryGetValue(plane, out var original))
        {
            float alpha = fill != null && fill.sharedMaterial != null && fill.sharedMaterial.HasProperty(k_BaseColor) ? fill.sharedMaterial.GetColor(k_BaseColor).a : 1f;
            original = (alpha, outline != null ? outline.widthMultiplier : 1f);
            m_Originals[plane] = original;
        }

        float fillShare = Mathf.Lerp(m_RoundFillShare, 1f, m_Visibility);
        if (fill != null && fill.sharedMaterial != null && fill.sharedMaterial.HasProperty(k_BaseColor))
        {
            // Only the first material, the cyan fill; the second one receives the basket's real shadow.
            var color = fill.sharedMaterial.GetColor(k_BaseColor);
            color.a = original.fillAlpha * fillShare;
            fill.GetPropertyBlock(m_Block, 0);
            m_Block.SetColor(k_BaseColor, color);
            fill.SetPropertyBlock(m_Block, 0);
        }

        // The outline is drawn opaque, so it thins instead of fading.
        if (outline != null)
            outline.widthMultiplier = original.outlineWidth * Mathf.Lerp(m_RoundOutlineShare, 1f, m_Visibility);
    }

    /// <summary>
    /// The fill opacity a plane is drawn with now, for checks.
    /// </summary>
    public float FillAlphaOf(ARPlane plane)
    {
        var fill = plane.GetComponent<MeshRenderer>();
        if (fill == null || fill.sharedMaterial == null)
            return -1f;

        fill.GetPropertyBlock(m_Block, 0);
        return m_Block.HasColor(k_BaseColor) ? m_Block.GetColor(k_BaseColor).a : fill.sharedMaterial.GetColor(k_BaseColor).a;
    }
}
