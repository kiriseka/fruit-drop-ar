using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Shows a one-line hint telling the user what ARCore needs next, with optional raw session diagnostics.
/// </summary>
public class ARStatusText : MonoBehaviour
{
    [SerializeField]
    TMP_Text m_Text;

    [SerializeField]
    ARPlaneManager m_PlaneManager;

    [SerializeField]
    ARPointCloudManager m_PointCloudManager;

    [SerializeField]
    FloorBasketFollower m_Basket;

    [SerializeField]
    [Tooltip("Optional. Turns the hints into game instructions once the floor is ready.")]
    GameStateManager m_Game;

    [SerializeField]
    [Tooltip("Optional. Adds the round's catches and misses to the debug lines.")]
    FruitSpawner m_Spawner;

    [SerializeField]
    [Tooltip("Also show session state, feature point and plane counts, and the phone's height above the floor under the hint. Always on in the Editor.")]
    bool m_ShowDebug;

    [SerializeField]
    [Tooltip("Seconds between text refreshes.")]
    float m_UpdateInterval = 0.25f;

    float m_NextUpdateTime;

    void Update()
    {
        if (m_Text == null || Time.unscaledTime < m_NextUpdateTime)
            return;

        m_NextUpdateTime = Time.unscaledTime + m_UpdateInterval;

        int planeCount = m_PlaneManager != null ? m_PlaneManager.trackables.count : 0;
        bool showing = m_Basket != null && m_Basket.isBasketVisible;
        var floorSource = m_Basket != null ? m_Basket.floorSource : FloorBasketFollower.FloorSource.None;
        string text = GetHint(showing, floorSource, m_Game);

        if (m_ShowDebug || Application.isEditor)
        {
            // Feature points are what ARCore builds planes from; zero means the camera sees nothing trackable.
            int pointCount = 0;
            if (m_PointCloudManager != null)
            {
                foreach (var cloud in m_PointCloudManager.trackables)
                {
                    if (cloud.positions.HasValue)
                        pointCount += cloud.positions.Value.Length;
                }
            }

            // Compare this with a tape measure from the floor to the phone to check the floor estimate.
            string floorText = floorSource.ToString();
            var camera = Camera.main;
            if (floorSource != FloorBasketFollower.FloorSource.None && camera != null)
                floorText += $", phone {camera.transform.position.y - m_Basket.floorHeight:F2} m above it";

            text += $"\nSession: {ARSession.state} ({ARSession.notTrackingReason})\n" +
                $"Points: {pointCount}   Planes: {planeCount}\n" +
                $"Floor: {floorText}";

            if (m_Game != null)
                text += $"\nGame: {m_Game.state}{(m_Game.isPaused ? " (paused)" : "")}";

            if (m_Spawner != null)
                text += $"   Caught: {m_Spawner.caughtCount}   Missed: {m_Spawner.missedCount}";
        }

        m_Text.text = text;
    }

    static string GetHint(bool showing, FloorBasketFollower.FloorSource floorSource, GameStateManager game)
    {
        switch (ARSession.notTrackingReason)
        {
            case NotTrackingReason.InsufficientFeatures:
                return "Point at a surface with more pattern or texture";
            case NotTrackingReason.ExcessiveMotion:
                return "Move the phone more slowly";
            case NotTrackingReason.InsufficientLight:
                return "Need more light";
        }

        if (game != null && game.state == GameStateManager.GameState.Playing)
            return game.isPaused ? "Paused. Point the phone at the floor to carry on" : "Catch the falling fruit!";

        if (game != null && game.state == GameStateManager.GameState.Ready)
            return "Floor found. Tap Start";

        if (showing && floorSource == FloorBasketFollower.FloorSource.Assumed)
            return "Finding the floor... move the phone slowly";

        // A plane floor but no Start yet means too little floor has been found ahead for fruit to land on.
        if (showing && game != null && floorSource == FloorBasketFollower.FloorSource.Plane)
            return "Scan more of the floor ahead of you";

        if (showing)
            return "Move the phone to move the basket";

        return "Point the phone at the floor and move it slowly";
    }
}
