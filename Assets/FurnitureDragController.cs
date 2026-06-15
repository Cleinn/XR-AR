using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class FurnitureDragController : MonoBehaviour
{
    [Header("AR")]
    public ARRaycastManager arRaycastManager;

    [Header("Ghost UI")]
    public RectTransform dragGhostRect;
    public UnityEngine.UI.Image dragGhostImage;

    [Header("Drag Settings")]
    public float dragThreshold = 10f;

    private FurnitureItem activeItem = null;
    private bool isDragging  = false;
    private bool isTracking  = false;
    private Vector2 touchStartPos;
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();

    private void OnEnable()  { EnhancedTouchSupport.Enable(); }
    private void OnDisable() { EnhancedTouchSupport.Disable(); }

    public void OnCardTouchDown(FurnitureItem item, Vector2 screenPos)
    {
        activeItem    = item;
        isTracking    = true;
        isDragging    = false;
        touchStartPos = screenPos;
    }

    private void Update()
    {
        if (!isTracking && !isDragging) return;

        Vector2 currentPos = GetPointerPosition();
        bool    released   = PointerReleased();

        if (isDragging)
        {
            if (dragGhostRect != null)
                dragGhostRect.position = currentPos;

            if (released)
            {
                isDragging = false;
                isTracking = false;
                if (dragGhostRect != null)
                    dragGhostRect.gameObject.SetActive(false);
                TryPlace(currentPos);
            }
        }
        else if (isTracking)
        {
            if (Vector2.Distance(currentPos, touchStartPos) > dragThreshold)
            {
                isDragging = true;
                ShowGhost(currentPos);
            }

            if (released)
            {
                isTracking = false;
                activeItem = null;
            }
        }
    }

    private void ShowGhost(Vector2 screenPos)
    {
        if (dragGhostImage == null || dragGhostRect == null) return;

        if (activeItem != null && activeItem.icon != null)
        {
            dragGhostImage.sprite = activeItem.icon;
            dragGhostImage.color  = Color.white;
        }
        else
        {
            dragGhostImage.color = new Color(0, 0, 0, 0);
        }

        dragGhostRect.position = screenPos;
        dragGhostRect.gameObject.SetActive(true);
    }

    private void TryPlace(Vector2 screenPos)
    {
        try
        {
            if (activeItem == null) return;
            if (arRaycastManager == null) return;

            // Don't place if releasing over a UI element
            if (IsPointerOverUI(screenPos))
            {
                activeItem = null;
                return;
            }

            if (!arRaycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
            {
                activeItem = null;
                return;
            }

            // Check if the hit plane passes the size filter
            TrackableId hitPlaneId = hits[0].trackableId;
            if (!ARPlaneToggle.ValidPlaneIds.Contains(hitPlaneId))
            {
                Debug.Log("[DragController] Blocked — plane too small or invalid.");
                activeItem = null;
                return;
            }

            Pose       hitPose  = hits[0].pose;
            Vector3    spawnPos = hitPose.position + new Vector3(0, activeItem.yOffset, 0);

            // Keep furniture upright. Use only the yaw (Y) from the plane pose,
            // not its full orientation, so pieces never tip onto their side.
            float      yaw       = hitPose.rotation.eulerAngles.y;
            Quaternion uprightRot = Quaternion.Euler(0f, yaw, 0f);
            GameObject placed   = Instantiate(activeItem.prefab, spawnPos, uprightRot);

            if (placed == null) return;

            // Store yOffset on PlacedFurniture so FurnitureInteraction
            // can use the correct height when moving the object
            PlacedFurniture pf = placed.GetComponent<PlacedFurniture>();
            if (pf == null) pf = placed.AddComponent<PlacedFurniture>();
            pf.yOffset = activeItem.yOffset;

            // Add colliders to all child meshes
            MeshFilter[] meshFilters = placed.GetComponentsInChildren<MeshFilter>(true);
            int collidersAdded = 0;

            foreach (MeshFilter mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (mf.GetComponent<Collider>() != null) continue;

                try
                {
                    MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh   = mf.sharedMesh;
                    mc.convex       = true;
                    collidersAdded++;
                }
                catch
                {
                    mf.gameObject.AddComponent<BoxCollider>();
                    collidersAdded++;
                }
            }

            if (meshFilters.Length == 0 &&
                placed.GetComponentInChildren<Collider>() == null)
            {
                placed.AddComponent<BoxCollider>();
                collidersAdded++;
            }

            Debug.Log("[DragController] Placed: " + placed.name
                + " | yOffset: " + activeItem.yOffset
                + " | Colliders: " + collidersAdded);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[DragController] Exception: " + e.Message);
        }
        finally
        {
            activeItem = null;
        }
    }

    private Vector2 GetPointerPosition()
    {
        var touches = Touch.activeTouches;
        if (touches.Count > 0) return touches[0].screenPosition;
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
        return Vector2.zero;
    }

    private bool PointerReleased()
    {
        var touches = Touch.activeTouches;
        if (touches.Count > 0)
        {
            var phase = touches[0].phase;
            return phase == UnityEngine.InputSystem.TouchPhase.Ended
                || phase == UnityEngine.InputSystem.TouchPhase.Canceled;
        }
        if (Mouse.current != null) return Mouse.current.leftButton.wasReleasedThisFrame;
        return false;
    }

    private bool IsPointerOverUI(Vector2 screenPos)
    {
        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = screenPos
        };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    public bool IsDragging => isDragging;
}
