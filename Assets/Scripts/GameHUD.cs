using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the round on screen: the score, the combo multiplier and a heart for each life during a round,
/// the 3-2-1 countdown, and the Game Over panel with the score, the best score and a Restart button.
/// It only displays; the rules live in <see cref="ScoreManager"/> and the flow in <see cref="GameStateManager"/>.
/// </summary>
public class GameHUD : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Where the game is in its flow; decides which parts of the HUD show.")]
    GameStateManager m_Game;

    [SerializeField]
    [Tooltip("Score, multiplier, lives and best score to show.")]
    ScoreManager m_Score;

    [SerializeField]
    [Tooltip("Row at the top with the score, multiplier and hearts. Shown from the countdown until Game Over.")]
    GameObject m_RoundBar;

    [SerializeField]
    [Tooltip("Current score.")]
    TMP_Text m_ScoreText;

    [SerializeField]
    [Tooltip("Multiplier the next catch scores with, such as ×2. Hidden at ×1.")]
    TMP_Text m_ComboText;

    [SerializeField]
    [Tooltip("One heart per starting life, left to right. Lost lives are dimmed rather than hidden, " +
        "so the player can see how many they started with.")]
    Image[] m_Hearts;

    [SerializeField]
    [Tooltip("Opacity of the heart for a life that has been lost.")]
    float m_LostHeartAlpha = 0.25f;

    [SerializeField]
    [Tooltip("Large number in the middle of the screen during the countdown.")]
    TMP_Text m_CountdownText;

    [SerializeField]
    [Tooltip("Panel shown at Game Over. Holds the Restart button, which the GameStateManager shows and wires.")]
    GameObject m_GameOverPanel;

    [SerializeField]
    [Tooltip("Final score on the Game Over panel.")]
    TMP_Text m_FinalScoreText;

    [SerializeField]
    [Tooltip("Best score on the Game Over panel, or a new-best message.")]
    TMP_Text m_BestScoreText;

    void OnEnable()
    {
        if (m_Score != null)
            m_Score.changed += Refresh;

        if (m_Game != null)
            m_Game.stateChanged += OnStateChanged;
    }

    void OnDisable()
    {
        if (m_Score != null)
            m_Score.changed -= Refresh;

        if (m_Game != null)
            m_Game.stateChanged -= OnStateChanged;
    }

    void Start()
    {
        OnStateChanged(m_Game.state);
    }

    void Update()
    {
        // The countdown changes every frame without an event, so it is the one part polled here.
        if (m_CountdownText == null || m_Game.state != GameStateManager.GameState.Countdown)
            return;

        m_CountdownText.text = Mathf.CeilToInt(Mathf.Max(0.01f, m_Game.countdownRemaining)).ToString();
    }

    void OnStateChanged(GameStateManager.GameState state)
    {
        bool inRound = state == GameStateManager.GameState.Countdown ||
            state == GameStateManager.GameState.Playing ||
            state == GameStateManager.GameState.Paused;

        if (m_RoundBar != null)
            m_RoundBar.SetActive(inRound);

        if (m_CountdownText != null)
            m_CountdownText.gameObject.SetActive(state == GameStateManager.GameState.Countdown);

        if (m_GameOverPanel != null)
            m_GameOverPanel.SetActive(state == GameStateManager.GameState.GameOver);

        Refresh();
    }

    void Refresh()
    {
        if (m_Score == null)
            return;

        if (m_ScoreText != null)
            m_ScoreText.text = m_Score.score.ToString();

        if (m_ComboText != null)
        {
            int multiplier = m_Score.multiplier;
            m_ComboText.text = multiplier > 1 ? $"×{multiplier}" : "";
        }

        if (m_Hearts != null)
        {
            for (int i = 0; i < m_Hearts.Length; i++)
            {
                var color = m_Hearts[i].color;
                color.a = i < m_Score.lives ? 1f : m_LostHeartAlpha;
                m_Hearts[i].color = color;
            }
        }

        if (m_FinalScoreText != null)
            m_FinalScoreText.text = m_Score.score.ToString();

        if (m_BestScoreText != null)
            m_BestScoreText.text = m_Score.isNewHighScore ? "New best!" : $"Best {m_Score.highScore}";
    }
}
