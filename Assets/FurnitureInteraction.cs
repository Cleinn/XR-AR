using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class FurnitureInteraction : MonoBehaviour
{
    [Header("AR")]
    public ARRaycastManager arRaycastManager;

    [Header("Indicators — assign from Hierarchy")]
    public GameObject selectionIndicator;
    public GameObject placementIndicator;

    [Header("Delete Button")]
    public GameObject deleteButton;
    public GameObject deleteAllButton;

    [Header("Bottom Bar")]
    public GameObject bottomBar;

    [Header("Indicator Colors")]
    public Color validColor   = new Color(0f,  1f,  0.4f, 0.7f);
    public Color invalidColor = new Color(1f,  0.2f, 0.2f, 0.7f);
    public Color selectColor  = new Color(1f,  1f,  1f,   0.7f);

    [Header("Settings")]
    public float rotationSensitivity = 1.0f;
    [Tooltip("Pixels finger must move before object starts moving. Prevents micro movement on tap.")]
    public float movementThreshold = 15f;

    private Material selectionMat;
    private Material placementMat;

    private GameObject selectedObject;
    private PlacedFurniture selectedFurniture;
    private bool isMoving    = false;
    private bool isRotating  = false;
    private bool movementConfirmed = false; // true only after finger moves past threshold
    private float previousTouchAngle = 0f;
    private Vector2 touchBeganPos;

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();

    private void OnEnable()  { EnhancedTouchSupport.Enable(); }
    private void OnDisable() { EnhancedTouchSupport.Disable(); }

    private void Start()
    {
        if (arRaycastManager == null)
            arRaycastManager = GetComponent<ARRaycastManager>();

        if (selectionIndicator != null)
        {
            selectionMat = selectionIndicator.GetComponent<Renderer>()?.material;
            if (selectionMat != null) selectionMat.color = selectColor;
            selectionIndicator.SetActive(false);
        }

        if (placementIndicator != null)
        {
            placementMat = placementIndicator.GetComponent<Renderer>()?.material;
            if (placementMat != null) placementMat.color = validColor;
            placementIndicator.SetActive(false);
        }

        // Wire delete button
        if (deleteButton != null)
        {
            deleteButton.SetActive(false);
            UnityEngine.UI.Button btn = deleteButton.GetComponent<UnityEngine.UI.Button>();
            if (btn != null) btn.onClick.AddListener(DeleteSelected);
        }

        // Wire delete all button
        if (deleteAllButton != null)
        {
            deleteAllButton.SetActive(false);
            UnityEngine.UI.Button btnAll = deleteAllButton.GetComponent<UnityEngine.UI.Button>();
            if (btnAll != null) btnAll.onClick.AddListener(DeleteAll);
        }
    }

    private void Update()
    {
        int touchCount = Touch.activeTouches.Count;

        if (touchCount >= 2 && selectedObject != null)
        {
            isMoving = false;
            HidePlacement();
            HandleTwoFingerRotation();
            return;
        }

        isRotating = false;

        if (touchCount == 1)
        {
            var t = Touch.activeTouches[0];
            switch (t.phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    HandleBegan(t.screenPosition); break;
                case UnityEngine.InputSystem.TouchPhase.Moved:
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    HandleDrag(t.screenPosition); break;
                case UnityEngine.InputSystem.TouchPhase.Ended:
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    HandleEnded(); break;
            }
        }

        if (Mouse.current != null)
        {
            Vector2 mp = Mouse.current.position.ReadValue();
            if      (Mouse.current.leftButton.wasPressedThisFrame)  HandleBegan(mp);
            else if (Mouse.current.leftButton.isPressed)             HandleDrag(mp);
            else if (Mouse.current.leftButton.wasReleasedThisFrame)  HandleEnded();
        }

        UpdateSelectionIndicator();
    }

    private void HandleBegan(Vector2 screenPos)
    {
        // Block interaction if tapping on any UI element
        if (IsPointerOverUI(screenPos)) return;

        if (Camera.main == null) return;

        Ray          ray     = Camera.main.ScreenPointToRay(screenPos);
        RaycastHit[] allHits = Physics.RaycastAll(ray, 50f);
        System.Array.Sort(allHits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in allHits)
        {
            if (hit.collider.GetComponent<ARPlane>() != null)         continue;
            if (hit.collider.GetComponentInParent<ARPlane>() != null) continue;

            if (selectionIndicator != null &&
                hit.collider.gameObject == selectionIndicator)        continue;
            if (placementIndicator != null &&
                hit.collider.gameObject == placementIndicator)        continue;

            PlacedFurniture pf = hit.collider.GetComponentInParent<PlacedFurniture>();
            if (pf != null)
            {
                Select(pf);
                isMoving           = true;
                movementConfirmed  = false;
                touchBeganPos      = screenPos;
                return;
            }
        }

        Deselect();
    }

    // Returns true if the screen position is over any UI element
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

    private void HandleDrag(Vector2 screenPos)
    {
        if (selectedObject == null || !isMoving) return;

        // Only start moving after finger passes the threshold
        // This prevents micro movement on simple taps
        if (!movementConfirmed)
        {
            if (Vector2.Distance(screenPos, touchBeganPos) < movementThreshold)
                return;

            movementConfirmed = true;
        }

        // Hide selection ring while moving
        if (selectionIndicator != null) selectionIndicator.SetActive(false);

        if (arRaycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
        {
            Vector3 pos  = hits[0].pose.position;

            // Use stored yOffset from PlacedFurniture — prevents floating
            float yOff = selectedFurniture != null
                ? selectedFurniture.yOffset
                : 0.05f;

            selectedObject.transform.position = pos + Vector3.up * yOff;

            if (placementIndicator != null)
            {
                placementIndicator.SetActive(true);
                placementIndicator.transform.position = pos + Vector3.up * 0.002f;
                if (placementMat != null) placementMat.color = validColor;
            }
        }
        else
        {
            if (placementIndicator != null && placementIndicator.activeSelf)
                if (placementMat != null) placementMat.color = invalidColor;
        }
    }

    private void HandleEnded()
    {
        isMoving          = false;
        movementConfirmed = false;
        HidePlacement();

        // Restore selection ring after move ends
        if (selectedObject != null && selectionIndicator != null)
            selectionIndicator.SetActive(true);
    }

    private void HandleTwoFingerRotation()
    {
        if (selectedObject == null) return;

        Vector2 a     = Touch.activeTouches[0].screenPosition;
        Vector2 b     = Touch.activeTouches[1].screenPosition;
        float   angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;

        if (!isRotating)
        {
            isRotating         = true;
            previousTouchAngle = angle;
        }
        else
        {
            float delta = angle - previousTouchAngle;
            selectedObject.transform.Rotate(0f, -delta * rotationSensitivity, 0f, Space.World);
            previousTouchAngle = angle;
        }
    }

    private void Select(PlacedFurniture pf)
    {
        selectedObject    = pf.gameObject;
        selectedFurniture = pf;

        if (selectionIndicator != null)
            selectionIndicator.SetActive(true);

        // Smoothly swap bottom bar for delete button
        StartCoroutine(ShowDeleteBar());
    }

    private void Deselect()
    {
        selectedObject    = null;
        selectedFurniture = null;
        isMoving          = false;
        isRotating        = false;
        if (selectionIndicator != null) selectionIndicator.SetActive(false);
        HidePlacement();

        // Smoothly swap delete button back to bottom bar
        StartCoroutine(ShowBottomBar());
    }

    public void DeleteSelected()
    {
        if (selectedObject == null) return;
        Destroy(selectedObject);
        Deselect();
    }

    public void DeleteAll()
    {
        // Find and destroy all placed furniture in the scene
        PlacedFurniture[] allFurniture =
            FindObjectsByType<PlacedFurniture>(FindObjectsSortMode.None);

        foreach (PlacedFurniture pf in allFurniture)
            Destroy(pf.gameObject);

        Deselect();
    }

    // ─────────────────────────────────────────────
    // Bar transition coroutines
    // ─────────────────────────────────────────────

    private IEnumerator ShowDeleteBar()
    {
        if (bottomBar != null) yield return StartCoroutine(SlideBar(bottomBar, false));

        if (deleteButton != null)     deleteButton.SetActive(true);
        if (deleteAllButton != null)  deleteAllButton.SetActive(true);

        if (deleteButton != null)     StartCoroutine(SlideBar(deleteButton, true));
        if (deleteAllButton != null)  StartCoroutine(SlideBar(deleteAllButton, true));

        yield return new WaitForSeconds(0.2f);
    }

    private IEnumerator ShowBottomBar()
    {
        if (deleteButton != null)     StartCoroutine(SlideBar(deleteButton, false));
        if (deleteAllButton != null)  StartCoroutine(SlideBar(deleteAllButton, false));

        yield return new WaitForSeconds(0.2f);

        if (bottomBar != null)
        {
            bottomBar.SetActive(true);
            yield return StartCoroutine(SlideBar(bottomBar, true));
        }
    }

    private IEnumerator SlideBar(GameObject bar, bool show)
    {
        CanvasGroup cg = bar.GetComponent<CanvasGroup>();
        if (cg == null) cg = bar.AddComponent<CanvasGroup>();

        float duration   = 0.2f;
        float elapsed    = 0f;
        float startAlpha = show ? 0f : 1f;
        float endAlpha   = show ? 1f : 0f;

        cg.alpha = startAlpha;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t  = elapsed / duration;
            float ease = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic
            cg.alpha = Mathf.Lerp(startAlpha, endAlpha, ease);
            yield return null;
        }

        cg.alpha = endAlpha;
        if (!show) bar.SetActive(false);
    }

    private void HidePlacement()
    {
        if (placementIndicator != null) placementIndicator.SetActive(false);
    }

    private void UpdateSelectionIndicator()
    {
        if (selectedObject     == null) return;
        if (selectionIndicator == null) return;
        if (!selectionIndicator.activeSelf) return;

        // Position ring at base of object using stored yOffset
        float yOff = selectedFurniture != null
            ? -selectedFurniture.yOffset + 0.002f
            : 0.002f;

        selectionIndicator.transform.position =
            selectedObject.transform.position + Vector3.up * yOff;
    }
}
