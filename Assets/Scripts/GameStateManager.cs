using System;
using UnityEngine;
using UnityEngine.InputSystem;
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

        // Added last, so the values of the states above stay what they were.
        Resuming,
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
    [Tooltip("Seconds of 3-2-1 countdown before fruit moves again after a pause, so the player can find the basket and the fruit first.")]
    float m_ResumeSeconds = 3f;

    [SerializeField]
    [Tooltip("Seconds the floor can be something rougher than a plane during a round before the round pauses. " +
        "Long enough to ride out a flicker, short enough that the basket does not sit at a wrong height for long.")]
    float m_FloorLossGraceSeconds = 0.5f;

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
    float m_FloorLostSeconds;
    bool m_PausedByPlayer;

    /// <summary>
    /// Where the game is in its flow.
    /// </summary>
    public GameState state => m_State;

    /// <summary>
    /// True while a round or its countdown is held because tracking or the floor was lost.
    /// </summary>
    public bool isPaused => m_State == GameState.Paused || (m_State == GameState.Countdown && (!m_Floor.isBasketVisible || m_PausedByPlayer));

    /// <summary>
    /// True while the player has paused the round or its countdown, from the pause button, the back key or by
    /// leaving the app. It stays paused until the player resumes, even when tracking is good.
    /// </summary>
    public bool isPausedByPlayer => m_PausedByPlayer;

    /// <summary>
    /// Called with true when the player pauses and false when they resume or restart.
    /// </summary>
    public event Action<bool> playerPauseChanged;

    /// <summary>
    /// Seconds left before fruit starts to fall, or moves again after a pause, during either countdown.
    /// </summary>
    public float countdownRemaining => m_State == GameState.Countdown || m_State == GameState.Resuming ? m_CountdownRemaining : 0f;

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
        UpdateScreenSleep();
    }

    void Update()
    {
        // On Android the back key arrives as Escape.
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            OnBackPressed();

        switch (m_State)
        {
            case GameState.Scanning:
            case GameState.Ready:
                CheckFloor();
                break;

            case GameState.Countdown:
                // The countdown waits while the basket is hidden, so the round never starts out of view,
                // and while the player has paused it.
                if (!m_Floor.isBasketVisible || m_PausedByPlayer)
                    break;

                m_CountdownRemaining -= Time.deltaTime;
                if (m_CountdownRemaining <= 0f)
                    BeginPlaying();
                break;

            case GameState.Playing:
                // A round runs on a plane floor only. If the floor falls back to a rougher estimate the basket
                // can change height, so that pauses the round too, after a moment's grace for a flicker.
                m_FloorLostSeconds = m_Floor.hasPlaneFloor ? 0f : m_FloorLostSeconds + Time.deltaTime;
                if (!m_Floor.isBasketVisible || m_FloorLostSeconds > m_FloorLossGraceSeconds)
                {
                    m_Spawner.isPaused = true;
                    Debug.Log($"[GameStateManager] {(m_Floor.isBasketVisible ? "Floor plane" : "Tracking")} lost, round paused");
                    SetState(GameState.Paused);
                }
                break;

            case GameState.Paused:
                if (!m_PausedByPlayer && m_Floor.isBasketVisible && m_Floor.hasPlaneFloor)
                {
                    // Fruit stays held through a 3-2-1, so the player can find the basket and the fruit again.
                    m_FloorLostSeconds = 0f;
                    m_CountdownRemaining = m_ResumeSeconds;
                    Debug.Log("[GameStateManager] Tracking and floor back, resuming");
                    SetState(GameState.Resuming);
                }
                break;

            case GameState.Resuming:
                if (m_PausedByPlayer || !m_Floor.isBasketVisible || !m_Floor.hasPlaneFloor)
                {
                    SetState(GameState.Paused);
                    break;
                }

                m_CountdownRemaining -= Time.deltaTime;
                if (m_CountdownRemaining <= 0f)
                {
                    m_Spawner.isPaused = false;
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

        if (CanStartWithRetries())
        {
            BeginCountdown();
        }
        else
        {
            m_FailedChecks = 0;
            SetState(GameState.Scanning);
        }
    }

    /// <summary>
    /// Pauses the round, or holds its countdown, until <see cref="ResumeByPlayer"/>. Does nothing outside a round.
    /// </summary>
    public void PauseByPlayer()
    {
        if (m_PausedByPlayer || !IsInRound(m_State))
            return;

        m_PausedByPlayer = true;
        Debug.Log("[GameStateManager] Paused by the player");
        if (m_State == GameState.Playing || m_State == GameState.Resuming)
        {
            m_Spawner.isPaused = true;
            SetState(GameState.Paused);
        }

        UpdateScreenSleep();
        playerPauseChanged?.Invoke(true);
    }

    /// <summary>
    /// Ends a pause the player made. A round comes back through the 3-2-1 once tracking and the floor are good;
    /// a held countdown carries on.
    /// </summary>
    public void ResumeByPlayer()
    {
        if (!m_PausedByPlayer)
            return;

        m_PausedByPlayer = false;
        Debug.Log("[GameStateManager] Resumed by the player");
        UpdateScreenSleep();
        playerPauseChanged?.Invoke(false);
    }

    /// <summary>
    /// Gives up the paused round and starts a new one: straight into the countdown when the floor is still
    /// good, otherwise back to scanning. Only while the player has paused.
    /// </summary>
    public void RestartFromPause()
    {
        if (!m_PausedByPlayer)
            return;

        m_Spawner.StopSpawning();
        m_Spawner.isPaused = false;
        m_PausedByPlayer = false;
        playerPauseChanged?.Invoke(false);
        Debug.Log("[GameStateManager] Round restarted from the pause menu");
        if (CanStartWithRetries())
        {
            BeginCountdown();
        }
        else
        {
            m_FailedChecks = 0;
            SetState(GameState.Scanning);
        }

        UpdateScreenSleep();
    }

    // Android calls this when the app goes to the background, for a call or the home button. The round is held
    // as a player pause, so it waits for the player instead of carrying on the moment they come back.
    void OnApplicationPause(bool paused)
    {
        if (paused)
            PauseByPlayer();
    }

    void OnBackPressed()
    {
        if (m_PausedByPlayer)
        {
            ResumeByPlayer();
        }
        else if (IsInRound(m_State))
        {
            PauseByPlayer();
        }
        else
        {
            // Outside a round, back leaves the game as Android apps do, but only sends it to the background,
            // so nothing is lost if it was pressed by mistake.
            Debug.Log("[GameStateManager] Back pressed outside a round, leaving the app");
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call<bool>("moveTaskToBack", true);
#endif
        }
    }

    static bool IsInRound(GameState state)
    {
        return state == GameState.Countdown || state == GameState.Playing || state == GameState.Paused || state == GameState.Resuming;
    }

    // Landing spots are picked at random, so one failed check on a small floor does not mean the floor has gone.
    // Try as often as the Ready state would before it falls back to scanning.
    bool CanStartWithRetries()
    {
        bool canStart = false;
        for (int i = 0; i < m_FailedChecksToRescan && !canStart; i++)
            canStart = CanStart();

        return canStart;
    }

    // The screen stays on while the player scans and plays without touching it, and follows the phone's own
    // setting at Game Over and while the player has paused, when the phone may well be put down.
    void UpdateScreenSleep()
    {
        bool keepAwake = m_State != GameState.GameOver && !m_PausedByPlayer;
        Screen.sleepTimeout = keepAwake ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
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
        m_FloorLostSeconds = 0f;
        SetState(GameState.Playing);
        m_Spawner.StartSpawning();
    }

    void OnOutOfLives()
    {
        if (m_State != GameState.Playing && m_State != GameState.Paused && m_State != GameState.Resuming)
            return;

        m_Spawner.StopSpawning();
        m_Spawner.isPaused = false;
        if (m_PausedByPlayer)
        {
            m_PausedByPlayer = false;
            playerPauseChanged?.Invoke(false);
        }

        SetState(GameState.GameOver);
    }

    void SetState(GameState state)
    {
        if (state == m_State)
            return;

        Debug.Log($"[GameStateManager] {m_State} -> {state}");
        m_State = state;
        UpdateButtons();
        UpdateScreenSleep();
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
