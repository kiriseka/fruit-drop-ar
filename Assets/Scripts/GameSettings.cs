using System;
using UnityEngine;

/// <summary>
/// The player's settings: sound and vibration, each on or off, kept on the phone between sessions.
/// Both start on.
/// </summary>
public static class GameSettings
{
    const string k_SoundKey = "FruitDropAR.SoundOn";
    const string k_VibrationKey = "FruitDropAR.VibrationOn";

    /// <summary>
    /// Called after either setting changes.
    /// </summary>
    public static event Action changed;

    /// <summary>
    /// Whether the game plays sounds.
    /// </summary>
    public static bool soundOn
    {
        get => PlayerPrefs.GetInt(k_SoundKey, 1) != 0;
        set => Set(k_SoundKey, soundOn, value);
    }

    /// <summary>
    /// Whether the phone buzzes for a miss or for catching fruit to avoid.
    /// </summary>
    public static bool vibrationOn
    {
        get => PlayerPrefs.GetInt(k_VibrationKey, 1) != 0;
        set => Set(k_VibrationKey, vibrationOn, value);
    }

    /// <summary>
    /// Applies the sound setting to the whole game. Muting the listener silences every sound at once, the cues
    /// placed in the room included, so no sound can be missed.
    /// </summary>
    public static void ApplySound()
    {
        AudioListener.volume = soundOn ? 1f : 0f;
    }

    static void Set(string key, bool current, bool value)
    {
        if (current == value)
            return;

        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
        ApplySound();
        changed?.Invoke();
    }
}
