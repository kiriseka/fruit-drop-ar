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
    [Tooltip("Optional. Round stats on the Game Over panel: fruit caught, best streak and how long the round lasted.")]
    TMP_Text m_StatsText;

    [SerializeField]
    [Tooltip("Optional. Gives how long the round lasted, for the stats.")]
    FruitSpawner m_Spawner;

    [SerializeField]
    [Tooltip("Seconds the score takes to count up, or down, to a new value.")]
    float m_ScoreCountSeconds = 0.3f;

    [SerializeField]
    [Tooltip("Seconds a lost heart shakes and shrinks before it dims.")]
    float m_HeartLossSeconds = 0.4f;

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

    // Animation state: the score as shown while it counts, the multiplier punch, and each heart's loss.
    float m_ShownScore;
    float m_ScoreRate;
    int m_ShownMultiplier = 1;
    float m_ComboPunchAge = -1f;
    int m_ShownLives = -1;
    float[] m_HeartLossAge;
    Vector2[] m_HeartRestPosition;

    void Start()
    {
        if (m_Hearts != null)
        {
            m_HeartLossAge = new float[m_Hearts.Length];
            m_HeartRestPosition = new Vector2[m_Hearts.Length];
            for (int i = 0; i < m_Hearts.Length; i++)
            {
                m_HeartLossAge[i] = -1f;
                m_HeartRestPosition[i] = m_Hearts[i].rectTransform.anchoredPosition;
            }
        }

        OnStateChanged(m_Game.state);
    }

    void Update()
    {
        // The countdown changes every frame without an event, so it is polled here.
        if (m_CountdownText != null && IsCountingDown(m_Game.state))
            m_CountdownText.text = Mathf.CeilToInt(Mathf.Max(0.01f, m_Game.countdownRemaining)).ToString();

        AnimateScore();
        AnimateCombo();
        AnimateHearts();
    }

    void AnimateScore()
    {
        if (m_Score == null || m_ScoreText == null)
            return;

        int target = m_Score.score;
        if (Mathf.Approximately(m_ShownScore, target))
            return;

        m_ShownScore = Mathf.MoveTowards(m_ShownScore, target, Mathf.Max(1f, m_ScoreRate) * Time.deltaTime);
        m_ScoreText.text = Mathf.RoundToInt(m_ShownScore).ToString();
    }

    // A short swell when the multiplier goes up, so the change is noticed.
    void AnimateCombo()
    {
        if (m_ComboPunchAge < 0f || m_ComboText == null)
            return;

        m_ComboPunchAge += Time.deltaTime;
        float t = Mathf.Clamp01(m_ComboPunchAge / 0.25f);
        m_ComboText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, t);
        if (t >= 1f)
            m_ComboPunchAge = -1f;
    }

    // A lost heart shakes and shrinks, then dims to show the life is gone.
    void AnimateHearts()
    {
        if (m_HeartLossAge == null)
            return;

        for (int i = 0; i < m_Hearts.Length; i++)
        {
            if (m_HeartLossAge[i] < 0f)
                continue;

            m_HeartLossAge[i] += Time.deltaTime;
            float t = Mathf.Clamp01(m_HeartLossAge[i] / Mathf.Max(0.01f, m_HeartLossSeconds));
            var rect = m_Hearts[i].rectTransform;
            rect.anchoredPosition = m_HeartRestPosition[i] + Vector2.right * (Mathf.Sin(m_HeartLossAge[i] * 60f) * 12f * (1f - t));
            rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.7f, t);
            if (t >= 1f)
            {
                m_HeartLossAge[i] = -1f;
                SetHeartAtRest(i, false);
            }
        }
    }

    void SetHeartAtRest(int index, bool full)
    {
        var rect = m_Hearts[index].rectTransform;
        rect.anchoredPosition = m_HeartRestPosition[index];
        rect.localScale = Vector3.one;
        var color = m_Hearts[index].color;
        color.a = full ? 1f : m_LostHeartAlpha;
        m_Hearts[index].color = color;
    }

    void OnStateChanged(GameStateManager.GameState state)
    {
        bool inRound = state == GameStateManager.GameState.Countdown ||
            state == GameStateManager.GameState.Playing ||
            state == GameStateManager.GameState.Paused ||
            state == GameStateManager.GameState.Resuming;

        if (m_RoundBar != null)
            m_RoundBar.SetActive(inRound);

        if (m_CountdownText != null)
            m_CountdownText.gameObject.SetActive(IsCountingDown(state));

        if (m_GameOverPanel != null)
            m_GameOverPanel.SetActive(state == GameStateManager.GameState.GameOver);

        // A new round starts from a clean bar rather than counting down from the last score.
        if (state == GameStateManager.GameState.Countdown && m_Score != null)
        {
            m_ShownScore = m_Score.score;
            if (m_ScoreText != null)
                m_ScoreText.text = m_Score.score.ToString();
        }

        Refresh();
    }

    // The 3-2-1 before a round and the one after a pause look the same.
    static bool IsCountingDown(GameStateManager.GameState state)
    {
        return state == GameStateManager.GameState.Countdown || state == GameStateManager.GameState.Resuming;
    }

    void Refresh()
    {
        if (m_Score == null)
            return;

        // The score text itself counts toward the new score in Update.
        m_ScoreRate = Mathf.Abs(m_Score.score - m_ShownScore) / Mathf.Max(0.01f, m_ScoreCountSeconds);

        if (m_ComboText != null)
        {
            int multiplier = m_Score.multiplier;
            m_ComboText.text = multiplier > 1 ? $"×{multiplier}" : "";
            if (multiplier > m_ShownMultiplier && multiplier > 1)
                m_ComboPunchAge = 0f;

            if (multiplier == 1 || m_ComboPunchAge < 0f)
                m_ComboText.rectTransform.localScale = Vector3.one;

            m_ShownMultiplier = multiplier;
        }

        if (m_Hearts != null && m_HeartLossAge != null)
        {
            int lives = m_Score.lives;
            for (int i = 0; i < m_Hearts.Length; i++)
            {
                bool full = i < lives;
                bool justLost = !full && m_ShownLives >= 0 && i < m_ShownLives;
                if (justLost)
                {
                    // Shown full while it shakes; AnimateHearts dims it at the end.
                    m_HeartLossAge[i] = 0f;
                    var color = m_Hearts[i].color;
                    color.a = 1f;
                    m_Hearts[i].color = color;
                }
                else if (m_HeartLossAge[i] < 0f)
                {
                    SetHeartAtRest(i, full);
                }
                else if (full)
                {
                    // Lives refilled mid-shake, for a new round.
                    m_HeartLossAge[i] = -1f;
                    SetHeartAtRest(i, true);
                }
            }

            m_ShownLives = lives;
        }

        if (m_FinalScoreText != null)
            m_FinalScoreText.text = m_Score.score.ToString();

        if (m_BestScoreText != null)
            m_BestScoreText.text = m_Score.isNewHighScore ? "New best!" : $"Best {m_Score.highScore}";

        if (m_StatsText != null)
        {
            // The clock of the round stops while it is paused, so this is the time actually played.
            int seconds = m_Spawner != null ? Mathf.FloorToInt(m_Spawner.roundTime) : 0;
            m_StatsText.text = $"{m_Score.caughtCount} caught · best streak {m_Score.bestStreak} · {seconds / 60}:{seconds % 60:00}";
        }
    }
}
