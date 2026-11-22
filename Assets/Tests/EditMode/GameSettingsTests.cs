using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The sound and vibration settings: on by default, kept when changed, and the sound setting muting the game.
/// The editor's own saved settings and listener volume are put back after each test.
/// </summary>
public class GameSettingsTests
{
    const string k_SoundKey = "FruitDropAR.SoundOn";
    const string k_VibrationKey = "FruitDropAR.VibrationOn";

    bool m_HadSound;
    bool m_HadVibration;
    int m_Sound;
    int m_Vibration;
    float m_Volume;

    [SetUp]
    public void SetUp()
    {
        m_HadSound = PlayerPrefs.HasKey(k_SoundKey);
        m_HadVibration = PlayerPrefs.HasKey(k_VibrationKey);
        m_Sound = PlayerPrefs.GetInt(k_SoundKey, 1);
        m_Vibration = PlayerPrefs.GetInt(k_VibrationKey, 1);
        m_Volume = AudioListener.volume;
        PlayerPrefs.DeleteKey(k_SoundKey);
        PlayerPrefs.DeleteKey(k_VibrationKey);
    }

    [TearDown]
    public void TearDown()
    {
        Restore(k_SoundKey, m_HadSound, m_Sound);
        Restore(k_VibrationKey, m_HadVibration, m_Vibration);
        AudioListener.volume = m_Volume;
    }

    static void Restore(string key, bool had, int value)
    {
        if (had)
            PlayerPrefs.SetInt(key, value);
        else
            PlayerPrefs.DeleteKey(key);
    }

    [Test]
    public void BothSettings_StartOn()
    {
        Assert.IsTrue(GameSettings.soundOn);
        Assert.IsTrue(GameSettings.vibrationOn);
    }

    [Test]
    public void ASwitchedOffSetting_IsKept()
    {
        GameSettings.vibrationOn = false;
        Assert.IsFalse(GameSettings.vibrationOn);
        Assert.AreEqual(0, PlayerPrefs.GetInt(k_VibrationKey, 1));
        Assert.IsTrue(GameSettings.soundOn, "the other setting is untouched");
    }

    [Test]
    public void SoundOff_MutesTheGame_AndOnBringsItBack()
    {
        GameSettings.soundOn = false;
        Assert.AreEqual(0f, AudioListener.volume);
        GameSettings.soundOn = true;
        Assert.AreEqual(1f, AudioListener.volume);
    }

    [Test]
    public void Changed_IsCalledOnlyForARealChange()
    {
        int calls = 0;
        void Count() => calls++;
        GameSettings.changed += Count;
        GameSettings.soundOn = true;
        Assert.AreEqual(0, calls, "setting the value it already has");
        GameSettings.soundOn = false;
        Assert.AreEqual(1, calls);
        GameSettings.changed -= Count;
    }
}
