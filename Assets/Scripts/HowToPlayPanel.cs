using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shown the first time the game is opened: how to play in a few lines, and a reminder to check the space around
/// before moving about with the phone. It covers the screen, so the Start button cannot be tapped until
/// "Got it" is, and it is remembered on the phone so it only appears once.
/// The panel is built here in code and sized with anchors, so it fits any screen shape inside the safe area.
/// Put this on the object that holds the rest of the HUD.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class HowToPlayPanel : MonoBehaviour
{
    const string k_SeenKey = "FruitDropAR.SeenHowToPlay";

    [SerializeField]
    [TextArea(4, 10)]
    [Tooltip("How to play, one short paragraph per step.")]
    string m_Instructions =
        "1. Point the phone at the floor. The basket follows the middle of the screen.\n\n" +
        "2. Move the basket under the falling fruit. The ring on the floor shows where each one will land.\n\n" +
        "3. A red ring means a rotten apple: let it fall.\n\n" +
        "4. Three misses end the round.";

    [SerializeField]
    [TextArea(2, 5)]
    [Tooltip("Safety reminder shown under the instructions.")]
    string m_SafetyNote = "Make sure the space around you is clear, and keep an eye on your surroundings while you play.";

    [SerializeField]
    [Tooltip("Colour of the card, matching the Game Over panel.")]
    Color m_CardColor = new Color(0.122f, 0.227f, 0.180f, 0.96f);

    [SerializeField]
    [Tooltip("Colour of the title, the safety note and the button, matching the Start button.")]
    Color m_AccentColor = new Color(0.957f, 0.769f, 0.188f, 1f);

    GameObject m_Panel;

    /// <summary>
    /// True while the panel is on screen.
    /// </summary>
    public bool isShowing => m_Panel != null && m_Panel.activeSelf;

    void Start()
    {
        if (PlayerPrefs.GetInt(k_SeenKey, 0) == 0)
            Build();
    }

    /// <summary>
    /// Closes the panel and remembers that it has been read.
    /// </summary>
    public void Dismiss()
    {
        PlayerPrefs.SetInt(k_SeenKey, 1);
        PlayerPrefs.Save();
        if (m_Panel != null)
            m_Panel.SetActive(false);

        Debug.Log("[HowToPlayPanel] Read and closed");
    }

    void Build()
    {
        // A dim layer over the whole HUD. It takes the taps, so nothing behind it can be pressed.
        m_Panel = NewRect("How To Play", transform, Vector2.zero, Vector2.one);
        m_Panel.transform.SetAsLastSibling();
        m_Panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        // The card ends above the Start button, so the button is not half covered behind it, and a quick second
        // tap on "Got it" does not land on Start. Its top stays under the hint band.
        var card = NewRect("Card", m_Panel.transform, new Vector2(0.07f, 0.20f), new Vector2(0.93f, 0.88f));
        card.AddComponent<Image>().color = m_CardColor;

        NewText("Title", card.transform, new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.96f), "How to play", 96f, m_AccentColor, TextAlignmentOptions.Center);
        NewText("Instructions", card.transform, new Vector2(0.07f, 0.38f), new Vector2(0.93f, 0.80f), m_Instructions, 48f, Color.white, TextAlignmentOptions.TopLeft);
        NewText("Safety Note", card.transform, new Vector2(0.07f, 0.21f), new Vector2(0.93f, 0.36f), m_SafetyNote, 44f, m_AccentColor, TextAlignmentOptions.Center);

        var button = NewRect("Got It Button", card.transform, new Vector2(0.25f, 0.04f), new Vector2(0.75f, 0.18f));
        var image = button.AddComponent<Image>();
        image.color = m_AccentColor;
        button.AddComponent<Button>().onClick.AddListener(Dismiss);
        var label = NewText("Label", button.transform, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f), "Got it", 72f, new Color(0.122f, 0.227f, 0.180f, 1f), TextAlignmentOptions.Center);

        // Only the button itself should take the tap, not its label.
        label.raycastTarget = false;
        Debug.Log("[HowToPlayPanel] First run, showing how to play");
    }

    static GameObject NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect.gameObject;
    }

    // Text shrinks to fit its box, so the same panel reads on a narrow phone and a wide tablet.
    static TextMeshProUGUI NewText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string content, float maxSize, Color color, TextAlignmentOptions alignment)
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
}
