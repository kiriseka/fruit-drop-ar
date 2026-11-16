using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The difficulty curve against the difficulty table, and the weighted choice of the next fruit.
/// </summary>
public class DifficultyAndPickTests
{
    DifficultyCurve m_Curve;
    FruitType[] m_Types;

    [SetUp]
    public void SetUp()
    {
        // A fresh curve has the table's values as its defaults.
        m_Curve = ScriptableObject.CreateInstance<DifficultyCurve>();

        // Red, green, orange, lemon, golden, rotten: the weights are the planned shares in percent.
        m_Types = new[]
        {
            TestFruit.Make(10, spawnWeight: 40f),
            TestFruit.Make(15, spawnWeight: 25f),
            TestFruit.Make(20, spawnWeight: 20f),
            TestFruit.Make(30, spawnWeight: 10f),
            TestFruit.Make(100, streakBonus: 2, spawnWeight: 3f),
            TestFruit.Make(-20, isHazard: true, spawnWeight: 2f),
        };
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Curve);
        foreach (var fruitType in m_Types)
            Object.DestroyImmediate(fruitType);
    }

    // time, gap between drops, speed factor, spread on a phone seeing 30 degrees each side, rotten share, double chance
    [TestCase(0f, 2.00f, 1.00f, 20f, 0.02f, 0f)]
    [TestCase(30f, 1.55f, 1.24f, 25f, 0.04f, 0.25f)]
    [TestCase(60f, 1.10f, 1.48f, 30f, 0.06f, 0.25f)]
    [TestCase(90f, 0.70f, 1.60f, 30f, 0.08f, 0.25f)]
    [TestCase(120f, 0.70f, 1.60f, 30f, 0.10f, 0.25f)]
    [TestCase(600f, 0.70f, 1.60f, 30f, 0.10f, 0.25f)]
    public void TheCurve_MatchesTheTable(float time, float gap, float speed, float spread, float rotten, float doubles)
    {
        Assert.AreEqual(gap, m_Curve.SpawnInterval(time), 0.001f, "gap");
        Assert.AreEqual(speed, m_Curve.SpeedFactor(time), 0.001f, "speed factor");
        Assert.AreEqual(spread, m_Curve.LandingHalfAngle(time, 30f), 0.01f, "spread");
        Assert.AreEqual(rotten, m_Curve.HazardShare(time), 0.0001f, "rotten share");
        Assert.AreEqual(doubles, m_Curve.DoubleDropChance(time), 0.0001f, "double chance");
    }

    [Test]
    public void TheCurve_OnlyEverGetsHarder()
    {
        for (float time = 1f; time <= 300f; time += 1f)
        {
            Assert.LessOrEqual(m_Curve.SpawnInterval(time), m_Curve.SpawnInterval(time - 1f), $"gap at {time}");
            Assert.GreaterOrEqual(m_Curve.SpeedFactor(time), m_Curve.SpeedFactor(time - 1f), $"speed at {time}");
            Assert.GreaterOrEqual(m_Curve.HazardShare(time), m_Curve.HazardShare(time - 1f), $"rotten share at {time}");
            Assert.GreaterOrEqual(m_Curve.LandingHalfAngle(time, 30f), m_Curve.LandingHalfAngle(time - 1f, 30f), $"spread at {time}");
        }
    }

    [Test]
    public void DoubleDrops_StartAtThirtySeconds()
    {
        Assert.AreEqual(0f, m_Curve.DoubleDropChance(29.9f));
        Assert.AreEqual(0.25f, m_Curve.DoubleDropChance(30f));
    }

    [Test]
    public void APhoneWithANarrowView_KeepsTheStartingSpread()
    {
        Assert.AreEqual(20f, m_Curve.LandingHalfAngle(60f, 15f), 0.01f);
    }

    [Test]
    public void TimeBeforeTheRound_CountsAsItsStart()
    {
        Assert.AreEqual(2f, m_Curve.SpawnInterval(-5f), 0.001f);
        Assert.AreEqual(1f, m_Curve.SpeedFactor(-5f), 0.001f);
        Assert.AreEqual(0.02f, m_Curve.HazardShare(-5f), 0.0001f);
    }

    // Shares in percent of 100,000 seeded picks.
    float[] Shares(float hazardShare)
    {
        const int picks = 100000;
        var random = new System.Random(2026);
        var counts = new int[m_Types.Length];
        for (int i = 0; i < picks; i++)
            counts[System.Array.IndexOf(m_Types, FruitSpawner.PickType(m_Types, hazardShare, (float)random.NextDouble()))]++;

        var shares = new float[counts.Length];
        for (int i = 0; i < counts.Length; i++)
            shares[i] = 100f * counts[i] / picks;

        return shares;
    }

    [Test]
    public void AtTheStart_PicksFollowTheTable()
    {
        var shares = Shares(m_Curve.HazardShare(0f));
        float[] table = { 40f, 25f, 20f, 10f, 3f, 2f };
        for (int i = 0; i < table.Length; i++)
            Assert.AreEqual(table[i], shares[i], 0.5f, $"type {i}");
    }

    [Test]
    public void AfterTwoMinutes_RottenIsATenth_AndTheRestKeepTheirProportions()
    {
        var shares = Shares(m_Curve.HazardShare(120f));

        // The five good types split the other 90 % as 40 : 25 : 20 : 10 : 3.
        float[] table = { 36.73f, 22.96f, 18.37f, 9.18f, 2.76f, 10f };
        for (int i = 0; i < table.Length; i++)
            Assert.AreEqual(table[i], shares[i], 0.5f, $"type {i}");
    }

    [Test]
    public void WithoutAShare_TheWeightsAloneDecide()
    {
        Assert.AreEqual(2f, Shares(-1f)[5], 0.3f);
    }

    [Test]
    public void TheEndsOfTheRoll_StillGiveAType()
    {
        Assert.AreSame(m_Types[0], FruitSpawner.PickType(m_Types, 0.02f, 0f));
        Assert.IsNotNull(FruitSpawner.PickType(m_Types, 0.02f, 0.9999999f));
        Assert.IsNotNull(FruitSpawner.PickType(m_Types, 0.02f, 1f));
    }

    [Test]
    public void AnEmptyList_GivesNothing()
    {
        Assert.IsNull(FruitSpawner.PickType(new FruitType[0], 0.02f, 0.5f));
    }

    [Test]
    public void WithOnlyGoodFruit_AShareForRottenIsIgnored()
    {
        var good = new[] { m_Types[0], m_Types[1] };
        var random = new System.Random(1);
        for (int i = 0; i < 1000; i++)
            Assert.IsFalse(FruitSpawner.PickType(good, 0.5f, (float)random.NextDouble()).isHazard);
    }

    [Test]
    public void ATypeWithNoWeight_IsNeverPicked()
    {
        var unused = TestFruit.Make(5, spawnWeight: 0f);
        var types = new[] { unused, m_Types[0], m_Types[5] };
        var random = new System.Random(1);
        for (int i = 0; i < 1000; i++)
            Assert.AreNotSame(unused, FruitSpawner.PickType(types, 0.1f, (float)random.NextDouble()));

        Object.DestroyImmediate(unused);
    }
}
