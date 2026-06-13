using UnityEngine;

/// <summary>
/// Marker component on every placed furniture object.
/// Stores the yOffset from the original FurnitureItem
/// so FurnitureInteraction can use the correct height when moving.
/// </summary>
public class PlacedFurniture : MonoBehaviour
{
    [HideInInspector]
    public float yOffset = 0.05f;
}
