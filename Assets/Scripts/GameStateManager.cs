using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs the flow of the game: scanning the room, waiting for the player to start, a 3-2-1 countdown, the round,
/// and Game Over with a restart.
/// A round only starts on floor found as a plane with room ahead for fruit to land, so the rougher floor
/// estimates never move the basket mid-round. While tracking is lost the round pauses, so fruit cannot fall
/// past a hidden basket. Pausing is a flag the spawner and fruit read, never <c>Time.timeScale</c>, because the
/// basket smooths and times tracking loss with game time.
/// </summary>
public class GameStateManager : MonoBehaviour
{
    public enum GameState
    {
        Scanning,
        Ready,
        Countdown,
        Playing,
        Paused,
        GameOver,
    }

    [SerializeField]
    [Tooltip("Keeps the basket on the floor; tells whether the floor is a plane and the basket is showing.")]
    FloorBasketFollower m_Floor;

    [SerializeField]
    [Tooltip("Drops the fruit during a round.")]
    FruitSpawner m_Spawner;

    [SerializeField]
    [Tooltip("Keeps the score and lives. Losing the last life ends the round.")]
    ScoreManager m_Score;

    [SerializeField]
    [Tooltip("Shown while the game is ready; starts a round when pressed.")]
    Button m_StartButton;

    [SerializeField]
    [Tooltip("Optional. Shown at Game Over; starts a new round when pressed.")]
    Button m_RestartButton;

    [SerializeField]
    [Tooltip("Seconds of 3-2-1 countdown before fruit starts to fall.")]
    float m_CountdownSeconds = 3f;

    [SerializeField]
    [Tooltip("Seconds between checks for enough detected floor while scanning.")]
    float m_FloorCheckInterval = 0.25f;

    [SerializeField]
    [Tooltip("Failed floor checks in a row before Ready falls back to Scanning. Landing spots are picked at random, " +
        "so one failed check on a small floor is not enough to hide the Start button.")]
    int m_FailedChecksToRescan = 4;

    GameState m_State;
    float m_NextFloorCheckTime;
    int m_FailedChecks;
    float m_CountdownRemaining;

    /// <summary>
    /// Where the game is in its flow.
    /// </summary>
    public GameState state => m_State;

    /// <summary>
    /// True while a round or its countdown is held because tracking was lost.
    /// </summary>
    public bool isPaused => m_State == GameState.Paused || (m_State == GameState.Countdown && !m_Floor.isBasketVisible);

    /// <summary>
    /// Seconds left before fruit starts to fall, during the countdown.
    /// </summary>
    public float countdownRemaining => m_State == GameState.Countdown ? m_CountdownRemaining : 0f;

    /// <summary>
    /// Called with the new state whenever it changes.
    /// </summary>
    public event Action<GameState> stateChanged;

    void OnEnable()
    {
        if (m_StartButton != null)
            m_StartButton.onClick.AddListener(StartRound);

        if (m_RestartButton != null)
            m_RestartButton.onClick.AddListener(Restart);

        if (m_Score != null)
            m_Score.outOfLives += OnOutOfLives;
    }

    void OnDisable()
    {
        if (m_StartButton != null)
            m_StartButton.onClick.RemoveListener(StartRound);

        if (m_RestartButton != null)
            m_RestartButton.onClick.RemoveListener(Restart);

        if (m_Score != null)
            m_Score.outOfLives -= OnOutOfLives;
    }

    void Start()
    {
        m_State = GameState.Scanning;
        UpdateButtons();
    }

    void Update()
    {
        switch (m_State)
        {
            case GameState.Scanning:
            case GameState.Ready:
                CheckFloor();
                break;

            case GameState.Countdown:
                // The countdown waits while the basket is hidden, so the round never starts out of view.
                if (!m_Floor.isBasketVisible)
                    break;

                m_CountdownRemaining -= Time.deltaTime;
                if (m_CountdownRemaining <= 0f)
                    BeginPlaying();
                break;

            case GameState.Playing:
                if (!m_Floor.isBasketVisible)
                {
                    m_Spawner.isPaused = true;
                    Debug.Log("[GameStateManager] Tracking lost, round paused");
                    SetState(GameState.Paused);
                }
                break;

            case GameState.Paused:
                if (m_Floor.isBasketVisible)
                {
                    m_Spawner.isPaused = false;
                    Debug.Log("[GameStateManager] Tracking back, round resumed");
                    SetState(GameState.Playing);
                }
                break;
        }
    }

    void CheckFloor()
    {
        if (Time.unscaledTime < m_NextFloorCheckTime)
            return;

        m_NextFloorCheckTime = Time.unscaledTime + m_FloorCheckInterval;
        bool hasFloor = CanStart();
        m_FailedChecks = hasFloor ? 0 : m_FailedChecks + 1;
        if (hasFloor)
            SetState(GameState.Ready);
        else if (m_State == GameState.Ready && (m_FailedChecks >= m_FailedChecksToRescan || !m_Floor.isBasketVisible))
            SetState(GameState.Scanning);
    }

    // Only a plane floor is steady enough for a round; the point and assumed estimates can still jump.
    bool CanStart()
    {
        return m_Floor.isBasketVisible && m_Floor.hasPlaneFloor && m_Spawner.HasLandingSpot();
    }

    /// <summary>
    /// Starts the countdown to a round. Does nothing unless the game is ready and the floor is a plane.
    /// </summary>
    public void StartRound()
    {
        if (m_State != GameState.Ready || !m_Floor.hasPlaneFloor)
            return;

        BeginCountdown();
    }

    /// <summary>
    /// Starts a new round after Game Over: straight into the countdown when the floor is still good,
    /// otherwise back to scanning.
    /// </summary>
    public void Restart()
    {
        if (m_State != GameState.GameOver)
            return;

        if (CanStart())
        {
            BeginCountdown();
        }
        else
        {
            m_FailedChecks = 0;
            SetState(GameState.Scanning);
        }
    }

    void BeginCountdown()
    {
        // Reset now so the HUD shows a fresh score and full lives during the countdown.
        if (m_Score != null)
            m_Score.ResetRound();

        m_CountdownRemaining = m_CountdownSeconds;
        SetState(GameState.Countdown);
    }

    void BeginPlaying()
    {
        SetState(GameState.Playing);
        m_Spawner.StartSpawning();
    }

    void OnOutOfLives()
    {
        if (m_State != GameState.Playing && m_State != GameState.Paused)
            return;

        m_Spawner.StopSpawning();
        m_Spawner.isPaused = false;
        SetState(GameState.GameOver);
    }

    void SetState(GameState state)
    {
        if (state == m_State)
            return;

        Debug.Log($"[GameStateManager] {m_State} -> {state}");
        m_State = state;
        UpdateButtons();
        stateChanged?.Invoke(state);
    }

    void UpdateButtons()
    {
        if (m_StartButton != null)
            m_StartButton.gameObject.SetActive(m_State == GameState.Ready);

        if (m_RestartButton != null)
            m_RestartButton.gameObject.SetActive(m_State == GameState.GameOver);
    }
}
