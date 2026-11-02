using UnityEngine;

/// <summary>
/// Ring on the floor under a falling fruit, so the player can see where it will land without looking up.
/// It is a child of the fruit and stays flat on the floor however high the fruit is.
/// Its radius matches the basket's catch radius: a basket whose centre is inside the ring catches the fruit.
/// </summary>
public class LandingMarker : MonoBehaviour
{
    // Just above the basket's shadow (2 mm) so the two never flicker against each other.
    const float k_HeightAboveFloor = 0.004f;

    Fruit m_Fruit;

    void Awake()
    {
        m_Fruit = GetComponentInParent<Fruit>();
    }

    void LateUpdate()
    {
        if (m_Fruit == null || m_Fruit.floor == null)
            return;

        var position = m_Fruit.transform.position;
        transform.SetPositionAndRotation(
            new Vector3(position.x, m_Fruit.floor.floorHeight + k_HeightAboveFloor, position.z),
            Quaternion.Euler(90f, 0f, 0f));
    }
}
