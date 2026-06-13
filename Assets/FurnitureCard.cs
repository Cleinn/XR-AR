using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to each FurnitureCard.
/// On mobile: finger touches card → immediately notifies FurnitureDragController.
/// The controller waits for finger movement to confirm drag intent.
/// Horizontal movement = scroll tray. Any movement = drag out to place.
/// </summary>
public class FurnitureCard : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [HideInInspector] public FurnitureItem item;
    [HideInInspector] public FurnitureDragController dragController;
    [HideInInspector] public FurnitureUIManager uiManager;

    public void OnPointerDown(PointerEventData eventData)
    {
        // Immediately tell drag controller finger is down on this card
        dragController.OnCardTouchDown(item, eventData.position);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // Release is handled by FurnitureDragController.Update() via raw input
    }
}
