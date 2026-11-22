using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Builds the game's screen panels in code: boxes, text and buttons placed with anchors, so a panel fits any
/// screen shape inside the safe area. Also holds the game's two colours, so every panel matches.
/// </summary>
public static class UiBuilder
{
    /// <summary>
    /// Dark green of the bars, cards and the text on lemon buttons (1F3A2E).
    /// </summary>
    public static readonly Color forest = new Color(0.122f, 0.227f, 0.180f, 1f);

    /// <summary>
    /// Yellow of the titles and the main buttons (F4C430).
    /// </summary>
    public static readonly Color lemon = new Color(0.957f, 0.769f, 0.188f, 1f);

    /// <summary>
    /// An empty box stretched between two anchor points of its parent, given as fractions of the parent's size.
    /// </summary>
    public static GameObject NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect.gameObject;
    }

    /// <summary>
    /// A box filled with one colour.
    /// </summary>
    public static Image NewImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color color, bool takesTaps = false)
    {
        var image = NewRect(name, parent, anchorMin, anchorMax).AddComponent<Image>();
        image.color = color;
        image.raycastTarget = takesTaps;
        return image;
    }

    /// <summary>
    /// Text that shrinks to fit its box, so the same panel reads on a narrow phone and a wide tablet.
    /// It never takes taps, so a label never hides its button.
    /// </summary>
    public static TextMeshProUGUI NewText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string content, float maxSize, Color color, TextAlignmentOptions alignment)
    {
        var text = NewRect(name, parent, anchorMin, anchorMax).AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.color = color;
        text.alignment = alignment;
        text.enableAutoSizing = true;
        text.fontSizeMin = 16f;
        text.fontSizeMax = maxSize;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// A filled button with a centred label. The label object is named after the button with " Label" added.
    /// </summary>
    public static Button NewButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string label, Color fill, Color labelColor, float maxSize, UnityAction onClick)
    {
        var image = NewImage(name, parent, anchorMin, anchorMax, fill, true);
        var button = image.gameObject.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        NewText(LabelName(name), image.transform, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f), label, maxSize, labelColor, TextAlignmentOptions.Center);
        return button;
    }

    /// <summary>
    /// Name of the label object that <see cref="NewButton"/> makes for a button with this name.
    /// </summary>
    public static string LabelName(string buttonName)
    {
        return buttonName.EndsWith(" Button") ? buttonName.Substring(0, buttonName.Length - 7) + " Label" : buttonName + " Label";
    }
}
