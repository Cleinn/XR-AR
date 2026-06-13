using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.InputSystem.EnhancedTouch;

// Placement is now fully handled by FurnitureDragController.
// This file is kept so nothing breaks if referenced elsewhere.
[RequireComponent(typeof(ARRaycastManager))]
public class PlaceFurniture : MonoBehaviour
{
    private void OnEnable()  { EnhancedTouchSupport.Enable(); }
    private void OnDisable() { EnhancedTouchSupport.Disable(); }

    public void SetSelectedItem(FurnitureItem item) { }
    public void TryPlaceAtPosition(Vector2 pos) { }
    public void TryPlaceAtCurrentTouch() { }
    public void SuppressNextTap() { }
}
