using System;
using UnityEngine;

/// <summary>
/// Plays the game's sounds: a cue where each fruit appears, a pop for a catch, a thud for a miss, a buzz for
/// catching fruit to avoid, and a jingle at Game Over. It only listens to the spawner and the game flow.
/// The cue for a new fruit is placed in the room where the fruit appears, so with headphones the player can hear
/// which side it is on before seeing it. The other sounds are about the round, not a place, and are not placed.
/// </summary>
public class GameAudio : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Reports each fruit that appears, is caught or reaches the floor.")]
    FruitSpawner m_Spawner;

    [SerializeField]
    [Tooltip("Reports Game Over.")]
    GameStateManager m_Game;

    [SerializeField]
    [Tooltip("Played where a fruit appears on the rooftop.")]
    AudioClip m_SpawnClip;

    [SerializeField]
    [Tooltip("Played when fruit is caught.")]
    AudioClip m_CatchClip;

    [SerializeField]
    [Tooltip("Optional. Played instead of the catch sound for fruit that adds to the streak, the golden apple.")]
    AudioClip m_GoldenClip;

    [SerializeField]
    [Tooltip("Played when fruit reaches the floor. Not played for fruit to avoid, which is meant to fall.")]
    AudioClip m_MissClip;

    [SerializeField]
    [Tooltip("Played when fruit to avoid is caught.")]
    AudioClip m_HazardClip;

    [SerializeField]
    [Tooltip("Played when the round ends.")]
    AudioClip m_GameOverClip;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("Loudness of the catch, miss, buzz and Game Over sounds.")]
    float m_Volume = 1f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("Loudness of the cue for a new fruit. Lower than the rest, because late in a round it plays often.")]
    float m_CueVolume = 0.5f;

    [SerializeField]
    [Tooltip("How many cues can sound at once. Late in a round fruit appears faster than one cue ends.")]
    int m_CueVoices = 4;

    [SerializeField]
    [Tooltip("Seconds between the last miss and the Game Over jingle, so the jingle does not cover the thud.")]
    float m_GameOverDelay = 0.4f;

    AudioSource m_Source;
    AudioSource[] m_CueSources;
    int m_NextCue;

    /// <summary>
    /// Called with the clip each time a sound starts.
    /// </summary>
    public event Action<AudioClip> played;

    void Awake()
    {
        // The player's sound setting from the last session.
        GameSettings.ApplySound();

        m_Source = gameObject.AddComponent<AudioSource>();
        m_Source.playOnAwake = false;
        m_Source.spatialBlend = 0f;

        m_CueSources = new AudioSource[Mathf.Max(1, m_CueVoices)];
        for (int i = 0; i < m_CueSources.Length; i++)
        {
            var cue = new GameObject($"Spawn Cue {i}");
            cue.transform.SetParent(transform, false);
            var source = cue.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;

            // Fruit appears 2 to 4 m from the phone, so the cue stays clearly audible over that range.
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 10f;
            m_CueSources[i] = source;
        }
    }

    void OnEnable()
    {
        if (m_Spawner != null)
        {
            m_Spawner.fruitSpawned += OnFruitSpawned;
            m_Spawner.fruitLanded += OnFruitLanded;
        }

        if (m_Game != null)
            m_Game.stateChanged += OnStateChanged;
    }

    void OnDisable()
    {
        if (m_Spawner != null)
        {
            m_Spawner.fruitSpawned -= OnFruitSpawned;
            m_Spawner.fruitLanded -= OnFruitLanded;
        }

        if (m_Game != null)
            m_Game.stateChanged -= OnStateChanged;

        CancelInvoke();
    }

    void OnFruitSpawned(Fruit fruit)
    {
        if (m_SpawnClip == null)
            return;

        var source = m_CueSources[m_NextCue];
        m_NextCue = (m_NextCue + 1) % m_CueSources.Length;
        source.transform.position = fruit.transform.position;
        source.clip = m_SpawnClip;
        source.volume = m_CueVolume;
        source.Play();
        played?.Invoke(m_SpawnClip);
    }

    void OnFruitLanded(Fruit fruit, bool caught)
    {
        bool isHazard = fruit.type.isHazard;
        bool isSpecial = fruit.type.streakBonus > 0 && m_GoldenClip != null;
        if (caught)
            Play(isHazard ? m_HazardClip : isSpecial ? m_GoldenClip : m_CatchClip);
        else if (!isHazard)
            Play(m_MissClip);
    }

    void OnStateChanged(GameStateManager.GameState state)
    {
        if (state == GameStateManager.GameState.GameOver)
            Invoke(nameof(PlayGameOver), m_GameOverDelay);
    }

    void PlayGameOver()
    {
        Play(m_GameOverClip);
    }

    void Play(AudioClip clip)
    {
        if (clip == null)
            return;

        m_Source.PlayOneShot(clip, m_Volume);
        played?.Invoke(clip);
    }
}
