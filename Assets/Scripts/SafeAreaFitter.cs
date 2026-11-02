using UnityEngine;

/// <summary>
/// Stretches a UI panel over the part of the screen that is not covered by a notch, camera hole or rounded
/// corners, so nothing placed inside it is cut off. Refits when the safe area or screen size changes,
/// for example when the phone rotates.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    RectTransform m_RectTransform;
    Rect m_FittedArea;
    Vector2Int m_FittedScreenSize;

    void Awake()
    {
        m_RectTransform = GetComponent<RectTransform>();
    }

    void Update()
    {
        var safeArea = Screen.safeArea;
        var screenSize = new Vector2Int(Screen.width, Screen.height);
        if (safeArea == m_FittedArea && screenSize == m_FittedScreenSize)
            return;

        m_FittedArea = safeArea;
        m_FittedScreenSize = screenSize;
        if (screenSize.x <= 0 || screenSize.y <= 0)
            return;

        // Anchors are fractions of the parent canvas, which covers the whole screen.
        m_RectTransform.anchorMin = new Vector2(safeArea.xMin / screenSize.x, safeArea.yMin / screenSize.y);
        m_RectTransform.anchorMax = new Vector2(safeArea.xMax / screenSize.x, safeArea.yMax / screenSize.y);
        m_RectTransform.offsetMin = Vector2.zero;
        m_RectTransform.offsetMax = Vector2.zero;
        Debug.Log($"[SafeAreaFitter] Fitted to {safeArea} on a {screenSize.x}x{screenSize.y} screen");
    }
}
