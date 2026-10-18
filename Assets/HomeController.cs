using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Home screen shown BEFORE the AR camera. Built entirely at runtime (same approach as
/// MainMenuController / CatalogSheet), so there is no manual UI wiring.
///
/// The AR scene (and therefore the camera + AR session) is only loaded when the user taps
/// the SCAN button in the bottom bar (or the AR badge on a product card).
///
/// SETUP: open the MainMenu scene, remove/disable the object that has MainMenuController,
/// add an empty GameObject "Home" and attach this script. Done.
/// </summary>
public class HomeController : MonoBehaviour
{
    [Serializable]
    public class BannerSlide
    {
        public string pre = "Up To";
        public string highlight = "30%";
        public string post = "Discount";
        public string sub = "Limited-time Deals";
        public string button = "Shop Now";
        [Tooltip("If ticked, the button opens the AR camera instead of scrolling to the items.")]
        public bool buttonOpensAR = false;
        [Tooltip("Optional picture (transparent PNG works best).")]
        public Sprite image;
        [Tooltip("If no picture is set, uses the icon of the first furniture item whose name contains this.")]
        public string imageKeyword = "table";
    }

    [Header("Scenes")]
    [Tooltip("Name of the AR scene. Must be in Build Settings.")]
    public string arSceneName = "SampleScene";

    [Header("Data")]
    [Tooltip("FurnitureDatabase asset name inside a Resources folder.")]
    public string databaseResourceName = "FurnitureDatabase";

    [Header("Banner")]
    public float autoAdvanceSeconds = 5f;
    public BannerSlide[] slides = new BannerSlide[]
    {
        new BannerSlide(),
        new BannerSlide
        {
            pre = "Try Before You Buy", highlight = "AR", post = "Preview",
            sub = "See it in your own room", button = "Scan Now",
            buttonOpensAR = true, imageKeyword = "chair"
        }
    };

    [Header("Palette")]
    public Color bgColor     = new Color32(0xEB, 0xE7, 0xDD, 0xFF);
    public Color ink         = new Color32(0x1A, 0x1A, 0x1A, 0xFF);
    public Color muted       = new Color32(0x8A, 0x8A, 0x8A, 0xFF);
    public Color green       = new Color32(0x2E, 0x4A, 0x3D, 0xFF);
    public Color chipGray    = new Color32(0xC6, 0xC6, 0xC9, 0xFF);
    public Color cardBg      = new Color32(0xF4, 0xF1, 0xEB, 0xFF);
    public Color accentTeal  = new Color32(0x6F, 0xD6, 0xCC, 0xFF);
    public Color accentOrange = new Color32(0xE8, 0x85, 0x1F, 0xFF);

    /// <summary>Set when the user opens AR from a product card. CatalogSheet reads and clears it.</summary>
    public static FurnitureItem PendingItem;

    public event Action CartPressed;
    public event Action MenuPressed;
    public event Action ProfilePressed;

    // ── layout constants (canvas reference pixels, 1080 x 2400) ──
    private const float RefW = 1080f, RefH = 2400f, Match = 0.5f;
    private const float BannerH = 396f;
    private const float SideMargin = 30f;
    private const float TopBarH = 106f;

    // ── runtime refs ──
    private FurnitureDatabase database;
    private Canvas canvas;
    private ScrollRect pageScroll;
    private RectTransform pageContent, gridRT, chipsContent;
    private TMP_Text emptyText;
    private Image fadeOverlay;
    private float canvasW, topInset, bottomInset;

    private string searchQuery = "";
    private string activeCategory = null;   // null = All
    private readonly List<Image> chipBgs = new List<Image>();
    private readonly List<TMP_Text> chipLabels = new List<TMP_Text>();
    private readonly List<string> chipCategories = new List<string>();   // null entry = All

    private readonly List<CanvasGroup> slideGroups = new List<CanvasGroup>();
    private readonly List<Image> dots = new List<Image>();
    private int currentSlide;
    private Coroutine bannerAnim;
    private bool loading;

    private readonly List<Image> navRings = new List<Image>();
    private readonly List<Image> navFills = new List<Image>();
    private TMP_Text loadingLabel;

    // ════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (!string.IsNullOrEmpty(databaseResourceName))
            database = Resources.Load<FurnitureDatabase>(databaseResourceName);
        ShutdownXR();   // coming back from AR: release the camera + AR session completely
        EnsureEventSystem();
        BuildUI();
        RebuildChips();
        RebuildGrid();
    }

    private void Start()
    {
        StartCoroutine(FadeOverlay(1f, 0f, 0.3f));
        if (slides != null && slides.Length > 1) StartCoroutine(AutoAdvance());
    }

    // ════════════════════════════════════════════════════════════
    //  ACTIONS
    // ════════════════════════════════════════════════════════════

    /// <summary>The ONLY place the AR scene (and camera) is started.</summary>
    public void StartAR()
    {
        if (loading) return;
        if (!Application.CanStreamedLevelBeLoaded(arSceneName))
        {
            Debug.LogError("[Home] Scene '" + arSceneName + "' is not in Build Settings.");
            return;
        }
        loading = true;
        SetNavSelected(1);   // highlight Scan while we switch pages
        StartCoroutine(LoadAR());
    }

    public void OpenInAR(FurnitureItem item)
    {
        PendingItem = item;
        StartAR();
    }

    private IEnumerator LoadAR()
    {
        yield return FadeOverlay(0f, 1f, 0.3f);

        // Don't leave a silent black screen while the AR scene loads.
        if (loadingLabel != null) loadingLabel.gameObject.SetActive(true);

        // Fresh AR session every time (fixes the black camera on the 2nd, 3rd... visit).
        yield return StartXR();

        AsyncOperation op = SceneManager.LoadSceneAsync(arSceneName);
        while (op != null && !op.isDone) yield return null;
    }

    // ── XR lifecycle ──
    // When the AR scene is closed, its ARSession is destroyed and takes the XR subsystems with it.
    // On the next visit the new ARSession would get a dead subsystem, and the camera stays black.
    // So: shut XR down whenever Home is shown, and start it fresh just before loading AR.

    private static void ShutdownXR()
    {
        XRGeneralSettings gs = XRGeneralSettings.Instance;
        if (gs == null || gs.Manager == null) return;
        if (gs.Manager.isInitializationComplete)
        {
            gs.Manager.StopSubsystems();
            gs.Manager.DeinitializeLoader();
        }
    }

    private static IEnumerator StartXR()
    {
        XRGeneralSettings gs = XRGeneralSettings.Instance;
        if (gs == null || gs.Manager == null) yield break;
        if (!gs.Manager.isInitializationComplete)
            yield return gs.Manager.InitializeLoader();
        // The ARSession in the AR scene starts the subsystems itself when it enables.
    }

    private void OnScanPressed()
    {
        PendingItem = null;
        StartAR();
    }

    private void OnHomePressed()
    {
        SetNavSelected(0);
        StartCoroutine(ScrollTo(0f));
    }

    /// <summary>Highlights one bottom-bar button (0 = Home, 1 = Scan, 2 = Profile).</summary>
    public void SetNavSelected(int index)
    {
        for (int i = 0; i < navRings.Count; i++)
        {
            bool on = i == index;
            navRings[i].color = new Color(1f, 1f, 1f, on ? 0.95f : 0.20f);
            navFills[i].color = on ? new Color32(0x4B, 0x6B, 0x5C, 0xFF) : new Color32(0x24, 0x3B, 0x31, 0xFF);
        }
    }

    private void ScrollToItems()
    {
        StartCoroutine(ScrollTo(BannerH + 40f));
    }

    private IEnumerator ScrollTo(float y)
    {
        RectTransform c = pageScroll.content;
        pageScroll.StopMovement();
        float from = c.anchoredPosition.y, t = 0f, dur = 0.35f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
            c.anchoredPosition = new Vector2(c.anchoredPosition.x, Mathf.Lerp(from, y, k));
            yield return null;
        }
    }

    private IEnumerator FadeOverlay(float from, float to, float dur)
    {
        if (fadeOverlay == null) yield break;
        fadeOverlay.raycastTarget = true;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            fadeOverlay.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.Clamp01(t / dur)));
            yield return null;
        }
        fadeOverlay.color = new Color(0f, 0f, 0f, to);
        fadeOverlay.raycastTarget = to > 0.5f;
    }

    // ── search / categories ──

    private void OnSearchChanged(string q)
    {
        searchQuery = q == null ? "" : q.Trim();
        RebuildGrid();
    }

    private void SelectCategory(string category)
    {
        activeCategory = category;
        for (int i = 0; i < chipBgs.Count; i++)
        {
            bool on = string.Equals(chipCategories[i], category, StringComparison.OrdinalIgnoreCase)
                      || (chipCategories[i] == null && category == null);
            chipBgs[i].color = on ? green : chipGray;
            chipLabels[i].color = on ? Color.white : ink;
        }
        RebuildGrid();
    }

    private void RebuildChips()
    {
        for (int i = chipsContent.childCount - 1; i >= 0; i--)
        {
            GameObject old = chipsContent.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }
        chipBgs.Clear(); chipLabels.Clear(); chipCategories.Clear();

        List<string> cats = new List<string>();
        if (database != null && database.items != null)
        {
            foreach (FurnitureItem it in database.items)
            {
                if (it == null || string.IsNullOrEmpty(it.category)) continue;
                bool exists = false;
                foreach (string c in cats)
                    if (string.Equals(c, it.category, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists) cats.Add(it.category);
            }
        }

        MakeChip("All", null);
        foreach (string c in cats) MakeChip(c, c);
        SelectCategory(null);
    }

    private void MakeChip(string label, string category)
    {
        Image bg = MakeRounded(chipsContent, "Chip_" + label, chipGray, 39);
        bg.raycastTarget = true;
        LayoutElement le = bg.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 78f;
        HorizontalLayoutGroup h = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(46, 46, 0, 0);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        TMP_Text t = MakeText("Label", bg.transform, label, 44, FontStyles.Normal, ink, TextAlignmentOptions.Center);
        Button b = bg.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        string cat = category;
        b.onClick.AddListener(() => SelectCategory(cat));

        chipBgs.Add(bg); chipLabels.Add(t); chipCategories.Add(category);
    }

    private void RebuildGrid()
    {
        for (int i = gridRT.childCount - 1; i >= 0; i--)
        {
            GameObject old = gridRT.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }

        int shown = 0;
        if (database != null && database.items != null)
        {
            foreach (FurnitureItem item in database.items)
            {
                if (item == null) continue;
                if (activeCategory != null &&
                    !string.Equals(item.category, activeCategory, StringComparison.OrdinalIgnoreCase)) continue;
                if (searchQuery.Length > 0 &&
                    (item.furnitureName == null ||
                     item.furnitureName.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                MakeCard(item);
                shown++;
            }
        }
        emptyText.gameObject.SetActive(shown == 0);
    }

    // ════════════════════════════════════════════════════════════
    //  BUILD UI
    // ════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("HomeCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefW, RefH);
        scaler.matchWidthOrHeight = Match;

        float s = CanvasScale();
        canvasW = Screen.width / s;
        topInset = (Screen.height - Screen.safeArea.yMax) / s;
        bottomInset = Screen.safeArea.yMin / s;

        Transform root = canvasGO.transform;

        Image bg = NewImage("Background", root, bgColor);
        Stretch(bg.rectTransform, 0f);
        bg.raycastTarget = false;

        float top = Mathf.Max(topInset, 60f) + 20f;

        BuildPage(root, top + TopBarH + 24f);
        BuildTopBar(root, top);
        BuildNav(root);

        fadeOverlay = NewImage("FadeOverlay", root, new Color(0f, 0f, 0f, 1f));
        Stretch(fadeOverlay.rectTransform, 0f);
        fadeOverlay.transform.SetAsLastSibling();

        loadingLabel = MakeText("LoadingLabel", fadeOverlay.transform, "Opening camera\u2026", 52,
            FontStyles.Normal, Color.white, TextAlignmentOptions.Center);
        Stretch(loadingLabel.rectTransform, 0f);
        loadingLabel.gameObject.SetActive(false);
    }

    // ── top bar: search, cart, menu ──

    private void BuildTopBar(Transform root, float top)
    {
        // Search field
        Image field = MakeRounded(root, "SearchField", Color.white, 53);
        field.raycastTarget = true;
        field.gameObject.SetActive(false);   // finish wiring before it enables
        RectTransform frt = field.rectTransform;
        frt.anchorMin = new Vector2(0f, 1f);
        frt.anchorMax = new Vector2(1f, 1f);
        frt.pivot = new Vector2(0.5f, 1f);
        frt.offsetMin = new Vector2(77f, -(top + TopBarH));
        frt.offsetMax = new Vector2(-359f, -top);

        RectTransform area = NewRect("TextArea", field.transform);
        Stretch(area, 0f);
        area.offsetMin = new Vector2(40f, 0f);
        area.offsetMax = new Vector2(-110f, 0f);
        area.gameObject.AddComponent<RectMask2D>();

        TMP_Text placeholder = MakeText("Placeholder", area, "Search Furniture", 38, FontStyles.Normal,
            new Color(0.62f, 0.62f, 0.62f, 1f), TextAlignmentOptions.MidlineLeft);
        Stretch(placeholder.rectTransform, 0f);
        TMP_Text text = MakeText("Text", area, "", 38, FontStyles.Normal, ink, TextAlignmentOptions.MidlineLeft);
        text.richText = false;
        Stretch(text.rectTransform, 0f);

        TMP_InputField input = field.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = field;
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onValueChanged.AddListener(OnSearchChanged);
        field.gameObject.SetActive(true);

        Image searchIcon = MakeIcon(field.transform, "SearchIcon", "search", ink, 52f);
        searchIcon.rectTransform.anchorMin = searchIcon.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        searchIcon.rectTransform.pivot = new Vector2(1f, 0.5f);
        searchIcon.rectTransform.anchoredPosition = new Vector2(-34f, 0f);

        // Cart + red badge
        Button cart = MakeIconButton(root, "CartButton", "cart", ink, new Vector2(100f, 100f), 80f,
            () => { if (CartPressed != null) CartPressed(); });
        PlaceTopRight(cart.GetComponent<RectTransform>(), 271f, top + TopBarH * 0.5f);
        Image badge = MakeRounded(cart.transform, "Badge", new Color32(0xE5, 0x39, 0x35, 0xFF), 21);
        badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(1f, 1f);
        badge.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        badge.rectTransform.anchoredPosition = new Vector2(-12f, -14f);
        badge.rectTransform.sizeDelta = new Vector2(42f, 42f);
        TMP_Text bang = MakeText("Bang", badge.transform, "!", 28, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        Stretch(bang.rectTransform, 0f);

        // Hamburger
        Button menu = MakeIconButton(root, "MenuButton", "menu", ink, new Vector2(100f, 100f), 74f,
            () => { if (MenuPressed != null) MenuPressed(); });
        PlaceTopRight(menu.GetComponent<RectTransform>(), 128f, top + TopBarH * 0.5f);
    }

    // ── scrolling page: banner, title, chips, grid ──

    private void BuildPage(Transform root, float topOffset)
    {
        GameObject scrollGO = new GameObject("PageScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGO.transform.SetParent(root, false);
        scrollGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        RectTransform srt = scrollGO.GetComponent<RectTransform>();
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = Vector2.zero;
        srt.offsetMax = new Vector2(0f, -topOffset);

        pageScroll = scrollGO.GetComponent<ScrollRect>();
        pageScroll.horizontal = false;
        pageScroll.vertical = true;
        pageScroll.movementType = ScrollRect.MovementType.Elastic;
        pageScroll.scrollSensitivity = 40f;

        RectTransform vp = NewRect("Viewport", srt);
        Stretch(vp, 0f);
        vp.gameObject.AddComponent<RectMask2D>();

        pageContent = NewRect("Content", vp);
        pageContent.anchorMin = new Vector2(0f, 1f);
        pageContent.anchorMax = new Vector2(1f, 1f);
        pageContent.pivot = new Vector2(0.5f, 1f);
        pageContent.anchoredPosition = Vector2.zero;
        pageContent.sizeDelta = Vector2.zero;

        VerticalLayoutGroup v = pageContent.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset((int)SideMargin, (int)SideMargin, 6, (int)(300f + bottomInset));
        v.spacing = 30f;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        ContentSizeFitter f = pageContent.gameObject.AddComponent<ContentSizeFitter>();
        f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        pageScroll.viewport = vp;
        pageScroll.content = pageContent;

        BuildBanner(pageContent);

        TMP_Text title = MakeText("PopularTitle", pageContent, "Popular Items", 64, FontStyles.Normal, ink,
            TextAlignmentOptions.BottomLeft);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;

        BuildChipsRow(pageContent);

        // Grid of product cards
        gridRT = NewRect("Grid", pageContent);
        GridLayoutGroup g = gridRT.gameObject.AddComponent<GridLayoutGroup>();
        float cellW = Mathf.Floor((canvasW - SideMargin * 2f - 35f) * 0.5f);
        g.cellSize = new Vector2(cellW, Mathf.Round(cellW * 606f / 492f));
        g.spacing = new Vector2(35f, 40f);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 2;
        g.childAlignment = TextAnchor.UpperLeft;
        ContentSizeFitter gf = gridRT.gameObject.AddComponent<ContentSizeFitter>();
        gf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        emptyText = MakeText("Empty", pageContent, "No items found", 44, FontStyles.Italic, muted, TextAlignmentOptions.Center);
        emptyText.gameObject.AddComponent<LayoutElement>().preferredHeight = 160f;
        emptyText.gameObject.SetActive(false);
    }

    private void BuildChipsRow(Transform parent)
    {
        GameObject scrollGO = new GameObject("ChipsScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGO.transform.SetParent(parent, false);
        scrollGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        scrollGO.GetComponent<LayoutElement>().preferredHeight = 90f;

        ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
        sr.horizontal = true;
        sr.vertical = false;
        sr.movementType = ScrollRect.MovementType.Elastic;

        RectTransform vp = NewRect("Viewport", scrollGO.transform);
        Stretch(vp, 0f);
        vp.gameObject.AddComponent<RectMask2D>();

        chipsContent = NewRect("Content", vp);
        chipsContent.anchorMin = new Vector2(0f, 0f);
        chipsContent.anchorMax = new Vector2(0f, 1f);
        chipsContent.pivot = new Vector2(0f, 0.5f);
        chipsContent.anchoredPosition = Vector2.zero;
        chipsContent.sizeDelta = Vector2.zero;

        HorizontalLayoutGroup h = chipsContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 35f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        ContentSizeFitter f = chipsContent.gameObject.AddComponent<ContentSizeFitter>();
        f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = vp;
        sr.content = chipsContent;
    }

    // ── banner carousel ──

    private void BuildBanner(Transform parent)
    {
        Image banner = MakeRounded(parent, "Banner", Color.white, 40);
        banner.raycastTarget = true;
        Mask mask = banner.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        banner.gameObject.AddComponent<LayoutElement>().preferredHeight = BannerH;

        Image grad = NewImage("Gradient", banner.transform, Color.white);
        grad.sprite = GetBannerGradient();
        grad.raycastTarget = false;
        Stretch(grad.rectTransform, 0f);

        slideGroups.Clear();
        dots.Clear();
        int n = (slides != null) ? slides.Length : 0;
        for (int i = 0; i < n; i++) BuildSlide(banner.transform, slides[i]);

        for (int i = 0; i < n; i++)
        {
            Image dot = MakeIcon(banner.transform, "Dot" + i, "disc", Color.white, 21f);
            RectTransform rt = dot.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((i - (n - 1) * 0.5f) * 40f, 37f);
            dots.Add(dot);
        }

        HomeBannerSwipe swipe = banner.gameObject.AddComponent<HomeBannerSwipe>();
        swipe.scroll = pageScroll;
        swipe.onSwipe = dir => { if (slideGroups.Count > 1) ShowSlide(currentSlide + dir); };

        currentSlide = 0;
        for (int i = 0; i < slideGroups.Count; i++) SetSlideState(i, i == 0 ? 1f : 0f);
        UpdateDots();
    }

    private void BuildSlide(Transform parent, BannerSlide slide)
    {
        RectTransform sr = NewRect("Slide", parent);
        Stretch(sr, 0f);
        CanvasGroup cg = sr.gameObject.AddComponent<CanvasGroup>();
        slideGroups.Add(cg);

        Sprite pic = slide.image != null ? slide.image : FindIcon(slide.imageKeyword);
        if (pic != null)
        {
            Image im = NewImage("Image", sr, Color.white);
            im.sprite = pic;
            im.preserveAspect = true;
            im.raycastTarget = false;
            RectTransform irt = im.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(1f, 0f);
            irt.pivot = new Vector2(1f, 0f);
            irt.anchoredPosition = new Vector2(-14f, -16f);
            irt.sizeDelta = new Vector2(430f, 350f);
        }

        Color soft = new Color32(0xCF, 0xCF, 0xCF, 0xFF);
        TMP_Text pre = MakeText("Pre", sr, slide.pre, 44, FontStyles.Normal, soft, TextAlignmentOptions.MidlineLeft);
        PlaceLeftTop(pre.rectTransform, 50f, 46f, 600f, 62f);

        string hl = "<color=#" + ColorUtility.ToHtmlStringRGB(accentOrange) + ">" + slide.highlight + "</color>";
        TMP_Text main = MakeText("Main", sr, hl + " " + slide.post, 68, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);
        PlaceLeftTop(main.rectTransform, 50f, 104f, 640f, 90f);

        TMP_Text sub = MakeText("Sub", sr, slide.sub, 38, FontStyles.Normal, soft, TextAlignmentOptions.MidlineLeft);
        PlaceLeftTop(sub.rectTransform, 50f, 182f, 600f, 56f);

        Image btn = MakeRounded(sr, "Button", Color.white, 42);
        btn.raycastTarget = true;
        PlaceLeftTop(btn.rectTransform, 50f, 266f, 343f, 85f);
        Button b = btn.gameObject.AddComponent<Button>();
        b.targetGraphic = btn;
        bool ar = slide.buttonOpensAR;
        b.onClick.AddListener(() => { if (ar) OnScanPressed(); else ScrollToItems(); });
        TMP_Text bl = MakeText("Label", btn.transform, slide.button, 44, FontStyles.Normal, ink, TextAlignmentOptions.Center);
        Stretch(bl.rectTransform, 0f);
    }

    private Sprite FindIcon(string keyword)
    {
        if (database == null || database.items == null || string.IsNullOrEmpty(keyword)) return null;
        foreach (FurnitureItem it in database.items)
            if (it != null && it.icon != null && it.furnitureName != null &&
                it.furnitureName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return it.icon;
        return null;
    }

    private void ShowSlide(int index)
    {
        int n = slideGroups.Count;
        if (n < 2) return;
        index = ((index % n) + n) % n;
        if (index == currentSlide) return;
        if (bannerAnim != null) StopCoroutine(bannerAnim);
        bannerAnim = StartCoroutine(CrossFade(currentSlide, index));
        currentSlide = index;
        UpdateDots();
    }

    private IEnumerator CrossFade(int from, int to)
    {
        float t = 0f, dur = 0.35f;
        float fa = slideGroups[from].alpha, ta = slideGroups[to].alpha;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            slideGroups[from].alpha = Mathf.Lerp(fa, 0f, k);
            slideGroups[to].alpha = Mathf.Lerp(ta, 1f, k);
            yield return null;
        }
        for (int i = 0; i < slideGroups.Count; i++) SetSlideState(i, i == to ? 1f : 0f);
    }

    private void SetSlideState(int i, float alpha)
    {
        slideGroups[i].alpha = alpha;
        slideGroups[i].blocksRaycasts = alpha > 0.5f;
        slideGroups[i].interactable = alpha > 0.5f;
    }

    private void UpdateDots()
    {
        for (int i = 0; i < dots.Count; i++)
            dots[i].color = (i == currentSlide) ? Color.white : new Color(1f, 1f, 1f, 0.35f);
    }

    private IEnumerator AutoAdvance()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(1f, autoAdvanceSeconds));
            ShowSlide(currentSlide + 1);
        }
    }

    // ── product card ──

    private void MakeCard(FurnitureItem item)
    {
        Image card = MakeRounded(gridRT, "Card_" + item.furnitureName, cardBg, 40);
        card.raycastTarget = true;
        Button cb = card.gameObject.AddComponent<Button>();
        cb.targetGraphic = card;
        cb.onClick.AddListener(() => OpenInAR(item));

        // product picture (sits above the green info panel)
        if (item.icon != null)
        {
            Image pic = NewImage("Product", card.transform, Color.white);
            pic.sprite = item.icon;
            pic.preserveAspect = true;
            pic.raycastTarget = false;
            RectTransform prt = pic.rectTransform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(30f, 240f);
            prt.offsetMax = new Vector2(-30f, -26f);
        }

        // green info panel
        Image panel = MakeRounded(card.transform, "Info", green, 40);
        RectTransform pr = panel.rectTransform;
        pr.anchorMin = new Vector2(0f, 0f);
        pr.anchorMax = new Vector2(1f, 0f);
        pr.pivot = new Vector2(0.5f, 0f);
        pr.anchoredPosition = Vector2.zero;
        pr.sizeDelta = new Vector2(0f, 226f);

        string room = !string.IsNullOrEmpty(item.room) ? item.room
                    : (!string.IsNullOrEmpty(item.category) ? item.category : "Furniture");
        Color soft = new Color(1f, 1f, 1f, 0.82f);

        TMP_Text r = MakeText("Room", panel.transform, room, 36, FontStyles.Normal, soft, TextAlignmentOptions.MidlineLeft);
        SetTopRow(r.rectTransform, 12f, 56f, 30f, 130f);
        TMP_Text n = MakeText("Name", panel.transform, string.IsNullOrEmpty(item.furnitureName) ? "Item" : item.furnitureName,
            44, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);
        n.enableAutoSizing = true; n.fontSizeMin = 28f; n.fontSizeMax = 44f;
        n.overflowMode = TextOverflowModes.Ellipsis;
        SetTopRow(n.rectTransform, 72f, 60f, 30f, 130f);
        if (item.price > 0)
        {
            TMP_Text p = MakeText("Price", panel.transform, "Rp. " + FormatRupiah(item.price), 38,
                FontStyles.Normal, soft, TextAlignmentOptions.MidlineLeft);
            p.enableAutoSizing = true; p.fontSizeMin = 24f; p.fontSizeMax = 38f;
            SetTopRow(p.rectTransform, 136f, 56f, 30f, 20f);
        }

        // AR badge
        Image badge = MakeRounded(panel.transform, "ARBadge",
            new Color(accentTeal.r, accentTeal.g, accentTeal.b, 0.28f), 22);
        badge.raycastTarget = true;
        RectTransform brt = badge.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
        brt.anchoredPosition = new Vector2(-29f, -35f);
        brt.sizeDelta = new Vector2(80f, 80f);
        Button bb = badge.gameObject.AddComponent<Button>();
        bb.targetGraphic = badge;
        bb.onClick.AddListener(() => OpenInAR(item));
        Image cube = MakeIcon(badge.transform, "Cube", "cube", accentTeal, 52f);
        cube.rectTransform.anchorMin = cube.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        cube.rectTransform.anchoredPosition = Vector2.zero;
    }

    // ── floating bottom bar ──

    private void BuildNav(Transform root)
    {
        Color navColor = new Color(green.r, green.g, green.b, 0.93f);
        Image nav = MakeRounded(root, "NavBar", navColor, 117);
        nav.raycastTarget = true;
        RectTransform nrt = nav.rectTransform;
        nrt.anchorMin = new Vector2(0f, 0f);
        nrt.anchorMax = new Vector2(1f, 0f);
        nrt.pivot = new Vector2(0.5f, 0f);
        nrt.offsetMin = new Vector2(24f, 35f + bottomInset);
        nrt.offsetMax = new Vector2(-24f, 35f + bottomInset + 234f);

        MakeNavButton(nav.transform, 0.155f, "home", true, OnHomePressed);
        MakeNavButton(nav.transform, 0.5f, "scan", false, OnScanPressed);
        MakeNavButton(nav.transform, 0.845f, "profile", false, () => { if (ProfilePressed != null) ProfilePressed(); });
    }

    private void MakeNavButton(Transform parent, float anchorX, string icon, bool selected, UnityEngine.Events.UnityAction onClick)
    {
        Image ring = MakeRounded(parent, "Nav_" + icon, new Color(1f, 1f, 1f, selected ? 0.95f : 0.20f), 88);
        ring.raycastTarget = true;
        RectTransform rrt = ring.rectTransform;
        rrt.anchorMin = rrt.anchorMax = new Vector2(anchorX, 0.5f);
        rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.anchoredPosition = Vector2.zero;
        rrt.sizeDelta = new Vector2(175f, 175f);
        Button b = ring.gameObject.AddComponent<Button>();
        b.targetGraphic = ring;
        b.onClick.AddListener(onClick);

        Color fill = selected ? new Color32(0x4B, 0x6B, 0x5C, 0xFF) : new Color32(0x24, 0x3B, 0x31, 0xFF);
        Image inner = MakeRounded(ring.transform, "Fill", fill, 80);
        Stretch(inner.rectTransform, 6f);
        navRings.Add(ring);
        navFills.Add(inner);

        Image ic = MakeIcon(inner.transform, "Icon", icon, Color.white, 92f);
        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        ic.rectTransform.anchoredPosition = Vector2.zero;
    }

    // ════════════════════════════════════════════════════════════
    //  SMALL HELPERS
    // ════════════════════════════════════════════════════════════

    private static string FormatRupiah(int value)
    {
        string digits = Mathf.Abs(value).ToString(CultureInfo.InvariantCulture);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append('.');
            sb.Append(digits[i]);
        }
        return (value < 0 ? "-" : "") + sb;
    }

    private float CanvasScale()
    {
        float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
        float lw = Mathf.Log(w / RefW, 2f), lh = Mathf.Log(h / RefH, 2f);
        return Mathf.Pow(2f, Mathf.Lerp(lw, lh, Match));
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    private RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private Image NewImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        return img;
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

    private Image MakeRounded(Transform parent, string name, Color color, int radius)
    {
        Image img = NewImage(name, parent, color);
        img.sprite = GetRoundedSprite(radius);
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        return img;
    }

    private Image MakeIcon(Transform parent, string name, string icon, Color color, float size)
    {
        Image img = NewImage(name, parent, color);
        img.sprite = GetIcon(icon);
        img.raycastTarget = false;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        return img;
    }

    private Button MakeIconButton(Transform parent, string name, string icon, Color color,
        Vector2 hitSize, float iconSize, UnityEngine.Events.UnityAction onClick)
    {
        Image hit = NewImage(name, parent, new Color(0f, 0f, 0f, 0f));
        hit.rectTransform.sizeDelta = hitSize;
        Button b = hit.gameObject.AddComponent<Button>();
        b.targetGraphic = hit;
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(onClick);
        Image ic = MakeIcon(hit.transform, "Icon", icon, color, iconSize);
        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        ic.rectTransform.anchoredPosition = Vector2.zero;
        return b;
    }

    private void PlaceTopRight(RectTransform rt, float centerFromRight, float centerFromTop)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-centerFromRight, -centerFromTop);
    }

    private void PlaceLeftTop(RectTransform rt, float left, float top, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(left, -top);
        rt.sizeDelta = new Vector2(w, h);
    }

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

    private static Sprite GetRoundedSprite(int radius)
    {
        radius = Mathf.Max(1, radius);
        string key = "round_" + radius;
        Sprite cached;
        if (spriteCache.TryGetValue(key, out cached) && cached != null) return cached;

        int size = radius * 2 + 4;
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
                float a = 1f;
                if ((left || right) && (bottom || top))
                {
                    float cx = left ? radius : size - radius;
                    float cy = bottom ? radius : size - radius;
                    float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                    a = Mathf.Clamp01(radius - d + 0.5f);
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

    private static Sprite GetBannerGradient()
    {
        Sprite cached;
        if (spriteCache.TryGetValue("bannerGrad", out cached) && cached != null) return cached;

        const int W = 128, H = 64;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        Color dark = new Color32(0x0B, 0x0B, 0x0C, 0xFF);
        Color warm = new Color32(0x52, 0x44, 0x36, 0xFF);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1), v = y / (float)(H - 1);
                float dx = (u - 0.74f) * 2f, dy = (v - 1.08f);
                float f = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 1.0f);
                f = f * f;
                px[y * W + x] = Color.Lerp(dark, warm, f);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        spriteCache["bannerGrad"] = s;
        return s;
    }

    // ── line-art icons, rasterised from simple shapes (white, tinted by Image.color) ──

    private class IconDef
    {
        public float thick = 0.09f;
        public readonly List<Vector4> segs = new List<Vector4>();    // x0,y0,x1,y1
        public readonly List<Vector3> discs = new List<Vector3>();   // cx,cy,r
        public readonly List<Vector3> rings = new List<Vector3>();   // cx,cy,r
        public readonly List<Vector2[]> polys = new List<Vector2[]>();

        public void Seg(float x0, float y0, float x1, float y1) { segs.Add(new Vector4(x0, y0, x1, y1)); }
        public void Line(params float[] p)
        {
            for (int i = 0; i + 3 < p.Length; i += 2) Seg(p[i], p[i + 1], p[i + 2], p[i + 3]);
        }
    }

    private static Sprite GetIcon(string name)
    {
        Sprite cached;
        if (spriteCache.TryGetValue("icon_" + name, out cached) && cached != null) return cached;

        IconDef d = new IconDef();
        switch (name)
        {
            case "search":
                d.thick = 0.10f;
                d.rings.Add(new Vector3(0.42f, 0.58f, 0.28f));
                d.Seg(0.62f, 0.38f, 0.86f, 0.14f);
                break;
            case "cart":
                d.thick = 0.08f;
                d.Line(0.06f, 0.86f, 0.20f, 0.86f, 0.32f, 0.34f, 0.84f, 0.34f, 0.94f, 0.70f, 0.25f, 0.70f);
                d.discs.Add(new Vector3(0.40f, 0.14f, 0.075f));
                d.discs.Add(new Vector3(0.78f, 0.14f, 0.075f));
                break;
            case "menu":
                d.thick = 0.12f;
                d.Seg(0.10f, 0.80f, 0.90f, 0.80f);
                d.Seg(0.10f, 0.50f, 0.90f, 0.50f);
                d.Seg(0.10f, 0.20f, 0.90f, 0.20f);
                break;
            case "home":
                d.polys.Add(new Vector2[] {
                    new Vector2(0.50f, 0.90f), new Vector2(0.94f, 0.50f), new Vector2(0.80f, 0.50f),
                    new Vector2(0.80f, 0.12f), new Vector2(0.20f, 0.12f), new Vector2(0.20f, 0.50f),
                    new Vector2(0.06f, 0.50f) });
                break;
            case "scan":
                d.thick = 0.08f;
                float lo = 0.12f, hi = 0.88f, len = 0.24f;
                d.Line(lo, hi - len, lo, hi, lo + len, hi);
                d.Line(hi - len, hi, hi, hi, hi, hi - len);
                d.Line(lo, lo + len, lo, lo, lo + len, lo);
                d.Line(hi - len, lo, hi, lo, hi, lo + len);
                d.Seg(0.28f, 0.50f, 0.72f, 0.50f);
                break;
            case "profile":
                d.thick = 0.07f;
                d.rings.Add(new Vector3(0.5f, 0.5f, 0.42f));
                d.discs.Add(new Vector3(0.5f, 0.64f, 0.12f));
                for (int a = 15; a < 165; a += 15)
                {
                    float a0 = a * Mathf.Deg2Rad, a1 = (a + 15) * Mathf.Deg2Rad;
                    d.Seg(0.5f + Mathf.Cos(a0) * 0.22f, 0.27f + Mathf.Sin(a0) * 0.22f,
                          0.5f + Mathf.Cos(a1) * 0.22f, 0.27f + Mathf.Sin(a1) * 0.22f);
                }
                break;
            case "cube":
                d.thick = 0.07f;
                Vector2[] hex = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float ang = (90f + i * 60f) * Mathf.Deg2Rad;
                    hex[i] = new Vector2(0.5f + Mathf.Cos(ang) * 0.40f, 0.5f + Mathf.Sin(ang) * 0.40f);
                }
                for (int i = 0; i < 6; i++) d.Seg(hex[i].x, hex[i].y, hex[(i + 1) % 6].x, hex[(i + 1) % 6].y);
                d.Seg(0.5f, 0.5f, hex[1].x, hex[1].y);
                d.Seg(0.5f, 0.5f, hex[3].x, hex[3].y);
                d.Seg(0.5f, 0.5f, hex[5].x, hex[5].y);
                break;
            default: // "disc"
                d.discs.Add(new Vector3(0.5f, 0.5f, 0.48f));
                break;
        }

        Sprite s = RasterIcon(d);
        spriteCache["icon_" + name] = s;
        return s;
    }

    private static Sprite RasterIcon(IconDef d)
    {
        const int S = 128;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        float halfT = d.thick * 0.5f;

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / S, (y + 0.5f) / S);
                float cov = 0f;

                foreach (Vector4 sg in d.segs)
                {
                    float dist = DistToSegment(p, new Vector2(sg.x, sg.y), new Vector2(sg.z, sg.w));
                    cov = Mathf.Max(cov, Mathf.Clamp01((halfT - dist) * S + 0.5f));
                }
                foreach (Vector3 c in d.discs)
                {
                    float dist = Vector2.Distance(p, new Vector2(c.x, c.y));
                    cov = Mathf.Max(cov, Mathf.Clamp01((c.z - dist) * S + 0.5f));
                }
                foreach (Vector3 c in d.rings)
                {
                    float dist = Mathf.Abs(Vector2.Distance(p, new Vector2(c.x, c.y)) - c.z);
                    cov = Mathf.Max(cov, Mathf.Clamp01((halfT - dist) * S + 0.5f));
                }
                foreach (Vector2[] poly in d.polys)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                            if (InPoly(poly, new Vector2((x + (sx + 0.5f) / 3f) / S, (y + (sy + 0.5f) / 3f) / S))) hit++;
                    cov = Mathf.Max(cov, hit / 9f);
                }
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(cov * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * t);
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
/// Swipe left/right on the banner to change slides. Vertical drags are handed to the page's
/// ScrollRect so the page still scrolls when the finger starts on the banner.
/// </summary>
public class HomeBannerSwipe : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public ScrollRect scroll;
    public Action<int> onSwipe;
    public float minSwipe = 60f;

    private bool forward;

    public void OnInitializePotentialDrag(PointerEventData e)
    {
        if (scroll != null) ExecuteEvents.Execute(scroll.gameObject, e, ExecuteEvents.initializePotentialDrag);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        Vector2 d = e.position - e.pressPosition;
        forward = Mathf.Abs(d.y) > Mathf.Abs(d.x);
        if (forward && scroll != null) ExecuteEvents.Execute(scroll.gameObject, e, ExecuteEvents.beginDragHandler);
    }

    public void OnDrag(PointerEventData e)
    {
        if (forward && scroll != null) ExecuteEvents.Execute(scroll.gameObject, e, ExecuteEvents.dragHandler);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (forward)
        {
            if (scroll != null) ExecuteEvents.Execute(scroll.gameObject, e, ExecuteEvents.endDragHandler);
        }
        else
        {
            float dx = e.position.x - e.pressPosition.x;
            if (Mathf.Abs(dx) > minSwipe && onSwipe != null) onSwipe(dx < 0f ? 1 : -1);
        }
        forward = false;
    }
}
