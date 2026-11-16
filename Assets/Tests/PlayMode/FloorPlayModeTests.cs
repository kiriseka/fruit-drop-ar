using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// The V0.3 floor behaviour in XR Simulation: the floor is found as a plane at the height of the simulated
/// room's floor, the basket stands on it, and a point far outside the room is pulled back onto the floor.
/// </summary>
public class FloorPlayModeTests
{
    [UnityTest]
    public IEnumerator ThePlaneFloorIsFound_AndTheBasketStaysOnIt()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);

        FloorBasketFollower follower = null;
        GameObject simCamera = null;
        float start = Time.time;
        while ((follower == null || simCamera == null) && Time.time - start < 10f)
        {
            follower = Object.FindAnyObjectByType<FloorBasketFollower>();
            simCamera = GameObject.Find("SimulationCamera");
            yield return null;
        }

        Assert.IsNotNull(follower, "The scene has no FloorBasketFollower");
        Assert.IsNotNull(simCamera, "XR Simulation is not running: no SimulationCamera");

        // XR Simulation only rescans the room when the camera moves, so sweep it across the floor.
        start = Time.time;
        float yaw = simCamera.transform.eulerAngles.y;
        while (!follower.hasPlaneFloor && Time.time - start < 30f)
        {
            simCamera.transform.rotation = Quaternion.Euler(40f, yaw + 25f * Mathf.Sin(Time.time - start), 0f);
            yield return null;
        }

        Assert.IsTrue(follower.hasPlaneFloor, "No plane floor after 30 s");
        simCamera.transform.rotation = Quaternion.Euler(40f, yaw, 0f);
        yield return new WaitForSeconds(2f);

        // The simulated room's floor is at height 0.
        Assert.AreEqual(0f, follower.floorHeight, 0.03f, "floor height");
        Assert.IsTrue(follower.isBasketVisible, "basket shown");
        var basket = follower.basket.transform.position;
        Assert.AreEqual(follower.floorHeight, basket.y, 0.03f, "basket on the floor");

        // Ten meters to the side is outside the room.
        var far = basket + new Vector3(10f, 0f, 0f);
        Assert.IsFalse(follower.IsOnDetectedFloor(far), "a point 10 m away counts as floor");
        var clamped = follower.ClampToFloor(far);
        Assert.Less(Vector3.Distance(clamped, basket), 6f, "the far point was not pulled back toward the room");
        Assert.Less(Vector3.Distance(follower.ClampToFloor(clamped), clamped), 0.01f, "a clamped point moves again when clamped again");
    }
}
