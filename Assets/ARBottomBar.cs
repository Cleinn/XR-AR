using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The floating green bottom bar used on the Home screen, now also in the AR scene.
/// Created automatically by FurnitureUIManager (no scene wiring needed).
///
/// Default layout (SideButtons = HomeAndProfile):
///     [ Home ]   [ old middle icon -> furniture catalog ]   [ Profile ]
///   plus two small round quick buttons above the bar on the right:
///     plane on/off (old left button) and photo (old right button).
///
/// Alternative layout (SideButtons = PlaneAndCamera):
///     [ plane on/off ]   [ furniture catalog ]   [ photo ]     (the old bar, in the new style)
///
/// The middle icon (and the quick-button icons) are copied from the old buttons in the scene,
/// and pressing them runs the old buttons' existing logic, so nothing else needs to change.
/// </summary>
public class ARBottomBar : MonoBehaviour
{
    public enum SideButtons { HomeAndProfile, PlaneAndCamera }

    [Header("Layout")]
    public SideButtons sideButtons = SideButtons.HomeAndProfile;
    public string homeSceneName = "MainMenu";

    [Header("Palette")]
    public Color green = new Color32(0x2E, 0x4A, 0x3D, 0xFF);
    public Color ink   = new Color32(0x1A, 0x1A, 0x1A, 0xFF);

    public event Action ProfilePressed;

    // ── layout constants (canvas reference pixels, 1080 x 2400; same as HomeController) ──
    private const float RefW = 1080f, RefH = 2400f, Match = 0.5f;

    private Button trayButton, cameraButton, planeButton;
    private GameObject root;                 // the bar; FurnitureInteraction fades/hides this one
    private Image planeIcon;                 // mirrors the old plane button's on/off tint
    private Image planeSource;
    private bool loadingHome;
    private readonly List<Image> navRings = new List<Image>();
    private readonly List<Image> navFills = new List<Image>();

    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    // ════════════════════════════════════════════════════════════
    //  SETUP (called by FurnitureUIManager)
    // ════════════════════════════════════════════════════════════

    public void Init(Button trayToggle, Button camera, Button planeToggle, FurnitureInteraction interaction)
    {
        trayButton = trayToggle;
        cameraButton = camera;
        planeButton = planeToggle;

        BuildUI();

        // Hide the old bar (its buttons stay alive; we call their onClick).
        if (trayButton != null && trayButton.transform.parent != null)
            trayButton.transform.parent.gameObject.SetActive(false);

        // The "piece selected" logic fades the bottom bar out and the Delete buttons in.
        // Point it at the new bar instead of the old one.
        if (interaction != null) interaction.bottomBar = root;

        // "Starting camera..." message until AR is tracking, so the screen is never just blank.
        if (GetComponent<ARStartupOverlay>() == null) gameObject.AddComponent<ARStartupOverlay>();
    }

    private void Update()
    {
        // Keep the plane quick-button tinted like the old button (white = planes shown, dim = hidden).
        if (planeIcon != null && planeSource != null)
        {
            Color c = planeIcon.color;
            c.a = planeSource.color.a;
            planeIcon.color = c;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  ACTIONS
    // ════════════════════════════════════════════════════════════

    /// <summary>Highlights one bar button (0 = left, 1 = middle, 2 = right).</summary>
    public void SetSelected(int index)
    {
        for (int i = 0; i < navRings.Count; i++)
        {
            bool on = i == index;
            navRings[i].color = new Color(1f, 1f, 1f, on ? 0.95f : 0.20f);
            navFills[i].color = on ? new Color32(0x4B, 0x6B, 0x5C, 0xFF) : new Color32(0x24, 0x3B, 0x31, 0xFF);
        }
    }

    private void Press(Button b)
    {
        if (b != null) b.onClick.Invoke();
    }

    private void GoHome()
    {
        if (loadingHome) return;
        if (!Application.CanStreamedLevelBeLoaded(homeSceneName))
        {
            Debug.LogWarning("[ARBottomBar] Scene '" + homeSceneName + "' is not in Build Settings.");
            return;
        }
        loadingHome = true;
        SetSelected(0);   // highlight Home while we switch pages
        SceneManager.LoadScene(homeSceneName);
    }

    // ════════════════════════════════════════════════════════════
    //  BUILD UI
    // ════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("ARBottomBarCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;   // above the AR HUD, below the catalog sheet (150)

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefW, RefH);
        scaler.matchWidthOrHeight = Match;

        float bottomInset = Screen.safeArea.yMin / CanvasScale();

        // ── the pill ──
        Color navColor = new Color(green.r, green.g, green.b, 0.93f);
        Image nav = MakeRounded(canvasGO.transform, "NavBar", navColor, 117);
        nav.raycastTarget = true;
        root = nav.gameObject;
        RectTransform nrt = nav.rectTransform;
        nrt.anchorMin = new Vector2(0f, 0f);
        nrt.anchorMax = new Vector2(1f, 0f);
        nrt.pivot = new Vector2(0.5f, 0f);
        nrt.offsetMin = new Vector2(24f, 35f + bottomInset);
        nrt.offsetMax = new Vector2(-24f, 35f + bottomInset + 234f);

        Sprite trayIcon   = SpriteOf(trayButton);
        Sprite cameraIcon = SpriteOf(cameraButton);
        Sprite planeSprite = SpriteOf(planeButton);

        if (sideButtons == SideButtons.HomeAndProfile)
        {
            MakeNavButton(nav.transform, 0.155f, GetIcon("home"), false, GoHome);
            MakeNavButton(nav.transform, 0.5f, trayIcon != null ? trayIcon : GetIcon("scan"), true,
                () => Press(trayButton));
            MakeNavButton(nav.transform, 0.845f, GetIcon("profile"), false,
                () => { if (ProfilePressed != null) ProfilePressed(); });

            // quick buttons above the bar, right side (children of the bar, so they hide with it)
            MakeQuickButton(nav.transform, 0, cameraIcon, GetIcon("scan"), () => Press(cameraButton), false);
            MakeQuickButton(nav.transform, 1, planeSprite, GetIcon("scan"), () => Press(planeButton), true);
        }
        else
        {
            MakeNavButton(nav.transform, 0.155f, planeSprite != null ? planeSprite : GetIcon("scan"), false,
                () => Press(planeButton), true);
            MakeNavButton(nav.transform, 0.5f, trayIcon != null ? trayIcon : GetIcon("scan"), true,
                () => Press(trayButton));
            MakeNavButton(nav.transform, 0.845f, cameraIcon != null ? cameraIcon : GetIcon("scan"), false,
                () => Press(cameraButton));
        }
    }

    private static Sprite SpriteOf(Button b)
    {
        if (b == null) return null;
        Image img = b.GetComponent<Image>();
        return img != null ? img.sprite : null;
    }

    private void MakeNavButton(Transform parent, float anchorX, Sprite icon, bool selected,
        UnityEngine.Events.UnityAction onClick, bool isPlane = false)
    {
        Image ring = MakeRounded(parent, "Nav", new Color(1f, 1f, 1f, selected ? 0.95f : 0.20f), 88);
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

        Image ic = NewImage("Icon", inner.transform, Color.white);
        ic.sprite = icon;
        ic.preserveAspect = true;
        ic.raycastTarget = false;
        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        ic.rectTransform.anchoredPosition = Vector2.zero;
        ic.rectTransform.sizeDelta = new Vector2(92f, 92f);

        if (isPlane && planeButton != null)
        {
            planeIcon = ic;
            planeSource = planeButton.GetComponent<Image>();
        }
    }

    /// <summary>Small round button floating just above the bar, right-aligned. index 0 = lowest.</summary>
    private void MakeQuickButton(Transform bar, int index, Sprite icon, Sprite fallback,
        UnityEngine.Events.UnityAction onClick, bool isPlane)
    {
        Image circle = MakeRounded(bar, "Quick" + index,
            new Color(green.r, green.g, green.b, 0.93f), 60);
        circle.raycastTarget = true;
        RectTransform rt = circle.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);     // top-right corner of the bar
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-12f, 28f + index * 150f);
        rt.sizeDelta = new Vector2(120f, 120f);
        Button b = circle.gameObject.AddComponent<Button>();
        b.targetGraphic = circle;
        b.onClick.AddListener(onClick);

        Image ic = NewImage("Icon", circle.transform, Color.white);
        ic.sprite = icon != null ? icon : fallback;
        ic.preserveAspect = true;
        ic.raycastTarget = false;
        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        ic.rectTransform.anchoredPosition = Vector2.zero;
        ic.rectTransform.sizeDelta = new Vector2(64f, 64f);

        if (isPlane && planeButton != null)
        {
            planeIcon = ic;
            planeSource = planeButton.GetComponent<Image>();
        }
    }

    // ════════════════════════════════════════════════════════════
    //  HELPERS / PROCEDURAL SPRITES
    // ════════════════════════════════════════════════════════════

    private float CanvasScale()
    {
        float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
        float lw = Mathf.Log(w / RefW, 2f), lh = Mathf.Log(h / RefH, 2f);
        return Mathf.Pow(2f, Mathf.Lerp(lw, lh, Match));
    }

    private Image NewImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private Image MakeRounded(Transform parent, string name, Color color, int radius)
    {
        Image img = NewImage(name, parent, color);
        img.sprite = GetRoundedSprite(radius);
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        return img;
    }

    private void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

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

    // ── line-art icons (same shapes as HomeController's bottom bar) ──

    private class IconDef
    {
        public float thick = 0.09f;
        public readonly List<Vector4> segs = new List<Vector4>();
        public readonly List<Vector3> discs = new List<Vector3>();
        public readonly List<Vector3> rings = new List<Vector3>();
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
            case "home":
                d.polys.Add(new Vector2[] {
                    new Vector2(0.50f, 0.90f), new Vector2(0.94f, 0.50f), new Vector2(0.80f, 0.50f),
                    new Vector2(0.80f, 0.12f), new Vector2(0.20f, 0.12f), new Vector2(0.20f, 0.50f),
                    new Vector2(0.06f, 0.50f) });
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
            default: // "scan" (fallback when an old icon can't be found)
                d.thick = 0.08f;
                float lo = 0.12f, hi = 0.88f, len = 0.24f;
                d.Line(lo, hi - len, lo, hi, lo + len, hi);
                d.Line(hi - len, hi, hi, hi, hi, hi - len);
                d.Line(lo, lo + len, lo, lo, lo + len, lo);
                d.Line(hi - len, lo, hi, lo, hi, lo + len);
                d.Seg(0.28f, 0.50f, 0.72f, 0.50f);
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
/// Shown over the AR scene until the AR session is tracking, so the screen is never an
/// unexplained black rectangle while the camera starts (or when it can't start).
/// Added automatically by ARBottomBar.
/// </summary>
public class ARStartupOverlay : MonoBehaviour
{
    public float timeoutSeconds = 10f;

    private CanvasGroup group;
    private TMPro.TMP_Text title, sub;
    private Image bg;
    private float elapsed;
    private bool timedOut, closing;

    private void Awake()
    {
        GameObject canvasGO = new GameObject("ARStartupCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasGO.transform.SetParent(transform, false);
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;   // under the bottom bar (100) so the bar still works
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2400f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.GetComponent<GraphicRaycaster>().enabled = false;   // never blocks touches
        group = canvasGO.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        GameObject bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(canvasGO.transform, false);
        bg = bgGO.GetComponent<Image>();
        bg.color = new Color32(0x0B, 0x10, 0x0D, 0xFF);
        bg.raycastTarget = false;
        RectTransform brt = bg.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = brt.offsetMax = Vector2.zero;

        title = MakeText(canvasGO.transform, "Starting camera\u2026", 60, 0.56f, FontStyles.Bold, Color.white);
        sub = MakeText(canvasGO.transform, "Move your phone slowly to scan the floor", 38, 0.51f,
            FontStyles.Normal, new Color(1f, 1f, 1f, 0.7f));
    }

    private TMPro.TMP_Text MakeText(Transform parent, string text, float size, float anchorY,
        FontStyles style, Color color)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TMPro.TextMeshProUGUI t = go.AddComponent<TMPro.TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.raycastTarget = false;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, anchorY);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(900f, 140f);
        return t;
    }

    private void Update()
    {
        if (closing) return;
        elapsed += Time.unscaledDeltaTime;

        // elapsed > 0.75: ignore a stale "tracking" state left over from the previous visit
        bool tracking = elapsed > 0.75f &&
                        UnityEngine.XR.ARFoundation.ARSession.state ==
                        UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking;

        if (tracking) { StartCoroutine(FadeAndClose(0.4f)); return; }

        if (!timedOut && elapsed > timeoutSeconds)
        {
            timedOut = true;
            title.text = "Camera not ready";
            sub.text = "Allow camera permission and check that this device supports ARCore. " +
                       "(This is normal in the Unity Editor.)";
            sub.rectTransform.sizeDelta = new Vector2(900f, 260f);
            StartCoroutine(FadeAndClose(6f));   // keep the message for a few seconds, then clear it
        }
    }

    private System.Collections.IEnumerator FadeAndClose(float delay)
    {
        closing = true;
        if (delay > 1f) yield return new WaitForSecondsRealtime(delay);
        float t = 0f, dur = 0.4f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(t / dur);
            yield return null;
        }
        Destroy(group.gameObject);
        Destroy(this);
    }
}
