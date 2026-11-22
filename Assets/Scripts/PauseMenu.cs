using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pause button shown during a round, at the right end of the hint band, and the menu it opens:
/// Resume, Restart, and the sound and vibration switches. The menu also opens when the player leaves the app
/// or presses the back key, because it follows the game's player pause rather than the button alone.
/// Built in code and sized with anchors, like the "How to play" panel. Put this on the object that holds the
/// rest of the HUD.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PauseMenu : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The game flow to pause and resume. Found in the scene when left empty.")]
    GameStateManager m_Game;

    GameObject m_PauseButton;
    GameObject m_Panel;
    TMP_Text m_SoundLabel;
    TMP_Text m_VibrationLabel;

    /// <summary>
    /// True while the pause menu is on screen.
    /// </summary>
    public bool isShowing => m_Panel != null && m_Panel.activeSelf;

    void Awake()
    {
        if (m_Game == null)
            m_Game = FindAnyObjectByType<GameStateManager>();

        Build();
    }

    void OnEnable()
    {
        if (m_Game != null)
        {
            m_Game.stateChanged += OnStateChanged;
            m_Game.playerPauseChanged += OnPlayerPauseChanged;
        }

        GameSettings.changed += RefreshLabels;
        Refresh();
    }

    void OnDisable()
    {
        if (m_Game != null)
        {
            m_Game.stateChanged -= OnStateChanged;
            m_Game.playerPauseChanged -= OnPlayerPauseChanged;
        }

        GameSettings.changed -= RefreshLabels;
    }

    void OnStateChanged(GameStateManager.GameState state)
    {
        Refresh();
    }

    void OnPlayerPauseChanged(bool paused)
    {
        Refresh();
    }

    void Refresh()
    {
        if (m_Game == null || m_Panel == null)
            return;

        var state = m_Game.state;
        bool inRound = state == GameStateManager.GameState.Countdown || state == GameStateManager.GameState.Playing ||
            state == GameStateManager.GameState.Paused || state == GameStateManager.GameState.Resuming;
        bool paused = m_Game.isPausedByPlayer;
        m_PauseButton.SetActive(inRound && !paused);
        m_Panel.SetActive(paused);

        // Drawn last, so it covers the HUD that is built after it.
        if (paused)
            m_Panel.transform.SetAsLastSibling();

        RefreshLabels();
    }

    void RefreshLabels()
    {
        if (m_SoundLabel != null)
            m_SoundLabel.text = GameSettings.soundOn ? "Sound: On" : "Sound: Off";

        if (m_VibrationLabel != null)
            m_VibrationLabel.text = GameSettings.vibrationOn ? "Vibration: On" : "Vibration: Off";
    }

    void Build()
    {
        // The hint band spans 90 to 220 units below the top of the safe area; the button sits at its right end.
        var pause = UiBuilder.NewImage("Pause Button", transform, Vector2.one, Vector2.one, new Color(UiBuilder.forest.r, UiBuilder.forest.g, UiBuilder.forest.b, 0.85f), true);
        var pauseRect = pause.rectTransform;
        pauseRect.pivot = Vector2.one;
        pauseRect.anchoredPosition = new Vector2(-40f, -100f);
        pauseRect.sizeDelta = new Vector2(110f, 110f);
        pause.gameObject.AddComponent<Button>().onClick.AddListener(() => m_Game.PauseByPlayer());

        // Two bars rather than a text glyph, so the symbol does not depend on the font.
        UiBuilder.NewImage("Bar Left", pause.transform, new Vector2(0.30f, 0.25f), new Vector2(0.43f, 0.75f), Color.white);
        UiBuilder.NewImage("Bar Right", pause.transform, new Vector2(0.57f, 0.25f), new Vector2(0.70f, 0.75f), Color.white);
        m_PauseButton = pause.gameObject;

        // The menu: a dim layer that takes every tap, and a card with the buttons.
        m_Panel = UiBuilder.NewImage("Pause Menu", transform, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.55f), true).gameObject;
        var card = UiBuilder.NewImage("Pause Card", m_Panel.transform, new Vector2(0.10f, 0.28f), new Vector2(0.90f, 0.74f),
            new Color(UiBuilder.forest.r, UiBuilder.forest.g, UiBuilder.forest.b, 0.96f), true).transform;
        UiBuilder.NewText("Pause Title", card, new Vector2(0.06f, 0.81f), new Vector2(0.94f, 0.95f), "Paused", 96f, UiBuilder.lemon, TextAlignmentOptions.Center);

        var light = new Color(1f, 1f, 1f, 0.14f);
        UiBuilder.NewButton("Resume Button", card, new Vector2(0.15f, 0.60f), new Vector2(0.85f, 0.76f), "Resume", UiBuilder.lemon, UiBuilder.forest, 72f, () => m_Game.ResumeByPlayer());
        UiBuilder.NewButton("Restart Button Pause", card, new Vector2(0.15f, 0.42f), new Vector2(0.85f, 0.56f), "Restart", light, Color.white, 60f, () => m_Game.RestartFromPause());
        var sound = UiBuilder.NewButton("Sound Button", card, new Vector2(0.15f, 0.24f), new Vector2(0.85f, 0.37f), "", light, Color.white, 56f, () => GameSettings.soundOn = !GameSettings.soundOn);
        var vibration = UiBuilder.NewButton("Vibration Button", card, new Vector2(0.15f, 0.06f), new Vector2(0.85f, 0.19f), "", light, Color.white, 56f, () => GameSettings.vibrationOn = !GameSettings.vibrationOn);
        m_SoundLabel = sound.GetComponentInChildren<TMP_Text>();
        m_VibrationLabel = vibration.GetComponentInChildren<TMP_Text>();
        m_Panel.SetActive(false);
        m_PauseButton.SetActive(false);
    }
}
