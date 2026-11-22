using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes fruit types with chosen values for the tests, without touching the real ones in Assets/Data.
/// </summary>
static class TestFruit
{
    public static FruitType Make(int points, int streakBonus = 0, bool isHazard = false, float spawnWeight = 1f)
    {
        var fruitType = ScriptableObject.CreateInstance<FruitType>();
        var serialized = new SerializedObject(fruitType);
        serialized.FindProperty("m_Points").intValue = points;
        serialized.FindProperty("m_StreakBonus").intValue = streakBonus;
        serialized.FindProperty("m_IsHazard").boolValue = isHazard;
        serialized.FindProperty("m_SpawnWeight").floatValue = spawnWeight;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return fruitType;
    }
}

/// <summary>
/// The scoring rules: points, the combo multiplier, lives, fruit to avoid, and the end of a round.
/// </summary>
public class ScoreManagerTests
{
    const string k_BestScoreKey = "FruitDropAR.HighScore";

    GameObject m_Object;
    ScoreManager m_Score;
    FruitType m_Red;
    FruitType m_Golden;
    FruitType m_Rotten;
    bool m_HadBestScore;
    int m_BestScore;

    [SetUp]
    public void SetUp()
    {
        // Scoring saves a new best score, so the editor's own one is put back afterwards.
        m_HadBestScore = PlayerPrefs.HasKey(k_BestScoreKey);
        m_BestScore = PlayerPrefs.GetInt(k_BestScoreKey, 0);

        m_Object = new GameObject("Score Test");
        m_Score = m_Object.AddComponent<ScoreManager>();

        // Awake does not run for a component added in Edit mode, so the round is started by hand.
        m_Score.ResetRound();
        m_Red = TestFruit.Make(10);
        m_Golden = TestFruit.Make(100, streakBonus: 2);
        m_Rotten = TestFruit.Make(-20, isHazard: true);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Object);
        Object.DestroyImmediate(m_Red);
        Object.DestroyImmediate(m_Golden);
        Object.DestroyImmediate(m_Rotten);
        if (m_HadBestScore)
            PlayerPrefs.SetInt(k_BestScoreKey, m_BestScore);
        else
            PlayerPrefs.DeleteKey(k_BestScoreKey);
    }

    void Catch(FruitType fruitType, int times = 1)
    {
        for (int i = 0; i < times; i++)
            m_Score.Apply(fruitType, true);
    }

    [Test]
    public void TwelveCatchesInARow_ScoreWithARisingMultiplier()
    {
        // 5 at x1, 5 at x2, 2 at x3.
        Catch(m_Red, 12);
        Assert.AreEqual(5 * 10 + 5 * 20 + 2 * 30, m_Score.score);
        Assert.AreEqual(12, m_Score.streak);
        Assert.AreEqual(3, m_Score.multiplier);
    }

    [Test]
    public void TheMultiplier_StopsAtFive()
    {
        // 5 each at x1 to x4, then 10 at x5.
        Catch(m_Red, 30);
        Assert.AreEqual(50 + 100 + 150 + 200 + 10 * 50, m_Score.score);
        Assert.AreEqual(5, m_Score.multiplier);
    }

    [Test]
    public void AMiss_EndsTheStreakAndCostsALife()
    {
        Catch(m_Red, 6);
        m_Score.Apply(m_Red, false);
        Assert.AreEqual(70, m_Score.score);
        Assert.AreEqual(0, m_Score.streak);
        Assert.AreEqual(1, m_Score.multiplier);
        Assert.AreEqual(2, m_Score.lives);
    }

    [Test]
    public void AGoldenApple_AddsTwoExtraCatchesToTheStreak()
    {
        Catch(m_Red, 2);
        Catch(m_Golden);
        Assert.AreEqual(120, m_Score.score);
        Assert.AreEqual(5, m_Score.streak);

        // The streak of 5 means the next catch already scores double.
        Catch(m_Red);
        Assert.AreEqual(140, m_Score.score);
    }

    [Test]
    public void ARottenApple_LeftToFall_ChangesNothing()
    {
        Catch(m_Red, 3);
        m_Score.Apply(m_Rotten, false);
        Assert.AreEqual(30, m_Score.score);
        Assert.AreEqual(3, m_Score.streak);
        Assert.AreEqual(3, m_Score.lives);
    }

    [Test]
    public void ARottenApple_Caught_CostsPointsTheStreakAndALife()
    {
        Catch(m_Red, 3);
        Catch(m_Rotten);
        Assert.AreEqual(10, m_Score.score);
        Assert.AreEqual(0, m_Score.streak);
        Assert.AreEqual(2, m_Score.lives);
    }

    [Test]
    public void TheScore_NeverGoesBelowZero()
    {
        Catch(m_Red);
        Catch(m_Rotten);
        Assert.AreEqual(0, m_Score.score);
    }

    [Test]
    public void TheThirdLostLife_EndsTheRoundOnce_AndLaterFruitIsIgnored()
    {
        int ended = 0;
        m_Score.outOfLives += () => ended++;
        Catch(m_Red);
        m_Score.Apply(m_Red, false);
        m_Score.Apply(m_Red, false);
        Assert.AreEqual(0, ended);
        m_Score.Apply(m_Red, false);
        Assert.AreEqual(1, ended);
        Assert.AreEqual(0, m_Score.lives);

        // Fruit still in the air when the round ends must not change the result.
        Catch(m_Red);
        m_Score.Apply(m_Red, false);
        Assert.AreEqual(10, m_Score.score);
        Assert.AreEqual(1, ended);
    }

    [Test]
    public void ThePlannedRoundOfTheSimTest_Scores205()
    {
        var green = TestFruit.Make(15);
        var orange = TestFruit.Make(20);
        var lemon = TestFruit.Make(30);
        Catch(m_Red, 2);                    // 20
        Catch(m_Golden);                    // 120, streak 5
        Catch(m_Red);                       // 140 (x2)
        m_Score.Apply(m_Rotten, false);     // let fall
        Catch(m_Red);                       // 160 (x2)
        Catch(m_Rotten);                    // 140, 2 lives
        m_Score.Apply(m_Red, false);        // 1 life
        Catch(green);                       // 155
        Catch(orange);                      // 175
        Catch(lemon);                       // 205
        m_Score.Apply(m_Red, false);        // 0 lives
        Assert.AreEqual(205, m_Score.score);
        Assert.AreEqual(0, m_Score.lives);

        // The Game Over stats: 8 good fruit (the rotten catch does not count), best streak 7 before the rotten
        // apple reset it.
        Assert.AreEqual(8, m_Score.caughtCount);
        Assert.AreEqual(7, m_Score.bestStreak);
        Object.DestroyImmediate(green);
        Object.DestroyImmediate(orange);
        Object.DestroyImmediate(lemon);
    }

    [Test]
    public void EachCatch_IsReportedWithWhatItChangedTheScoreBy()
    {
        var reported = new System.Collections.Generic.List<int>();
        m_Score.fruitScored += (fruitType, points) => reported.Add(points);
        Catch(m_Red, 5);
        Catch(m_Red);
        m_Score.Apply(m_Red, false);
        Catch(m_Rotten);
        m_Score.Apply(m_Rotten, false);

        // Five at x1, one at x2, nothing for the miss, -20 for the rotten catch, nothing for a rotten fall.
        CollectionAssert.AreEqual(new[] { 10, 10, 10, 10, 10, 20, -20 }, reported);
    }

    [Test]
    public void ARottenCatchNearZero_IsReportedAsWhatItReallyTook()
    {
        int reported = 0;
        m_Score.fruitScored += (fruitType, points) => reported = points;
        Catch(m_Red);
        Catch(m_Rotten);
        Assert.AreEqual(-10, reported);
    }

    [Test]
    public void ANewRound_ClearsTheScoreAndKeepsTheBest()
    {
        Catch(m_Red, 5);
        Assert.IsTrue(m_Score.isNewHighScore);
        m_Score.ResetRound();
        Assert.AreEqual(0, m_Score.score);
        Assert.AreEqual(0, m_Score.streak);
        Assert.AreEqual(3, m_Score.lives);
        Assert.AreEqual(50, m_Score.highScore);
        Assert.IsFalse(m_Score.isNewHighScore);
        Assert.AreEqual(0, m_Score.caughtCount);
        Assert.AreEqual(0, m_Score.bestStreak);
    }
}
