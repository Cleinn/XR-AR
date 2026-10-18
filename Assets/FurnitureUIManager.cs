using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem.EnhancedTouch;

public class FurnitureUIManager : MonoBehaviour
{
    [Header("Data")]
    public FurnitureDatabase database;

    [Header("Scene References")]
    public FurnitureDragController dragController;
    public ScreenshotManager screenshotManager;

    [Header("Bottom Bar")]
    public Button trayToggleButton;
    public Button cameraButton;

    [Header("Tray Panel")]
    public GameObject trayPanel;
    public Button closeButton;
    public Transform trayContent;

    [Header("Card Prefab")]
    public GameObject furnitureCardPrefab;

    [Header("New Bottom Bar")]
    [Tooltip("Replace the old bottom bar with the new floating green bar (created automatically).")]
    public bool useNewBottomBar = true;
    public ARBottomBar newBottomBar;

    [Header("Catalog Sheet (new bottom-sheet UI)")]
    [Tooltip("Use the bottom-sheet catalog instead of the old tray panel. " +
             "It is created automatically at runtime if no CatalogSheet is assigned.")]
    public bool useCatalogSheet = true;
    public CatalogSheet catalogSheet;

    [Header("Card Colors")]
    public Color selectedCardColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    public Color defaultCardColor  = new Color(0.93f, 0.93f, 0.93f, 1f);

    private bool isPanelOpen = false;

    private bool UsingCatalog => useCatalogSheet && catalogSheet != null;
    private bool IsPanelOpen  => UsingCatalog ? catalogSheet.IsOpen : isPanelOpen;

    private void OnEnable()  { EnhancedTouchSupport.Enable(); }
    private void OnDisable() { EnhancedTouchSupport.Disable(); }

    private void Start()
    {
        if (useCatalogSheet)
        {
            if (catalogSheet == null)
                catalogSheet = new GameObject("CatalogSheet").AddComponent<CatalogSheet>();
            catalogSheet.Init(database, dragController);

            // The old tray is no longer used
            if (trayPanel != null) trayPanel.SetActive(false);
        }
        else
        {
            trayPanel.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(CloseTrayPanel);
            BuildTray();
        }

        trayToggleButton.onClick.AddListener(ToggleTrayPanel);
        cameraButton.onClick.AddListener(OnCameraButtonPressed);

        // Swap the old bottom bar for the new one. Runs after the listeners above are wired,
        // because the new bar triggers the old buttons' onClick.
        if (useNewBottomBar)
        {
            ARPlaneToggle planeToggle = FindFirstObjectByType<ARPlaneToggle>();
            FurnitureInteraction interaction = FindFirstObjectByType<FurnitureInteraction>();
            if (newBottomBar == null)
                newBottomBar = new GameObject("ARBottomBar").AddComponent<ARBottomBar>();
            newBottomBar.Init(trayToggleButton, cameraButton,
                planeToggle != null ? planeToggle.planeToggleButton : null, interaction);
        }
    }

    private void Update()
    {
        // Auto-close tray when user starts dragging out a card
        if (dragController.IsDragging && IsPanelOpen)
            CloseTrayPanel();
    }

    // ─────────────────────────────────────────────
    // Tray panel
    // ─────────────────────────────────────────────

    private void ToggleTrayPanel()
    {
        if (IsPanelOpen) CloseTrayPanel();
        else             OpenTrayPanel();
    }

    private void OpenTrayPanel()
    {
        if (UsingCatalog) { catalogSheet.Open(); return; }
        trayPanel.SetActive(true);
        isPanelOpen = true;
        trayPanel.transform.localScale = new Vector3(1f, 0f, 1f);
        StartCoroutine(AnimatePanel(Vector3.one, 0.2f));
    }

    public void CloseTrayPanel()
    {
        if (UsingCatalog) { catalogSheet.Close(); return; }
        if (!isPanelOpen) return;
        isPanelOpen = false;
        StartCoroutine(AnimatePanel(new Vector3(1f, 0f, 1f), 0.15f, () =>
        {
            trayPanel.SetActive(false);
        }));
    }

    private System.Collections.IEnumerator AnimatePanel(
        Vector3 target, float duration, System.Action onComplete = null)
    {
        Vector3 start = trayPanel.transform.localScale;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            trayPanel.transform.localScale = Vector3.Lerp(start, target, t / duration);
            yield return null;
        }
        trayPanel.transform.localScale = target;
        onComplete?.Invoke();
    }

    // ─────────────────────────────────────────────
    // Build cards
    // ─────────────────────────────────────────────

    private void BuildTray()
    {
        foreach (Transform child in trayContent)
            Destroy(child.gameObject);

        foreach (FurnitureItem item in database.items)
        {
            GameObject cardGO = Instantiate(furnitureCardPrefab, trayContent);

            // Icon
            Image[] images = cardGO.GetComponentsInChildren<Image>();
            if (images.Length >= 2 && item.icon != null)
                images[1].sprite = item.icon;

            // Label
            TMP_Text label = cardGO.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = item.furnitureName;

            // Background color
            Image cardBg = images[0];
            cardBg.color = defaultCardColor;

            // Wire FurnitureCard
            FurnitureCard card   = cardGO.AddComponent<FurnitureCard>();
            card.item            = item;
            card.dragController  = dragController;
            card.uiManager       = this;
        }
    }

    // ─────────────────────────────────────────────
    // Camera
    // ─────────────────────────────────────────────

    public void OnCameraButtonPressed()
    {
        screenshotManager.TakeScreenshot();
    }
}
