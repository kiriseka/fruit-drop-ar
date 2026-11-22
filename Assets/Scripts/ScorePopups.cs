using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Short texts that show what a catch was worth: "+20" rising from the basket and fading, red for fruit to
/// avoid, and a "Combo ×N" call-out under the score bar when the multiplier goes up.
/// The texts are reused rather than created for every catch. Put this on the object that holds the rest of
/// the HUD; it builds its texts there.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ScorePopups : MonoBehaviour
{
    class Popup
    {
        public TextMeshProUGUI text;
        public Vector2 start;
        public float age = -1f;
        public float lifetime;
        public float rise;
    }

    [SerializeField]
    [Tooltip("Reports the points of each catch and the multiplier.")]
    ScoreManager m_Score;

    [SerializeField]
    [Tooltip("Gives the basket, which the points rise from.")]
    FloorBasketFollower m_Floor;

    [SerializeField]
    [Tooltip("Colour of the points that fruit to avoid takes away.")]
    Color m_LossColor = new Color(0.90f, 0.22f, 0.21f, 1f);

    [SerializeField]
    [Tooltip("Seconds a points text stays on screen.")]
    float m_Lifetime = 0.9f;

    [SerializeField]
    [Tooltip("How far a points text rises, in canvas units.")]
    float m_Rise = 140f;

    readonly List<Popup> m_Popups = new List<Popup>();
    Popup m_Callout;
    RectTransform m_Rect;
    Camera m_Camera;
    int m_LastMultiplier = 1;

    /// <summary>
    /// Called with the text each time a points text or a call-out appears.
    /// </summary>
    public event Action<string> shown;

    void Awake()
    {
        m_Rect = GetComponent<RectTransform>();
        if (m_Score == null)
            m_Score = FindAnyObjectByType<ScoreManager>();

        if (m_Floor == null)
            m_Floor = FindAnyObjectByType<FloorBasketFollower>();

        // Under the score bar, which ends 370 units below the top of the safe area.
        var callout = UiBuilder.NewText("Combo Callout", transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), "", 96f, UiBuilder.lemon, TextAlignmentOptions.Center);
        callout.rectTransform.pivot = new Vector2(0.5f, 1f);
        callout.rectTransform.sizeDelta = new Vector2(760f, 150f);
        callout.outlineWidth = 0.2f;
        callout.outlineColor = UiBuilder.forest;
        m_Callout = new Popup { text = callout, start = new Vector2(0f, -400f), lifetime = 1.1f, rise = 40f };
        callout.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (m_Score == null)
            return;

        m_Score.fruitScored += OnFruitScored;
        m_Score.changed += OnScoreChanged;
    }

    void OnDisable()
    {
        if (m_Score == null)
            return;

        m_Score.fruitScored -= OnFruitScored;
        m_Score.changed -= OnScoreChanged;
    }

    void OnFruitScored(FruitType fruitType, int points)
    {
        string text;
        if (fruitType.isHazard)
            text = points < 0 ? $"-{-points}" : "Rotten!";
        else
            text = $"+{points}";

        var popup = GetFreePopup();
        popup.text.text = text;
        popup.text.color = fruitType.isHazard ? m_LossColor : UiBuilder.lemon;

        // A golden apple's hundred points stand out.
        popup.text.fontSizeMax = points >= 100 ? 120f : 84f;
        popup.start = BasketPosition();
        Show(popup);
    }

    void OnScoreChanged()
    {
        int multiplier = m_Score.multiplier;
        if (multiplier > m_LastMultiplier && multiplier > 1)
        {
            m_Callout.text.text = $"Combo ×{multiplier}";
            Show(m_Callout);
        }

        m_LastMultiplier = multiplier;
    }

    void Show(Popup popup)
    {
        popup.age = 0f;
        popup.text.gameObject.SetActive(true);
        popup.text.transform.SetAsLastSibling();
        Place(popup, 0f);
        shown?.Invoke(popup.text.text);
    }

    Popup GetFreePopup()
    {
        foreach (var popup in m_Popups)
        {
            if (popup.age < 0f)
                return popup;
        }

        var text = UiBuilder.NewText("Score Popup", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "", 84f, UiBuilder.lemon, TextAlignmentOptions.Center);
        text.rectTransform.sizeDelta = new Vector2(400f, 130f);
        text.outlineWidth = 0.2f;
        text.outlineColor = UiBuilder.forest;
        var created = new Popup { text = text, lifetime = m_Lifetime, rise = m_Rise };
        m_Popups.Add(created);
        return created;
    }

    // Just above the basket on screen, in this panel's units, but never so high that the text would rise into
    // the score bar or the combo call-out under it (together 560 units from the top). Off screen, the middle
    // of the lower half.
    Vector2 BasketPosition()
    {
        var position = BasketScreenPosition();
        float highest = m_Rect.rect.height * 0.5f - 560f - m_Rise;
        position.y = Mathf.Min(position.y, highest);
        return position;
    }

    Vector2 BasketScreenPosition()
    {
        if (m_Camera == null)
            m_Camera = Camera.main;

        if (m_Floor == null || m_Floor.basket == null || m_Camera == null)
            return new Vector2(0f, -200f);

        var screenPoint = m_Camera.WorldToScreenPoint(m_Floor.basket.transform.position + Vector3.up * 0.3f);
        if (screenPoint.z <= 0f)
            return new Vector2(0f, -200f);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Rect, screenPoint, null, out var local);
        return local;
    }

    void Update()
    {
        foreach (var popup in m_Popups)
            Animate(popup);

        Animate(m_Callout);
    }

    void Animate(Popup popup)
    {
        if (popup.age < 0f)
            return;

        popup.age += Time.deltaTime;
        float t = popup.age / popup.lifetime;
        if (t >= 1f)
        {
            popup.age = -1f;
            popup.text.gameObject.SetActive(false);
            return;
        }

        Place(popup, t);
    }

    // A quick pop in size, a rise that slows down, and a fade over the second half.
    static void Place(Popup popup, float t)
    {
        float rise = 1f - (1f - t) * (1f - t);
        popup.text.rectTransform.anchoredPosition = popup.start + Vector2.up * popup.rise * rise;
        float scale = t < 0.15f ? Mathf.Lerp(1.4f, 1f, t / 0.15f) : 1f;
        popup.text.rectTransform.localScale = Vector3.one * scale;
        var color = popup.text.color;
        color.a = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
        popup.text.color = color;
    }
}
