using UnityEngine;

/// <summary>
/// Marker component on every placed furniture object.
/// Stores the yOffset from the original FurnitureItem
/// so FurnitureInteraction can use the correct height when moving,
/// and a reference to the source item so the hold-preview popup
/// can show its 3D model and name.
/// </summary>
public class PlacedFurniture : MonoBehaviour
{
    [HideInInspector]
    public float yOffset = 0.05f;

    [HideInInspector]
    public FurnitureItem sourceItem;   // set at placement time by FurnitureDragController
}
