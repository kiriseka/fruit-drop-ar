using UnityEngine;

/// <summary>
/// Ring on the floor under a falling fruit, so the player can see where it will land without looking up.
/// It is a child of the fruit and stays flat on the floor however high the fruit is.
/// The ring starts wide and closes in as the fruit comes down, so its size shows how long is left. When the
/// fruit arrives the ring matches the basket's catch radius: a basket whose centre is inside it catches the fruit.
/// Under fruit to avoid the ring is red.
/// </summary>
public class LandingMarker : MonoBehaviour
{
    // Just above the basket's shadow (2 mm) so the two never flicker against each other.
    const float k_HeightAboveFloor = 0.004f;

    static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

    [SerializeField]
    [Tooltip("Size of the ring when the fruit starts to fall, compared with its size when the fruit lands.")]
    float m_StartScale = 2f;

    [SerializeField]
    [Tooltip("Colour of the ring under fruit to avoid.")]
    Color m_HazardColor = new Color(0.90f, 0.22f, 0.21f, 1f);

    Fruit m_Fruit;
    Renderer m_Renderer;
    MaterialPropertyBlock m_Block;
    Vector3 m_LandedScale;
    FruitType m_ShownType;

    void Awake()
    {
        m_Fruit = GetComponentInParent<Fruit>();
        m_Renderer = GetComponent<Renderer>();
        m_LandedScale = transform.localScale;
    }

    void LateUpdate()
    {
        if (m_Fruit == null || m_Fruit.floor == null)
            return;

        var position = m_Fruit.transform.position;
        transform.SetPositionAndRotation(
            new Vector3(position.x, m_Fruit.floor.floorHeight + k_HeightAboveFloor, position.z),
            Quaternion.Euler(90f, 0f, 0f));
        transform.localScale = m_LandedScale * Mathf.Lerp(m_StartScale, 1f, m_Fruit.fallProgress);

        if (m_Fruit.type != m_ShownType)
            ShowType(m_Fruit.type);
    }

    // The colour goes through a property block, so every ring keeps sharing the one material.
    void ShowType(FruitType fruitType)
    {
        m_ShownType = fruitType;
        if (m_Renderer == null)
            return;

        if (fruitType != null && fruitType.isHazard)
        {
            m_Block ??= new MaterialPropertyBlock();
            m_Block.SetColor(k_BaseColor, m_HazardColor);
            m_Renderer.SetPropertyBlock(m_Block);
        }
        else
        {
            m_Renderer.SetPropertyBlock(null);
        }
    }
}
