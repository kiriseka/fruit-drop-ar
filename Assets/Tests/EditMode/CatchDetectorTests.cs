using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The catch test: a fruit is caught when its path since the last frame goes down through the basket's rim
/// close enough to the middle. The basket here stands away from the origin, so the test also shows that the
/// basket's own position is used.
/// </summary>
public class CatchDetectorTests
{
    static readonly Vector3 k_Basket = new Vector3(1f, 0.5f, 2f);

    GameObject m_Object;
    CatchDetector m_Catcher;
    float m_RimY;

    [SetUp]
    public void SetUp()
    {
        m_Object = new GameObject("Basket Test");
        m_Object.transform.position = k_Basket;
        m_Catcher = m_Object.AddComponent<CatchDetector>();
        m_RimY = k_Basket.y + m_Catcher.rimHeight;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Object);
    }

    // A short step straight down through the rim, the given distance to the side of the basket's middle.
    bool StepDownAt(float offset)
    {
        var above = new Vector3(k_Basket.x + offset, m_RimY + 0.01f, k_Basket.z);
        return m_Catcher.IsCatch(above, above + Vector3.down * 0.02f);
    }

    [Test]
    public void TheDefaults_AreTheTunedRimAndRadius()
    {
        Assert.AreEqual(0.07f, m_Catcher.rimHeight, 0.0001f);
        Assert.AreEqual(0.15f, m_Catcher.catchRadius, 0.0001f);
    }

    [Test]
    public void DownThroughTheMiddle_IsACatch()
    {
        Assert.IsTrue(StepDownAt(0f));
    }

    [Test]
    public void JustInsideTheRadius_IsACatch_AndJustOutsideIsNot()
    {
        Assert.IsTrue(StepDownAt(m_Catcher.catchRadius - 0.001f));
        Assert.IsFalse(StepDownAt(m_Catcher.catchRadius + 0.001f));
        Assert.IsTrue(StepDownAt(-(m_Catcher.catchRadius - 0.001f)));
    }

    [Test]
    public void AStepThatStaysAboveTheRim_IsNotACatch()
    {
        var from = new Vector3(k_Basket.x, m_RimY + 0.5f, k_Basket.z);
        Assert.IsFalse(m_Catcher.IsCatch(from, from + Vector3.down * 0.4f));
    }

    [Test]
    public void AStepThatStartsBelowTheRim_IsNotACatch()
    {
        var from = new Vector3(k_Basket.x, m_RimY - 0.01f, k_Basket.z);
        Assert.IsFalse(m_Catcher.IsCatch(from, from + Vector3.down * 0.02f));
    }

    [Test]
    public void MovingUpThroughTheRim_IsNotACatch()
    {
        var below = new Vector3(k_Basket.x, m_RimY - 0.01f, k_Basket.z);
        Assert.IsFalse(m_Catcher.IsCatch(below, below + Vector3.up * 0.02f));
    }

    [Test]
    public void AVeryLongStep_CannotSkipPastTheRim()
    {
        // A fast fruit on a slow frame: from 3 m above the rim to 1 m below the floor in one step.
        var from = new Vector3(k_Basket.x, m_RimY + 3f, k_Basket.z);
        Assert.IsTrue(m_Catcher.IsCatch(from, from + Vector3.down * 4.5f));
    }

    [Test]
    public void ASlantedPath_IsJudgedWhereItCrossesTheRim()
    {
        // Starts outside the radius and crosses the rim inside it.
        var from = new Vector3(k_Basket.x + 0.4f, m_RimY + 0.3f, k_Basket.z);
        var to = new Vector3(k_Basket.x - 0.2f, m_RimY - 0.3f, k_Basket.z);
        Assert.IsTrue(m_Catcher.IsCatch(from, to), "crosses at 0.1 m from the middle");

        // Ends under the basket, but was still outside the radius at rim height.
        from = new Vector3(k_Basket.x + 0.6f, m_RimY + 0.1f, k_Basket.z);
        to = new Vector3(k_Basket.x, m_RimY - 0.1f, k_Basket.z);
        Assert.IsFalse(m_Catcher.IsCatch(from, to), "crosses at 0.3 m from the middle");
    }

    [Test]
    public void TheBasketsOwnPosition_IsUsed()
    {
        // Above the world origin, where there is no basket.
        var from = new Vector3(0f, m_RimY + 0.01f, 0f);
        Assert.IsFalse(m_Catcher.IsCatch(from, from + Vector3.down * 0.02f));

        // After the basket moves there, the same step is a catch.
        m_Object.transform.position = new Vector3(0f, k_Basket.y, 0f);
        Assert.IsTrue(m_Catcher.IsCatch(from, from + Vector3.down * 0.02f));
    }
}
