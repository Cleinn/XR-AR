using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A popup card that shows a small, rotating 3D preview of a placed furniture
/// piece plus its name. Triggered by FurnitureInteraction when a piece is held
/// still for a couple of seconds.
///
/// HOW THE 3D PREVIEW WORKS (no layers or scene setup needed):
///   • A small "rig" (model + camera + lights) is parked far below the world
///     (y = -9000), well beyond the AR camera's far clip, so the AR view never
///     sees it and its lights never touch the real scene.
///   • A dedicated camera renders that rig into a RenderTexture.
///   • The RenderTexture is shown on a RawImage inside the card.
///   • The model spins on its Y axis every frame (a turntable preview).
///
/// SETUP: just have this component in the AR scene (FurnitureInteraction will
/// auto-create one if you don't add it yourself). No manual wiring required.
/// </summary>
public class FurniturePreviewPopup : MonoBehaviour
{
    [Header("Preview")]
    public float rotationSpeed   = 45f;     // degrees/sec turntable spin
    public int   renderSize      = 512;     // RenderTexture resolution
    public float modelTargetSize = 1.0f;    // model is scaled so its largest side = this
    public float cameraFov       = 30f;
    public float framePadding    = 1.18f;   // >1 leaves margin around the object
    public float cameraElevation = 0.40f;   // how high the camera sits relative to the object
    public float lightIntensity  = 1.5f;

    [Header("Card")]
    public Vector2 cardSize  = new Vector2(460, 600);
    public Vector2 cardAnchor = new Vector2(0.5f, 0.78f);

    [Header("Palette")]
    public Color cardColor   = new Color32(0x14, 0x20, 0x27, 0xF2);
    public Color previewBg   = new Color32(0x0C, 0x14, 0x18, 0xFF);
    public Color accent      = new Color32(0x2F, 0xE6, 0xA0, 0xFF);
    public Color textColor   = new Color32(0xE9, 0xF1, 0xED, 0xFF);
    public Color muted       = new Color32(0x8F, 0xA3, 0x9B, 0xFF);

    [Header("Data")]
    [Tooltip("FurnitureDatabase asset name in a Resources folder. Used to look up the display name.")]
    public string databaseResourceName = "FurnitureDatabase";
    private FurnitureDatabase database;

    // UI
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform card;
    private RawImage previewImage;
    private TMP_Text nameLabel;

    // 3D rig
    private Transform rig;       // static root, parked far away
    private Transform pivot;     // rotates; model lives under here
    private Camera previewCam;
    private RenderTexture rt;
    private GameObject currentModel;

    private bool isVisible = false;
    private Coroutine anim;

    private void Awake()
    {
        if (!string.IsNullOrEmpty(databaseResourceName))
            database = Resources.Load<FurnitureDatabase>(databaseResourceName);

        BuildRig();
        BuildUI();
        group.alpha = 0f;
        card.localScale = Vector3.one * 0.85f;
        canvas.gameObject.SetActive(false);
        previewCam.enabled = false;
    }

    private void Update()
    {
        if (isVisible && pivot != null)
            pivot.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);
    }

    // ─────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────

    public void Show(PlacedFurniture pf)
    {
        if (pf == null) return;

        FurnitureItem item = ResolveItem(pf);
        string label = (item != null && !string.IsNullOrEmpty(item.furnitureName))
            ? item.furnitureName               // the name from the FurnitureDatabase
            : CleanName(pf.gameObject.name);    // last-resort fallback

        GameObject source = (item != null && item.prefab != null) ? item.prefab : pf.gameObject;

        SpawnModel(source);
        nameLabel.text = label;

        canvas.gameObject.SetActive(true);
        previewCam.enabled = true;
        isVisible = true;

        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(Animate(true));
    }

    /// <summary>
    /// Figures out which FurnitureItem a placed object came from. Prefers the
    /// direct reference (sourceItem); if that's missing, matches the object's
    /// prefab against the FurnitureDatabase so the display name is still correct.
    /// </summary>
    private FurnitureItem ResolveItem(PlacedFurniture pf)
    {
        if (pf.sourceItem != null) return pf.sourceItem;
        if (database == null || database.items == null) return null;

        string clean = CleanName(pf.gameObject.name);

        // Exact prefab-name match first.
        foreach (FurnitureItem it in database.items)
            if (it != null && it.prefab != null && it.prefab.name == clean)
                return it;

        // Looser match (handles small naming differences).
        foreach (FurnitureItem it in database.items)
            if (it != null && it.prefab != null &&
                (clean.StartsWith(it.prefab.name) || it.prefab.name.StartsWith(clean)))
                return it;

        return null;
    }

    public void Hide()
    {
        if (!isVisible) return;
        isVisible = false;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(Animate(false));
    }

    // ─────────────────────────────────────────────
    //  MODEL HANDLING
    // ─────────────────────────────────────────────

    private void SpawnModel(GameObject source)
    {
        if (currentModel != null) Destroy(currentModel);

        currentModel = Instantiate(source, pivot);
        currentModel.transform.localPosition = Vector3.zero;
        currentModel.transform.localRotation = Quaternion.identity;
        currentModel.transform.localScale    = Vector3.one;
        currentModel.name = "PreviewModel";

        // Strip anything interactive/physical from the copy — it's purely visual.
        foreach (Collider c in currentModel.GetComponentsInChildren<Collider>(true)) Destroy(c);
        PlacedFurniture pf = currentModel.GetComponent<PlacedFurniture>();
        if (pf != null) Destroy(pf);
        foreach (Rigidbody rb in currentModel.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);

        // Measure at identity rotation so framing stays stable while it spins.
        pivot.localRotation = Quaternion.identity;

        // Scale so the largest dimension == modelTargetSize.
        Bounds b = GetWorldBounds(currentModel);
        float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (maxDim < 1e-4f) maxDim = 1f;
        currentModel.transform.localScale = Vector3.one * (modelTargetSize / maxDim);

        // Centre the model exactly on the pivot.
        b = GetWorldBounds(currentModel);
        currentModel.transform.position += pivot.position - b.center;

        // Frame the camera to the bounding sphere so the object is centred,
        // evenly padded, and never clips at any rotation.
        b = GetWorldBounds(currentModel);
        FrameCamera(b.extents.magnitude);

        pivot.localRotation = Quaternion.Euler(0f, 150f, 0f); // pleasant starting angle
    }

    private void FrameCamera(float radius)
    {
        if (radius < 1e-4f) radius = 0.5f;
        float halfFov = cameraFov * 0.5f * Mathf.Deg2Rad;
        float dist = radius / Mathf.Sin(halfFov) * framePadding;
        Vector3 dir = new Vector3(0f, cameraElevation, -1f).normalized; // front, slightly raised
        previewCam.transform.position = pivot.position + dir * dist;
        previewCam.transform.LookAt(pivot.position);
    }

    private Bounds GetWorldBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    private string CleanName(string n)
    {
        if (string.IsNullOrEmpty(n)) return "Furniture";
        return n.Replace("(Clone)", "").Trim();
    }

    // ─────────────────────────────────────────────
    //  ANIMATION
    // ─────────────────────────────────────────────

    private IEnumerator Animate(bool show)
    {
        float dur = 0.22f, t = 0f;
        float aFrom = group.alpha, aTo = show ? 1f : 0f;
        float sFrom = card.localScale.x, sTo = show ? 1f : 0.85f;
        group.blocksRaycasts = show;   // only the X button has a raycast target
        group.interactable   = show;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f); // ease-out cubic
            group.alpha     = Mathf.Lerp(aFrom, aTo, k);
            card.localScale = Vector3.one * Mathf.Lerp(sFrom, sTo, k);
            yield return null;
        }
        group.alpha = aTo;
        card.localScale = Vector3.one * sTo;

        if (!show)
        {
            canvas.gameObject.SetActive(false);
            previewCam.enabled = false;
            if (currentModel != null) { Destroy(currentModel); currentModel = null; }
        }
    }

    // ─────────────────────────────────────────────
    //  RIG (model + camera + lights, parked far away)
    // ─────────────────────────────────────────────

    private void BuildRig()
    {
        Vector3 far = new Vector3(0f, -9000f, 0f);

        rig = new GameObject("PreviewRig").transform;
        rig.position = far;

        pivot = new GameObject("PreviewPivot").transform;
        pivot.SetParent(rig, false);

        // Camera
        GameObject camGO = new GameObject("PreviewCamera");
        camGO.transform.SetParent(rig, false);
        previewCam = camGO.AddComponent<Camera>();
        previewCam.clearFlags      = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = previewBg;
        previewCam.fieldOfView     = cameraFov;
        previewCam.nearClipPlane   = 0.01f;
        previewCam.farClipPlane    = 100f;
        // Camera position/aim is set per-model in FrameCamera().

        rt = new RenderTexture(renderSize, renderSize, 16, RenderTextureFormat.ARGB32);
        rt.Create();
        previewCam.targetTexture = rt;

        // Key + fill + rim lights (point lights, short range — they can't reach the AR scene)
        AddLight(new Vector3( 1.2f,  1.6f, -1.4f), lightIntensity);
        AddLight(new Vector3(-1.4f,  0.6f, -0.8f), lightIntensity * 0.5f);
        AddLight(new Vector3( 0f,    0.8f,  1.6f), lightIntensity * 0.6f);
    }

    private void AddLight(Vector3 localOffset, float intensity)
    {
        GameObject lgo = new GameObject("PreviewLight");
        lgo.transform.SetParent(rig, false);
        lgo.transform.localPosition = localOffset;
        Light l = lgo.AddComponent<Light>();
        l.type      = LightType.Point;
        l.color     = Color.white;
        l.intensity = intensity;
        l.range     = 12f;
    }

    // ─────────────────────────────────────────────
    //  UI CARD
    // ─────────────────────────────────────────────

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("PreviewCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above the AR HUD

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 2400);
        scaler.matchWidthOrHeight = 0.5f;

        group = canvasGO.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // Card
        Image cardImg = MakeImage("Card", canvasGO.transform, cardColor);
        card = cardImg.rectTransform;
        card.anchorMin = card.anchorMax = cardAnchor;
        card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = cardSize;

        // Accent header bar
        Image bar = MakeImage("HeaderBar", card, accent);
        PlaceTop(bar.rectTransform, 6f);

        // Caption
        TMP_Text caption = MakeText("Caption", card, "\u25CF  HELD ITEM", 24, accent);
        Place(caption.rectTransform, new Vector2(0.5f, 0.93f), new Vector2(cardSize.x - 160, 40));
        caption.characterSpacing = 6f;

        // 3D preview surface — pushed down to leave a clear gap under the caption.
        Image frame = MakeImage("PreviewFrame", card, previewBg);
        Place(frame.rectTransform, new Vector2(0.5f, 0.53f), new Vector2(cardSize.x - 80, cardSize.x - 80));

        previewImage = new GameObject("Preview", typeof(RectTransform), typeof(RawImage))
            .GetComponent<RawImage>();
        previewImage.transform.SetParent(frame.transform, false);
        previewImage.texture = rt;
        previewImage.raycastTarget = false;
        RectTransform pir = previewImage.rectTransform;
        pir.anchorMin = Vector2.zero; pir.anchorMax = Vector2.one;
        pir.offsetMin = pir.offsetMax = Vector2.zero;

        // Name
        nameLabel = MakeText("Name", card, "Furniture", 44, textColor);
        nameLabel.fontStyle = FontStyles.Bold;
        Place(nameLabel.rectTransform, new Vector2(0.5f, 0.095f), new Vector2(cardSize.x - 60, 80));

        // Close (X) button — the only way to dismiss the card.
        BuildCloseButton(card);
    }

    private void BuildCloseButton(RectTransform parent)
    {
        GameObject go = new GameObject("CloseButton",
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f);     // invisible, but still a tap target
        img.raycastTarget = true;                  // the one interactive element on the card

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -10f);
        rt.sizeDelta = new Vector2(60f, 60f);

        Button btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(Hide);

        // Draw the "X" from two crossed bars so it never depends on a font glyph.
        MakeXBar(go.transform,  45f);
        MakeXBar(go.transform, -45f);
    }

    private void MakeXBar(Transform parent, float angle)
    {
        Image bar = MakeImage("XBar", parent, textColor);
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(34f, 5f);
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private Image MakeImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private TMP_Text MakeText(string name, Transform parent, string content, float size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = content; t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    private void Place(RectTransform rt, Vector2 anchor, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    private void PlaceTop(RectTransform rt, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, height);
    }

    private void OnDestroy()
    {
        if (rt != null) { rt.Release(); Destroy(rt); }
    }
}
