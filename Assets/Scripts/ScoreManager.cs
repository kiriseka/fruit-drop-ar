using System;
using UnityEngine;

/// <summary>
/// Keeps score for a round: points for each catch times the combo multiplier, the streak of catches in a row,
/// the lives left, and the best score on this phone.
/// The multiplier for a catch comes from the streak before it, so catches 1-5 score ×1, catches 6-10 score ×2,
/// and so on up to the cap. A miss, or catching fruit to avoid, ends the streak and costs a life.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    const string k_HighScoreKey = "FruitDropAR.HighScore";

    [SerializeField]
    [Tooltip("Reports each fruit that is caught or reaches the floor.")]
    FruitSpawner m_Spawner;

    [SerializeField]
    [Tooltip("Lives at the start of a round.")]
    int m_StartingLives = 3;

    [SerializeField]
    [Tooltip("Catches in a row needed for each step up in the combo multiplier.")]
    int m_CatchesPerMultiplierStep = 5;

    [SerializeField]
    [Tooltip("Highest combo multiplier.")]
    int m_MaxMultiplier = 5;

    int m_Score;
    int m_Streak;
    int m_Lives;
    int m_HighScore;
    int m_HighScoreAtRoundStart;

    /// <summary>
    /// Called whenever the score, streak or lives change.
    /// </summary>
    public event Action changed;

    /// <summary>
    /// Called once when the last life is lost.
    /// </summary>
    public event Action outOfLives;

    public int score => m_Score;
    public int streak => m_Streak;
    public int lives => m_Lives;
    public int highScore => m_HighScore;

    /// <summary>
    /// True once this round has beaten the best score from before it started.
    /// </summary>
    public bool isNewHighScore => m_Score > m_HighScoreAtRoundStart;

    /// <summary>
    /// Multiplier the next catch will score with.
    /// </summary>
    public int multiplier => Mathf.Min(m_MaxMultiplier, 1 + m_Streak / Mathf.Max(1, m_CatchesPerMultiplierStep));

    void Awake()
    {
        m_HighScore = PlayerPrefs.GetInt(k_HighScoreKey, 0);
        ResetRound();
    }

    void OnEnable()
    {
        if (m_Spawner != null)
            m_Spawner.fruitLanded += OnFruitLanded;
    }

    void OnDisable()
    {
        if (m_Spawner != null)
            m_Spawner.fruitLanded -= OnFruitLanded;

        PlayerPrefs.Save();
    }

    /// <summary>
    /// Clears the score and streak and refills the lives for a new round. The best score is kept.
    /// </summary>
    public void ResetRound()
    {
        m_Score = 0;
        m_Streak = 0;
        m_Lives = m_StartingLives;
        m_HighScoreAtRoundStart = m_HighScore;
        changed?.Invoke();
    }

    void OnFruitLanded(Fruit fruit, bool caught)
    {
        Apply(fruit.type, caught);
    }

    /// <summary>
    /// Scores one fruit that was caught or reached the floor.
    /// </summary>
    public void Apply(FruitType fruitType, bool caught)
    {
        if (m_Lives <= 0)
            return;

        if (fruitType.isHazard)
        {
            // Letting fruit to avoid fall is the right move, so it costs nothing.
            if (!caught)
                return;

            m_Score = Mathf.Max(0, m_Score + fruitType.points);
            m_Streak = 0;
            LoseLife();
        }
        else if (caught)
        {
            m_Score += fruitType.points * multiplier;
            m_Streak += 1 + fruitType.streakBonus;
        }
        else
        {
            m_Streak = 0;
            LoseLife();
        }

        if (m_Score > m_HighScore)
        {
            m_HighScore = m_Score;
            PlayerPrefs.SetInt(k_HighScoreKey, m_HighScore);
        }

        changed?.Invoke();
        if (m_Lives == 0)
        {
            PlayerPrefs.Save();
            outOfLives?.Invoke();
        }
    }

    void LoseLife()
    {
        m_Lives = Mathf.Max(0, m_Lives - 1);
    }
}
