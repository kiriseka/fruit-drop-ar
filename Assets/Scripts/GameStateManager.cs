using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs the flow of the game: scanning the room, waiting for the player to start, then playing.
/// A round only starts on floor found as a plane with room ahead for fruit to land, so the rougher floor
/// estimates never move the basket mid-round. While tracking is lost the round pauses, so fruit cannot fall
/// past a hidden basket.
/// </summary>
public class GameStateManager : MonoBehaviour
{
    public enum GameState
    {
        Scanning,
        Ready,
        Playing,
    }

    [SerializeField]
    [Tooltip("Keeps the basket on the floor; tells whether the floor is a plane and the basket is showing.")]
    FloorBasketFollower m_Floor;

    [SerializeField]
    [Tooltip("Drops the fruit during a round.")]
    FruitSpawner m_Spawner;

    [SerializeField]
    [Tooltip("Shown while the game is ready; starts a round when pressed.")]
    Button m_StartButton;

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

    /// <summary>
    /// Where the game is in its flow.
    /// </summary>
    public GameState state => m_State;

    /// <summary>
    /// True while a round is held because tracking was lost.
    /// </summary>
    public bool isPaused => m_State == GameState.Playing && m_Spawner.isPaused;

    /// <summary>
    /// Called with the new state whenever it changes.
    /// </summary>
    public event Action<GameState> stateChanged;

    void OnEnable()
    {
        if (m_StartButton != null)
            m_StartButton.onClick.AddListener(StartRound);
    }

    void OnDisable()
    {
        if (m_StartButton != null)
            m_StartButton.onClick.RemoveListener(StartRound);
    }

    void Start()
    {
        m_State = GameState.Scanning;
        UpdateStartButton();
    }

    void Update()
    {
        if (m_State == GameState.Playing)
        {
            bool paused = !m_Floor.isBasketVisible;
            if (paused != m_Spawner.isPaused)
            {
                m_Spawner.isPaused = paused;
                Debug.Log(paused ? "[GameStateManager] Tracking lost, round paused" : "[GameStateManager] Tracking back, round resumed");
            }

            return;
        }

        if (Time.unscaledTime < m_NextFloorCheckTime)
            return;

        m_NextFloorCheckTime = Time.unscaledTime + m_FloorCheckInterval;
        bool hasFloor = m_Floor.isBasketVisible && m_Spawner.HasLandingSpot();
        m_FailedChecks = hasFloor ? 0 : m_FailedChecks + 1;
        if (hasFloor)
            SetState(GameState.Ready);
        else if (m_State == GameState.Ready && (m_FailedChecks >= m_FailedChecksToRescan || !m_Floor.isBasketVisible))
            SetState(GameState.Scanning);
    }

    /// <summary>
    /// Starts dropping fruit. Does nothing unless the game is ready.
    /// </summary>
    public void StartRound()
    {
        if (m_State != GameState.Ready)
            return;

        SetState(GameState.Playing);
        m_Spawner.StartSpawning();
    }

    void SetState(GameState state)
    {
        if (state == m_State)
            return;

        Debug.Log($"[GameStateManager] {m_State} -> {state}");
        m_State = state;
        UpdateStartButton();
        stateChanged?.Invoke(state);
    }

    void UpdateStartButton()
    {
        if (m_StartButton != null)
            m_StartButton.gameObject.SetActive(m_State == GameState.Ready);
    }
}
