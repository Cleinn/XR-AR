using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Bottom-sheet furniture catalog (the "Aura Lounge Chair" design).
/// Built entirely at runtime, same approach as MainMenuController / FurniturePreviewPopup,
/// so there is no manual UI wiring. FurnitureUIManager creates and drives it.
///
/// Layout (top to bottom): title + close, room/rating/reviews + color dropdown,
/// product preview card, price + Buy Now, scrolling thumbnail strip.
///
/// Thumbnails: tap = select (shows its details), drag up/out = place in AR
/// (hands over to FurnitureDragController), drag sideways = scroll the strip.
/// </summary>
public class CatalogSheet : MonoBehaviour
{
    [Header("Data (filled in by FurnitureUIManager)")]
    public FurnitureDatabase database;
    public FurnitureDragController dragController;

    [Header("Back button")]
    public bool showBackButton = true;
    public string backSceneName = "MainMenu";

    [Header("Palette")]
    public Color sheetColor    = new Color32(0xEB, 0xE7, 0xDD, 0xFF);
    public Color ink           = new Color32(0x1A, 0x1A, 0x1A, 0xFF);
    public Color mutedInk      = new Color32(0x8A, 0x8A, 0x8A, 0xFF);
    public Color pillGray      = new Color32(0xC6, 0xC6, 0xC9, 0xFF);
    public Color green         = new Color32(0x2E, 0x4A, 0x3D, 0xFF);
    public Color starColor     = new Color32(0xD9, 0x82, 0x2B, 0xFF);
    public Color cardColor     = Color.white;
    public Color photoBg       = new Color32(0xEE, 0xEB, 0xE5, 0xFF);
    public Color thumbColor    = new Color32(0xD9, 0xD9, 0xDB, 0xFF);
    public Color thumbSelected = Color.white;

    /// <summary>Raised when "Buy Now" is tapped.</summary>
    public event Action<FurnitureItem> BuyNowPressed;
    /// <summary>Raised when the chevron next to "Buy Now" is tapped.</summary>
    public event Action<FurnitureItem> BuyOptionsPressed;
    /// <summary>Raised when a color is picked from the dropdown (item, color index).</summary>
    public event Action<FurnitureItem, int> ColorChanged;

    public bool IsOpen { get; private set; }

    // ── layout constants (canvas reference pixels, 1080 x 2400) ──
    private const float RefW = 1080f, RefH = 2400f, Match = 0.5f;
    private const float SheetH = 1460f;
    private const float SidePad = 54f;
    private const float ThumbSize = 133f;
    private const float ThumbItemH = 200f;

    // ── runtime refs ──
    private Canvas canvas;
    private RectTransform sheet, backRT, colorPopup, thumbContent, blocker;
    private TMP_Text titleText, metaText, reviewsText, colorLabel, priceText;
    private Image starImage, previewImage;
    private ScrollRect scroll;
    private readonly List<CatalogThumbnail> thumbs = new List<CatalogThumbnail>();
    private FurnitureItem current;
    private int colorIndex;
    private Coroutine anim;

    // ════════════════════════════════════════════════════════════
    //  LIFECYCLE / PUBLIC API
    // ════════════════════════════════════════════════════════════

    private void Awake()
    {
        BuildUI();
        canvas.gameObject.SetActive(false);
    }

    public void Init(FurnitureDatabase db, FurnitureDragController drag)
    {
        if (db != null) database = db;
        if (drag != null) dragController = drag;
        BuildThumbnails();
        if (thumbs.Count > 0) Select(thumbs[0]);

        // Arrived from a product card on the Home screen: show that item right away.
        if (HomeController.PendingItem != null)
        {
            foreach (CatalogThumbnail t in thumbs)
                if (t.item == HomeController.PendingItem) { Select(t); break; }
            HomeController.PendingItem = null;
            Open();
        }
    }

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        canvas.gameObject.SetActive(true);
        ApplyInsets();
        sheet.anchoredPosition = new Vector2(0f, -sheet.sizeDelta.y);
        HideColorPopup();
        if (current == null && thumbs.Count > 0) Select(thumbs[0]);
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(Slide(true));
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        HideColorPopup();
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(Slide(false));
    }

    private IEnumerator Slide(bool open)
    {
        float h    = sheet.sizeDelta.y;
        float from = sheet.anchoredPosition.y;
        float to   = open ? 0f : -h;
        float dur  = open ? 0.30f : 0.20f;
        float t    = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f); // ease-out cubic
            sheet.anchoredPosition = new Vector2(0f, Mathf.Lerp(from, to, k));
            yield return null;
        }
        sheet.anchoredPosition = new Vector2(0f, to);
        if (!open) canvas.gameObject.SetActive(false);
    }

    // ════════════════════════════════════════════════════════════
    //  SELECTION / CONTENT
    // ════════════════════════════════════════════════════════════

    private void BuildThumbnails()
    {
        foreach (CatalogThumbnail t in thumbs) if (t != null) Destroy(t.gameObject);
        thumbs.Clear();
        current = null;
        if (database == null || database.items == null) return;

        foreach (FurnitureItem item in database.items)
        {
            if (item == null) continue;
            thumbs.Add(MakeThumb(item));
        }
    }

    private void Select(CatalogThumbnail th)
    {
        if (th == null || th.item == null) return;
        current = th.item;
        colorIndex = 0;
        foreach (CatalogThumbnail t in thumbs) t.SetSelected(t == th);
        Refresh();
    }

    private void Refresh()
    {
        if (current == null) return;

        titleText.text = string.IsNullOrEmpty(current.furnitureName) ? "Furniture" : current.furnitureName;

        // "Living Room 5 [star] (175 Reviews)"
        string left = current.room != null ? current.room.Trim() : "";
        if (current.rating > 0f)
            left = (left + " " + current.rating.ToString("0.#", CultureInfo.InvariantCulture)).Trim();
        metaText.text = left;
        metaText.gameObject.SetActive(left.Length > 0);
        starImage.gameObject.SetActive(current.rating > 0f);
        reviewsText.text = "(" + current.reviewCount + " Reviews)";
        reviewsText.gameObject.SetActive(current.reviewCount > 0);

        previewImage.sprite  = current.icon;
        previewImage.enabled = current.icon != null;

        if (current.price > 0)
        {
            string s = "Rp. " + FormatRupiah(current.price);
            if (current.originalPrice > current.price)
                s += "   <size=85%><color=#" + ColorUtility.ToHtmlStringRGB(mutedInk) + "><s>Rp. "
                     + FormatRupiah(current.originalPrice) + "</s></color></size>";
            priceText.text = s;
            priceText.gameObject.SetActive(true);
        }
        else priceText.gameObject.SetActive(false);

        RebuildColorPopup();
    }

    private static string FormatRupiah(int value)
    {
        string digits = Mathf.Abs(value).ToString(CultureInfo.InvariantCulture);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append('.');
            sb.Append(digits[i]);
        }
        return (value < 0 ? "-" : "") + sb;
    }

    // ── color dropdown ──

    private void RebuildColorPopup()
    {
        for (int i = colorPopup.childCount - 1; i >= 0; i--)
        {
            GameObject old = colorPopup.GetChild(i).gameObject;
            old.SetActive(false);   // layout ignores inactive children immediately
            Destroy(old);
        }

        string[] cols = current.colors;
        bool has = cols != null && cols.Length > 0;
        colorLabel.text = has ? cols[Mathf.Clamp(colorIndex, 0, cols.Length - 1)] : "Default";
        if (!has) return;

        for (int i = 0; i < cols.Length; i++)
        {
            int idx = i;
            MakePopupRow(cols[i], () => PickColor(idx));
        }
    }

    private void PickColor(int idx)
    {
        colorIndex = idx;
        colorLabel.text = current.colors[idx];
        HideColorPopup();
        if (ColorChanged != null) ColorChanged(current, idx);
    }

    private void ToggleColorPopup()
    {
        if (current == null || current.colors == null || current.colors.Length < 2) return;
        bool show = !colorPopup.gameObject.activeSelf;
        blocker.gameObject.SetActive(show);
        colorPopup.gameObject.SetActive(show);
        if (show)
        {
            blocker.SetAsLastSibling();
            colorPopup.SetAsLastSibling();
        }
    }

    private void HideColorPopup()
    {
        if (colorPopup != null) colorPopup.gameObject.SetActive(false);
        if (blocker != null) blocker.gameObject.SetActive(false);
    }

    // ── buttons ──

    private void OnBuyNow()
    {
        if (current == null) return;
        if (BuyNowPressed != null) BuyNowPressed(current);
        if (!string.IsNullOrEmpty(current.buyUrl)) Application.OpenURL(current.buyUrl);
        else Debug.Log("[CatalogSheet] Buy Now pressed for '" + current.furnitureName + "' (no buyUrl set on the item).");
    }

    private void OnBuyOptions()
    {
        if (current != null && BuyOptionsPressed != null) BuyOptionsPressed(current);
    }

    private void GoBack()
    {
        if (!Application.CanStreamedLevelBeLoaded(backSceneName))
        {
            Debug.LogWarning("[CatalogSheet] Scene '" + backSceneName + "' is not in Build Settings.");
            return;
        }
        SceneManager.LoadScene(backSceneName);
    }

    // ════════════════════════════════════════════════════════════
    //  BUILD UI
    // ════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("CatalogCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;   // above the AR HUD, below the preview popup (200)

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefW, RefH);
        scaler.matchWidthOrHeight = Match;

        Transform root = canvasGO.transform;

        // ── Back arrow (floats over the camera view) ──
        Button back = MakeIconButton(root, "BackButton", new Vector2(120f, 120f), GoBack);
        backRT = back.GetComponent<RectTransform>();
        backRT.anchorMin = backRT.anchorMax = new Vector2(0f, 1f);
        backRT.pivot = new Vector2(0f, 1f);
        MakeBar(back.transform, ink, 58f, 8f, 0f,   Vector2.zero);
        MakeBar(back.transform, ink, 32f, 8f,  45f, new Vector2(-29f + 11f,  11f));
        MakeBar(back.transform, ink, 32f, 8f, -45f, new Vector2(-29f + 11f, -11f));
        back.gameObject.SetActive(showBackButton);

        // ── Sheet ──
        Image sheetImg = MakeRounded(root, "Sheet", sheetColor, 72, true, true, false, false);
        sheetImg.raycastTarget = true;   // swallow taps so they don't reach the AR scene
        sheet = sheetImg.rectTransform;
        sheet.anchorMin = new Vector2(0f, 0f);
        sheet.anchorMax = new Vector2(1f, 0f);
        sheet.pivot     = new Vector2(0.5f, 0f);
        sheet.sizeDelta = new Vector2(0f, SheetH);
        sheet.anchoredPosition = new Vector2(0f, -SheetH);

        // ── Title + close ──
        titleText = MakeText("Title", sheet, "", 58, FontStyles.Bold, ink, TextAlignmentOptions.MidlineLeft);
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        SetTopRow(titleText.rectTransform, 44f, 88f, SidePad, 170f);

        Button close = MakeIconButton(sheet, "CloseButton", new Vector2(84f, 84f), Close);
        RectTransform crt = close.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-SidePad, -24f);
        MakeBar(close.transform, ink, 52f, 8f,  45f, Vector2.zero);
        MakeBar(close.transform, ink, 52f, 8f, -45f, Vector2.zero);

        // ── Meta row: room + rating, star, reviews ──
        RectTransform meta = NewRect("Meta", sheet);
        SetTopRow(meta, 109f, 106f, SidePad, 450f);
        HorizontalLayoutGroup hlg = meta.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        metaText = MakeText("RoomRating", meta, "", 36, FontStyles.Normal, ink, TextAlignmentOptions.MidlineLeft);
        starImage = new GameObject("Star", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
        starImage.transform.SetParent(meta, false);
        starImage.sprite = GetStarSprite();
        starImage.color = starColor;
        starImage.raycastTarget = false;
        LayoutElement sle = starImage.GetComponent<LayoutElement>();
        sle.preferredWidth = sle.preferredHeight = 38f;
        reviewsText = MakeText("Reviews", meta, "", 36, FontStyles.Normal, ink, TextAlignmentOptions.MidlineLeft);

        // ── Color pill (label | chevron) ──
        RectTransform colorPill = NewRect("ColorPill", sheet);
        colorPill.anchorMin = colorPill.anchorMax = colorPill.pivot = new Vector2(1f, 1f);
        colorPill.anchoredPosition = new Vector2(-110f, -109f);
        colorPill.sizeDelta = new Vector2(316f, 106f);
        MakeSplitPill(colorPill, 189f, 122f, pillGray, ink, "Default", 38, FontStyles.Normal,
            ToggleColorPopup, ToggleColorPopup, out colorLabel);

        // ── Preview card ──
        Image card = MakeRounded(sheet, "PreviewCard", cardColor, 36);
        SetTopRow(card.rectTransform, 240f, 737f, 58f, 58f);
        Image photo = MakeRounded(card.transform, "PhotoBg", photoBg, 24);
        Stretch(photo.rectTransform, 42f);
        previewImage = new GameObject("Product", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        previewImage.transform.SetParent(photo.transform, false);
        previewImage.preserveAspect = true;
        previewImage.raycastTarget = false;
        Stretch(previewImage.rectTransform, 16f);

        // ── Price + Buy Now ──
        priceText = MakeText("Price", sheet, "", 40, FontStyles.Bold, ink, TextAlignmentOptions.MidlineLeft);
        priceText.enableAutoSizing = true;
        priceText.fontSizeMin = 24f;
        priceText.fontSizeMax = 40f;
        SetTopRow(priceText.rectTransform, 1067f, 106f, 96f, 48f + 354f + 24f);

        RectTransform buyPill = NewRect("BuyPill", sheet);
        buyPill.anchorMin = buyPill.anchorMax = buyPill.pivot = new Vector2(1f, 1f);
        buyPill.anchoredPosition = new Vector2(-48f, -1067f);
        buyPill.sizeDelta = new Vector2(354f, 106f);
        TMP_Text unused;
        MakeSplitPill(buyPill, 221f, 128f, green, Color.white, "Buy Now", 40, FontStyles.Bold,
            OnBuyNow, OnBuyOptions, out unused);

        // ── Thumbnail strip ──
        BuildStrip();

        // ── Color dropdown (built last so it draws on top) ──
        blocker = NewRect("PopupBlocker", sheet);
        Stretch(blocker, 0f);
        Image bimg = blocker.gameObject.AddComponent<Image>();
        bimg.color = new Color(0f, 0f, 0f, 0f);
        Button bbtn = blocker.gameObject.AddComponent<Button>();
        bbtn.targetGraphic = bimg;
        bbtn.transition = Selectable.Transition.None;
        bbtn.onClick.AddListener(HideColorPopup);
        blocker.gameObject.SetActive(false);

        Image popImg = MakeRounded(sheet, "ColorPopup", Color.white, 36);
        popImg.raycastTarget = true;
        colorPopup = popImg.rectTransform;
        colorPopup.anchorMin = colorPopup.anchorMax = colorPopup.pivot = new Vector2(1f, 1f);
        colorPopup.anchoredPosition = new Vector2(-110f, -225f);
        colorPopup.sizeDelta = new Vector2(316f, 100f);
        VerticalLayoutGroup vlg = colorPopup.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter fit = colorPopup.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        colorPopup.gameObject.SetActive(false);

        ApplyInsets();
    }

    private void BuildStrip()
    {
        GameObject scrollGO = new GameObject("ThumbScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGO.transform.SetParent(sheet, false);
        Image hit = scrollGO.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);   // lets empty gaps between tiles still scroll
        SetTopRow(scrollGO.GetComponent<RectTransform>(), 1194f, ThumbItemH + 10f, 0f, 0f);

        scroll = scrollGO.GetComponent<ScrollRect>();
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;

        RectTransform vp = NewRect("Viewport", scrollGO.transform);
        Stretch(vp, 0f);
        vp.gameObject.AddComponent<RectMask2D>();

        thumbContent = NewRect("Content", vp);
        thumbContent.anchorMin = new Vector2(0f, 0f);
        thumbContent.anchorMax = new Vector2(0f, 1f);
        thumbContent.pivot = new Vector2(0f, 0.5f);
        thumbContent.anchoredPosition = Vector2.zero;
        thumbContent.sizeDelta = Vector2.zero;

        HorizontalLayoutGroup h = thumbContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 37f;
        h.padding = new RectOffset((int)SidePad, (int)SidePad, 0, 0);
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        ContentSizeFitter f = thumbContent.gameObject.AddComponent<ContentSizeFitter>();
        f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vp;
        scroll.content = thumbContent;
    }

    private CatalogThumbnail MakeThumb(FurnitureItem item)
    {
        GameObject go = new GameObject("Thumb_" + item.furnitureName,
            typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(thumbContent, false);
        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);   // full-item hit area
        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = ThumbSize;
        le.preferredHeight = ThumbItemH;

        Image tile = MakeRounded(go.transform, "Tile", thumbColor, 30);
        tile.raycastTarget = false;
        RectTransform trt = tile.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = new Vector2(0f, ThumbSize);

        if (item.icon != null)
        {
            Image icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(tile.transform, false);
            icon.sprite = item.icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Stretch(icon.rectTransform, 12f);
        }

        TMP_Text label = MakeText("Label", go.transform,
            string.IsNullOrEmpty(item.furnitureName) ? "Item" : item.furnitureName,
            30, FontStyles.Normal, ink, TextAlignmentOptions.Center);
        label.enableAutoSizing = true;
        label.fontSizeMin = 20f;
        label.fontSizeMax = 30f;
        label.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(0f, 56f);

        CatalogThumbnail th = go.AddComponent<CatalogThumbnail>();
        th.item = item;
        th.tile = tile;
        th.scroll = scroll;
        th.dragController = dragController;
        th.normalColor = thumbColor;
        th.selectedColor = thumbSelected;
        th.onSelected = Select;
        th.SetSelected(false);
        return th;
    }

    private void MakePopupRow(string text, UnityAction onClick)
    {
        GameObject go = new GameObject("Row_" + text,
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(colorPopup, false);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        Button b = go.GetComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        go.GetComponent<LayoutElement>().preferredHeight = 92f;

        TMP_Text t = MakeText("Text", go.transform, text, 36, FontStyles.Normal, ink, TextAlignmentOptions.MidlineLeft);
        Stretch(t.rectTransform, 0f);
        t.rectTransform.offsetMin = new Vector2(28f, 0f);
    }

    private void ApplyInsets()
    {
        float s = CanvasScale();
        float bottom = Screen.safeArea.yMin / s;
        float top = (Screen.height - Screen.safeArea.yMax) / s;
        sheet.sizeDelta = new Vector2(0f, SheetH + bottom);
        backRT.anchoredPosition = new Vector2(40f, -(top + 40f));
    }

    private float CanvasScale()
    {
        float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
        float lw = Mathf.Log(w / RefW, 2f), lh = Mathf.Log(h / RefH, 2f);
        return Mathf.Pow(2f, Mathf.Lerp(lw, lh, Match));
    }

    // ════════════════════════════════════════════════════════════
    //  WIDGET HELPERS
    // ════════════════════════════════════════════════════════════

    /// <summary>Two-segment pill: a main button + a chevron button, split by a thin gap.</summary>
    private void MakeSplitPill(RectTransform pill, float leftW, float rightW, Color col, Color fg,
        string label, int labelSize, FontStyles style,
        UnityAction onMain, UnityAction onChevron, out TMP_Text labelText)
    {
        int r = Mathf.RoundToInt(pill.sizeDelta.y * 0.5f);

        Image main = MakeRounded(pill, "Main", col, r, true, false, true, false);
        RectTransform mrt = main.rectTransform;
        mrt.anchorMin = new Vector2(0f, 0f);
        mrt.anchorMax = new Vector2(0f, 1f);
        mrt.pivot = new Vector2(0f, 0.5f);
        mrt.anchoredPosition = Vector2.zero;
        mrt.sizeDelta = new Vector2(leftW, 0f);
        main.raycastTarget = true;
        Button mb = main.gameObject.AddComponent<Button>();
        mb.targetGraphic = main;
        mb.onClick.AddListener(onMain);
        labelText = MakeText("Label", main.transform, label, labelSize, style, fg, TextAlignmentOptions.Center);
        Stretch(labelText.rectTransform, 0f);

        Image chev = MakeRounded(pill, "Chevron", col, r, false, true, false, true);
        RectTransform crt = chev.rectTransform;
        crt.anchorMin = new Vector2(1f, 0f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(rightW, 0f);
        chev.raycastTarget = true;
        Button cb = chev.gameObject.AddComponent<Button>();
        cb.targetGraphic = chev;
        cb.onClick.AddListener(onChevron);

        // down chevron from two rounded bars
        MakeBar(chev.transform, fg, 30f, 7f, -45f, new Vector2(-8.5f, 3f));
        MakeBar(chev.transform, fg, 30f, 7f,  45f, new Vector2( 8.5f, 3f));
    }

    private Button MakeIconButton(Transform parent, string name, Vector2 size, UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);   // invisible tap target
        go.GetComponent<RectTransform>().sizeDelta = size;
        Button b = go.GetComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(onClick);
        return b;
    }

    private void MakeBar(Transform parent, Color c, float length, float thickness, float angle, Vector2 pos)
    {
        Image bar = MakeRounded(parent, "Bar", c, Mathf.RoundToInt(thickness * 0.5f));
        bar.raycastTarget = false;
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(length, thickness);
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private TMP_Text MakeText(string name, Transform parent, string content, float size,
        FontStyles style, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    private RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private Image MakeRounded(Transform parent, string name, Color color, int radius,
        bool tl = true, bool tr = true, bool bl = true, bool br = true)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.sprite = GetRoundedSprite(radius, tl, tr, bl, br);
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Anchors a rect to the top edge of its parent, full width minus side insets.</summary>
    private void SetTopRow(RectTransform rt, float top, float height, float left, float right)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(left, -(top + height));
        rt.offsetMax = new Vector2(-right, -top);
    }

    private void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    // ════════════════════════════════════════════════════════════
    //  PROCEDURAL SPRITES (cached)
    // ════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    private static Sprite GetRoundedSprite(int radius, bool tl, bool tr, bool bl, bool br)
    {
        radius = Mathf.Max(1, radius);
        string key = radius + "_" + (tl ? 1 : 0) + (tr ? 1 : 0) + (bl ? 1 : 0) + (br ? 1 : 0);
        Sprite cached;
        if (spriteCache.TryGetValue(key, out cached) && cached != null) return cached;

        int size = radius * 2 + 4;   // 4px stretchable centre for 9-slicing
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                bool left = fx < radius, right = fx > size - radius;
                bool bottom = fy < radius, top = fy > size - radius;
                bool rounded = (top && left && tl) || (top && right && tr)
                            || (bottom && left && bl) || (bottom && right && br);
                float a = 1f;
                if (rounded)
                {
                    float cx = left ? radius : size - radius;
                    float cy = bottom ? radius : size - radius;
                    float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                    a = Mathf.Clamp01(radius - d + 0.5f);   // 1px anti-aliased edge
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        spriteCache[key] = s;
        return s;
    }

    private static Sprite GetStarSprite()
    {
        Sprite cached;
        if (spriteCache.TryGetValue("star", out cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];

        Vector2[] poly = new Vector2[10];
        float c = S * 0.5f, outer = S * 0.48f, inner = outer * 0.42f;
        for (int i = 0; i < 10; i++)
        {
            float ang = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float rad = (i % 2 == 0) ? outer : inner;
            poly[i] = new Vector2(c + Mathf.Cos(ang) * rad, c - 1f + Mathf.Sin(ang) * rad);
        }

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                int hit = 0;   // 3x3 supersampling for smooth edges
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                        if (InPoly(poly, new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f))) hit++;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 9));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        Sprite s = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        spriteCache["star"] = s;
        return s;
    }

    private static bool InPoly(Vector2[] v, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
        {
            if (((v[i].y > p.y) != (v[j].y > p.y)) &&
                (p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x))
                inside = !inside;
        }
        return inside;
    }
}

/// <summary>
/// One tile in the catalog's thumbnail strip.
///   tap            -> select (show details in the sheet)
///   drag sideways  -> scroll the strip (forwarded to the ScrollRect)
///   drag up/out    -> hand over to FurnitureDragController to place in AR
/// </summary>
public class CatalogThumbnail : MonoBehaviour,
    IPointerClickHandler, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public FurnitureItem item;
    [HideInInspector] public Image tile;
    [HideInInspector] public ScrollRect scroll;
    [HideInInspector] public FurnitureDragController dragController;
    [HideInInspector] public Color normalColor, selectedColor;
    public Action<CatalogThumbnail> onSelected;

    private bool forwardToScroll;

    public void SetSelected(bool selected)
    {
        if (tile != null) tile.color = selected ? selectedColor : normalColor;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (onSelected != null) onSelected(this);
    }

    public void OnInitializePotentialDrag(PointerEventData e)
    {
        if (scroll != null) ExecuteEvents.Execute(scroll.gameObject, e, ExecuteEvents.initializePotentialDrag);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        Vector2 d = e.position - e.pressPosition;
        forwardToScroll = Mathf.Abs(d.x) > Mathf.Abs(d.y);

        if (forwardToScroll)
        {
            if (scroll != null) ExecuteEvents.Execute(scroll.gameObject, e, ExecuteEvents.beginDragHandler);
        }
        else if (dragController != null && item != null)
        {
            // Start the existing drag-to-place flow from where the finger first touched.
            dragController.OnCardTouchDown(item, e.pressPosition);
        }
    }

    public void OnDrag(PointerEventData e)
    {
        if (forwardToScroll && scroll != null)
            ExecuteEvents.Execute(scroll.gameObject, e, ExecuteEvents.dragHandler);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (forwardToScroll && scroll != null)
            ExecuteEvents.Execute(scroll.gameObject, e, ExecuteEvents.endDragHandler);
        forwardToScroll = false;
    }
}
