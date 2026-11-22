using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the player sees before a round: the game's title and a line about it under the hint band, the version
/// at the bottom, and a Start button that gently pulses once it can be pressed.
/// Built in code and sized with anchors, like the other panels. Put this on the object that holds the HUD.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class StartScreen : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The game flow; the title shows while scanning and when ready. Found in the scene when left empty.")]
    GameStateManager m_Game;

    [SerializeField]
    [Tooltip("The Start button, which pulses while it can be pressed.")]
    RectTransform m_StartButton;

    [SerializeField]
    [Tooltip("Seconds for one pulse of the Start button.")]
    float m_PulseSeconds = 1.2f;

    [SerializeField]
    [Tooltip("How much bigger the Start button gets at the top of a pulse, as a share of its size.")]
    float m_PulseAmount = 0.06f;

    GameObject m_Title;
    GameObject m_Version;
    float m_PulseAge;

    void Awake()
    {
        if (m_Game == null)
            m_Game = FindAnyObjectByType<GameStateManager>();

        // Under the hint band, which ends 220 units below the top of the safe area.
        var titleBox = UiBuilder.NewRect("Game Title", transform, new Vector2(0.06f, 1f), new Vector2(0.94f, 1f));
        var titleRect = (RectTransform)titleBox.transform;
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -250f);
        titleRect.sizeDelta = new Vector2(0f, 300f);

        // A dark backing like the score bar's, so the title reads over any camera picture, a white wall included.
        var backing = titleBox.AddComponent<Image>();
        backing.color = new Color(UiBuilder.forest.r, UiBuilder.forest.g, UiBuilder.forest.b, 0.7f);
        backing.raycastTarget = false;
        // Bold with an outline, the title draws wider than auto-sizing allows for, so its size is capped below
        // the point where auto-sizing would fill the box.
        var title = UiBuilder.NewText("Title Text", titleBox.transform, new Vector2(0.04f, 0.38f), new Vector2(0.96f, 1f), "Fruit Drop AR", 104f, UiBuilder.lemon, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.outlineWidth = 0.25f;
        title.outlineColor = UiBuilder.forest;
        var tagline = UiBuilder.NewText("Tagline Text", titleBox.transform, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.36f), "Catch the fruit falling onto your floor", 46f, Color.white, TextAlignmentOptions.Center);
        tagline.outlineWidth = 0.2f;
        tagline.outlineColor = UiBuilder.forest;
        m_Title = titleBox;

        var version = UiBuilder.NewText("Version Text", transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), $"v{Application.version}", 36f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Center);
        version.rectTransform.pivot = new Vector2(0.5f, 0f);
        version.rectTransform.anchoredPosition = new Vector2(0f, 40f);
        version.rectTransform.sizeDelta = new Vector2(400f, 56f);
        m_Version = version.gameObject;
    }

    void OnEnable()
    {
        if (m_Game != null)
            m_Game.stateChanged += OnStateChanged;

        Refresh();
    }

    void OnDisable()
    {
        if (m_Game != null)
            m_Game.stateChanged -= OnStateChanged;

        if (m_StartButton != null)
            m_StartButton.localScale = Vector3.one;
    }

    void OnStateChanged(GameStateManager.GameState state)
    {
        Refresh();
    }

    void Refresh()
    {
        if (m_Game == null || m_Title == null)
            return;

        bool beforeRound = m_Game.state == GameStateManager.GameState.Scanning || m_Game.state == GameStateManager.GameState.Ready;
        m_Title.SetActive(beforeRound);
        m_Version.SetActive(beforeRound);
        m_PulseAge = 0f;
        if (m_StartButton != null)
            m_StartButton.localScale = Vector3.one;
    }

    void Update()
    {
        if (m_StartButton == null || m_Game == null || m_Game.state != GameStateManager.GameState.Ready)
            return;

        // A smooth swell and back, starting from the button's normal size.
        m_PulseAge += Time.deltaTime;
        float wave = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * m_PulseAge / Mathf.Max(0.1f, m_PulseSeconds));
        m_StartButton.localScale = Vector3.one * (1f + m_PulseAmount * wave);
    }
}
