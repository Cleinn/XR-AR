using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Animated home screen / main menu for the AR Furniture app — built entirely at runtime.
///
/// WHAT'S NEW vs the basic version:
///   • Graphics  : procedural AR reticle (rotating + pulsing), perspective floor grid,
///                 glow, drifting particles, sweeping scan line, HUD corner brackets.
///   • Text      : typewriter title, a "terminal" status line, rotating taglines,
///                 letter-spacing intro on the subtitle.
///   • Motion    : staggered slide-in for everything, button press/hover scaling,
///                 fade-to-black scene transition, scale+fade panel.
///   • Help      : a scrollable, sectioned "How to Use" guide with detailed steps.
///
/// SETUP (no manual UI wiring needed):
///   1. Put this file in Assets/ (next to your other scripts).
///   2. Create a new scene: Assets/Scenes/MainMenu.unity
///   3. Add an empty GameObject "MainMenu", attach this script.
///   4. Make sure 'arSceneName' below matches your AR scene ("SampleScene").
///   5. File > Build Settings > Add Open Scenes — MainMenu at index 0, SampleScene at index 1.
///   6. Press Play.
///
/// Inspector knobs let you turn animation on/off and change all the text + colors.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Scene to load")]
    [Tooltip("Name of your AR scene. Must be added to Build Settings.")]
    public string arSceneName = "SampleScene";

    [Header("Text")]
    public string titleText    = "AR FURNITURE";
    public string subtitleText = "EXTENDED REALITY PROJECT";
    public string[] taglines = {
        "Place furniture in your room before you buy it.",
        "Detect a surface. Drag. Drop. Done.",
        "Real models, real scale, your real room."
    };

    [Header("Animation")]
    public bool playIntro       = true;
    public bool ambientMotion   = true;

    [Header("Palette")]
    public Color bgTop      = new Color32(0x0A, 0x0E, 0x11, 0xFF);
    public Color bgBottom   = new Color32(0x0F, 0x1A, 0x1E, 0xFF);
    public Color panelColor = new Color32(0x14, 0x20, 0x27, 0xFF);
    public Color accent     = new Color32(0x2F, 0xE6, 0xA0, 0xFF); // placement-valid green
    public Color textColor  = new Color32(0xE9, 0xF1, 0xED, 0xFF);
    public Color muted      = new Color32(0x8F, 0xA3, 0x9B, 0xFF);
    public Color darkInk    = new Color32(0x04, 0x14, 0x0D, 0xFF); // text on the green button

    // runtime refs
    private Canvas canvas;
    private CanvasGroup rootGroup;
    private GameObject helpPanel;
    private Image fadeOverlay;
    private TMP_Text taglineText;

    // cached procedural sprites
    private Sprite ringSprite, gridSprite, glowSprite, softSprite, bracketSprite, lineSprite;

    // intro targets
    private List<RectTransform> introTargets;
    private TMP_Text introTitle, introStatus, introSubtitle;

    private void Awake()
    {
        BuildSprites();
        EnsureEventSystem();
        BuildUI();
    }

    private void Start()
    {
        if (playIntro) StartCoroutine(IntroSequence());
        if (taglines != null && taglines.Length > 1) StartCoroutine(CycleTaglines());
    }

    // ════════════════════════════════════════════════════════════
    //  BUILD
    // ════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("MenuCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 2400);
        scaler.matchWidthOrHeight = 0.5f;

        rootGroup = canvasGO.AddComponent<CanvasGroup>();
        Transform root = canvasGO.transform;

        // ── Background gradient ──
        Image bg = MakeImage("Background", root, Color.white, gradient: true);
        Stretch(bg.rectTransform);

        // ── Perspective floor grid (bottom) ──
        Image grid = MakeImage("FloorGrid", root, new Color(1, 1, 1, 0.9f), spriteOverride: gridSprite);
        SetAnchored(grid.rectTransform, new Vector2(0.5f, 0.20f), new Vector2(1400, 900));
        grid.raycastTarget = false;
        if (ambientMotion) StartCoroutine(PulseAlpha(grid, 0.6f, 0.95f, 4f));

        // ── Glow behind the hero ──
        Image glow = MakeImage("Glow", root, new Color(accent.r, accent.g, accent.b, 0.16f), spriteOverride: glowSprite);
        SetAnchored(glow.rectTransform, new Vector2(0.5f, 0.80f), new Vector2(1000, 1000));
        glow.raycastTarget = false;

        // ── Drifting particles ──
        if (ambientMotion) SpawnParticles(root, 16);

        // ── AR reticle (hero centerpiece) ──
        Image reticle = MakeImage("Reticle", root, accent, spriteOverride: ringSprite);
        SetAnchored(reticle.rectTransform, new Vector2(0.5f, 0.80f), new Vector2(440, 440));
        reticle.raycastTarget = false;
        if (ambientMotion)
        {
            StartCoroutine(RotateForever(reticle.rectTransform, 14f));
            StartCoroutine(PulseScale(reticle.rectTransform, 1f, 1.05f, 3.2f));
        }

        // ── Scan line ──
        if (ambientMotion)
        {
            Image scan = MakeImage("ScanLine", root, new Color(accent.r, accent.g, accent.b, 0.5f), spriteOverride: lineSprite);
            scan.raycastTarget = false;
            StartCoroutine(SweepScanLine(scan.rectTransform));
        }

        // ── HUD corner brackets ──
        MakeCorner(root, new Vector2(0f, 1f), new Vector2( 60, -60));
        MakeCorner(root, new Vector2(1f, 1f), new Vector2(-60, -60));
        MakeCorner(root, new Vector2(0f, 0f), new Vector2( 60,  60));
        MakeCorner(root, new Vector2(1f, 0f), new Vector2(-60,  60));

        // ── Title (typewriter) ──
        TMP_Text title = MakeText("Title", root, titleText, 110, FontStyles.Bold, textColor);
        SetAnchored(title.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(1000, 150));
        title.characterSpacing = 8f;

        // ── Subtitle (letter-spacing intro) ──
        TMP_Text subtitle = MakeText("Subtitle", root, subtitleText, 36, FontStyles.Normal, accent);
        SetAnchored(subtitle.rectTransform, new Vector2(0.5f, 0.565f), new Vector2(1000, 60));
        subtitle.characterSpacing = 14f;

        // ── Rotating tagline ──
        taglineText = MakeText("Tagline", root,
            (taglines != null && taglines.Length > 0) ? taglines[0] : "", 32, FontStyles.Italic, muted);
        SetAnchored(taglineText.rectTransform, new Vector2(0.5f, 0.50f), new Vector2(820, 100));

        // ── Terminal status line (mono typewriter) ──
        TMP_Text status = MakeText("Status", root, "", 26, FontStyles.Normal, accent);
        SetAnchored(status.rectTransform, new Vector2(0.5f, 0.455f), new Vector2(900, 44));

        // ── Buttons ──
        Button startBtn = MakeButton("StartButton", root, "START PLACING",
            new Vector2(0.5f, 0.34f), accent, darkInk, StartAR);
        Button helpBtn  = MakeButton("HelpButton", root, "HOW TO USE",
            new Vector2(0.5f, 0.255f), panelColor, textColor, () => ShowHelp(true));
        Button quitBtn  = MakeButton("QuitButton", root, "QUIT",
            new Vector2(0.5f, 0.17f), panelColor, muted, QuitApp);

        // ── Footer ──
        TMP_Text footer = MakeText("Footer", root, "v0.1.0   \u00B7   ANDROID   \u00B7   ARCORE", 24, FontStyles.Normal, muted);
        SetAnchored(footer.rectTransform, new Vector2(0.5f, 0.06f), new Vector2(900, 50));
        footer.characterSpacing = 6f;

        // ── Help panel (built hidden) ──
        BuildHelpPanel(root);

        // ── Fade overlay (top of stack, for scene transition) ──
        fadeOverlay = MakeImage("FadeOverlay", root, new Color(0, 0, 0, 0));
        Stretch(fadeOverlay.rectTransform);
        fadeOverlay.raycastTarget = false;
        fadeOverlay.transform.SetAsLastSibling();

        // Stash references the intro animates
        introTargets = new List<RectTransform> {
            title.rectTransform, subtitle.rectTransform, taglineText.rectTransform,
            startBtn.GetComponent<RectTransform>(), helpBtn.GetComponent<RectTransform>(),
            quitBtn.GetComponent<RectTransform>(), footer.rectTransform
        };
        introTitle    = title;
        introStatus   = status;
        introSubtitle = subtitle;
    }

    // ════════════════════════════════════════════════════════════
    //  INTRO SEQUENCE
    // ════════════════════════════════════════════════════════════

    private IEnumerator IntroSequence()
    {
        rootGroup.alpha = 0f;
        yield return Fade(rootGroup, 0f, 1f, 0.4f);

        // Title types out
        if (introTitle != null) yield return Typewriter(introTitle, titleText, 22f);

        // Subtitle: collapse letter spacing in
        if (introSubtitle != null) StartCoroutine(LetterSpacingIn(introSubtitle, 40f, 14f, 0.5f));

        // Staggered slide-in for the lower elements (skip the title, already revealed)
        float delay = 0f;
        for (int i = 1; i < introTargets.Count; i++)
        {
            StartCoroutine(SlideFadeIn(introTargets[i], 60f, delay, 0.45f));
            delay += 0.07f;
        }

        yield return new WaitForSecondsRealtime(0.25f);

        // Terminal status line types out
        if (introStatus != null)
            yield return Typewriter(introStatus, "> AR MODULE READY  \u00B7  TAP START", 35f);
    }

    // ════════════════════════════════════════════════════════════
    //  DETAILED "HOW TO USE" PANEL (scrollable, sectioned)
    // ════════════════════════════════════════════════════════════

    private void BuildHelpPanel(Transform parent)
    {
        helpPanel = new GameObject("HelpPanel", typeof(RectTransform), typeof(CanvasGroup));
        helpPanel.transform.SetParent(parent, false);
        Stretch(helpPanel.GetComponent<RectTransform>());

        Image dim = MakeImage("Dim", helpPanel.transform, new Color(0, 0, 0, 0.78f));
        Stretch(dim.rectTransform);

        // Card
        Image card = MakeImage("Card", helpPanel.transform, panelColor);
        SetAnchored(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(980, 1700));

        // Accent header bar
        Image hbar = MakeImage("HeaderBar", card.transform, accent);
        SetAnchored(hbar.rectTransform, new Vector2(0.5f, 0.952f), new Vector2(980, 6));

        TMP_Text head = MakeText("Head", card.transform, "HOW TO USE", 56, FontStyles.Bold, accent);
        SetAnchored(head.rectTransform, new Vector2(0.5f, 0.915f), new Vector2(880, 80));
        head.characterSpacing = 6f;

        TMP_Text sub = MakeText("HeadSub", card.transform, "A quick guide to placing furniture in AR", 28, FontStyles.Italic, muted);
        SetAnchored(sub.rectTransform, new Vector2(0.5f, 0.875f), new Vector2(880, 50));

        // Scroll view in the middle of the card
        RectTransform content = MakeScrollView(card.transform,
            new Vector2(0.5f, 0.50f), new Vector2(900, 1180));

        AddSection(content, "\u2460  BEFORE YOU START",
            "\u2022  Use the app in a well-lit room . AR needs visible texture and detail to track.\n" +
            "\u2022  Hold your phone up and sweep it slowly across the floor with small side-to-side motions.\n" +
            "\u2022  Wait for the glowing surface grid to appear. That mesh is a detected plane you can place on.");

        AddSection(content, "\u2461  PLACE A PIECE",
            "\u2022  Tap the tray button on the bottom bar to open the furniture tray.\n" +
            "\u2022  Press a card and drag it out a ghost preview follows your finger.\n" +
            "\u2022  Drag over the floor and release. A GREEN ring means it's a valid spot. RED means the surface is too small, so aim for a larger area.\n" +
            "\u2022  The piece drops at true real-world scale, flat on the floor.");

        AddSection(content, "\u2462  MOVE & ROTATE",
            "\u2022  Tap a placed piece to select it, a ring appears around its base.\n" +
            "\u2022  Drag with one finger to slide it along the floor.\n" +
            "\u2022  Use two fingers and twist to rotate it to any angle.\n" +
            "\u2022  A quick tap won't nudge it: you must drag past a small threshold, so taps stay clean.");

        AddSection(content, "\u2463  REMOVE & RESET",
            "\u2022  With a piece selected, tap Delete to remove just that one.\n" +
            "\u2022  Tap Delete All to clear the whole room and start over.\n" +
            "\u2022  Use the plane toggle to hide or show the surface grid at any time.");

        AddSection(content, "\u2464  SAVE A PHOTO",
            "\u2022  Arrange the room the way you like it.\n" +
            "\u2022  Tap the camera button, the interface hides, the shot is captured, and a PNG is saved to your gallery in an 'AR Furniture' album.");

        AddSection(content, "\u2726  TIPS",
            "\u2022  Avoid plain, glossy, or very dark floors. Texture helps tracking.\n" +
            "\u2022  If a piece drifts, move your phone around to give the camera more to lock onto.\n" +
            "\u2022  Lost your surfaces? Just point at the floor again to re-scan.");

        // Close button (fixed at the bottom of the card)
        MakeButton("CloseHelp", card.transform, "GOT IT",
            new Vector2(0.5f, 0.06f), accent, darkInk, () => ShowHelp(false), 560, 130);

        helpPanel.SetActive(false);
    }

    private void AddSection(RectTransform content, string heading, string body)
    {
        TMP_Text h = MakeText("SecHead", content, heading, 34, FontStyles.Bold, textColor);
        h.alignment = TextAlignmentOptions.Left;
        LayoutElement le1 = h.gameObject.AddComponent<LayoutElement>();
        le1.minHeight = 50;

        TMP_Text b = MakeText("SecBody", content, body, 28, FontStyles.Normal, muted);
        b.alignment = TextAlignmentOptions.TopLeft;
    }

    // ════════════════════════════════════════════════════════════
    //  ACTIONS
    // ════════════════════════════════════════════════════════════

    public void StartAR()
    {
        if (!Application.CanStreamedLevelBeLoaded(arSceneName))
        {
            Debug.LogError("[MainMenu] Scene '" + arSceneName +
                "' is not in Build Settings. Add it via File > Build Settings.");
            return;
        }
        StartCoroutine(StartARRoutine());
    }

    private IEnumerator StartARRoutine()
    {
        if (fadeOverlay != null)
        {
            fadeOverlay.raycastTarget = true;
            float t = 0f;
            while (t < 0.45f)
            {
                t += Time.unscaledDeltaTime;
                fadeOverlay.color = new Color(0, 0, 0, Mathf.Clamp01(t / 0.45f));
                yield return null;
            }
        }
        SceneManager.LoadScene(arSceneName);
    }

    public void ShowHelp(bool show)
    {
        if (helpPanel == null) return;
        helpPanel.SetActive(true);
        CanvasGroup cg = helpPanel.GetComponent<CanvasGroup>();
        RectTransform card = helpPanel.transform.Find("Card") as RectTransform;
        StopCoroutine(nameof(AnimateHelp));
        StartCoroutine(AnimateHelp(cg, card, show));
    }

    private IEnumerator AnimateHelp(CanvasGroup cg, RectTransform card, bool show)
    {
        float dur = 0.22f, t = 0f;
        float aFrom = cg.alpha, aTo = show ? 1f : 0f;
        float sFrom = card != null ? card.localScale.x : 1f;
        float sTo   = show ? 1f : 0.92f;
        if (show && card != null) card.localScale = Vector3.one * 0.92f;
        cg.interactable = cg.blocksRaycasts = show;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = EaseOutCubic(t / dur);
            cg.alpha = Mathf.Lerp(aFrom, aTo, k);
            if (card != null) card.localScale = Vector3.one * Mathf.Lerp(sFrom, sTo, k);
            yield return null;
        }
        cg.alpha = aTo;
        if (card != null) card.localScale = Vector3.one * sTo;
        if (!show) helpPanel.SetActive(false);
    }

    public void QuitApp()
    {
        Debug.Log("[MainMenu] Quit requested.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ════════════════════════════════════════════════════════════
    //  TEXT ANIMATIONS
    // ════════════════════════════════════════════════════════════

    private IEnumerator Typewriter(TMP_Text t, string full, float charsPerSecond)
    {
        t.text = full;
        t.ForceMeshUpdate();
        int total = t.textInfo.characterCount;
        t.maxVisibleCharacters = 0;
        float shown = 0f;
        while (shown < total)
        {
            shown += Time.unscaledDeltaTime * charsPerSecond;
            t.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(shown), 0, total);
            yield return null;
        }
        t.maxVisibleCharacters = total;
    }

    private IEnumerator LetterSpacingIn(TMP_Text t, float from, float to, float dur)
    {
        float e = 0f;
        while (e < dur)
        {
            e += Time.unscaledDeltaTime;
            t.characterSpacing = Mathf.Lerp(from, to, EaseOutCubic(e / dur));
            yield return null;
        }
        t.characterSpacing = to;
    }

    private IEnumerator CycleTaglines()
    {
        int i = 0;
        var wait = new WaitForSecondsRealtime(4.5f);
        while (true)
        {
            yield return wait;
            i = (i + 1) % taglines.Length;
            yield return FadeGraphic(taglineText, 1f, 0f, 0.3f);
            taglineText.text = taglines[i];
            yield return FadeGraphic(taglineText, 0f, 1f, 0.3f);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  MOTION HELPERS
    // ════════════════════════════════════════════════════════════

    private IEnumerator Fade(CanvasGroup cg, float from, float to, float dur)
    {
        float t = 0f;
        while (t < dur) { t += Time.unscaledDeltaTime; cg.alpha = Mathf.Lerp(from, to, t / dur); yield return null; }
        cg.alpha = to;
    }

    private IEnumerator FadeGraphic(Graphic g, float from, float to, float dur)
    {
        Color c = g.color; float t = 0f;
        while (t < dur) { t += Time.unscaledDeltaTime; c.a = Mathf.Lerp(from, to, t / dur); g.color = c; yield return null; }
        c.a = to; g.color = c;
    }

    private IEnumerator SlideFadeIn(RectTransform rt, float fromYOffset, float delay, float dur)
    {
        CanvasGroup cg = rt.GetComponent<CanvasGroup>();
        if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
        Vector2 target = rt.anchoredPosition;
        Vector2 start  = target + Vector2.down * fromYOffset;
        rt.anchoredPosition = start; cg.alpha = 0f;
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = EaseOutCubic(t / dur);
            rt.anchoredPosition = Vector2.Lerp(start, target, k);
            cg.alpha = k;
            yield return null;
        }
        rt.anchoredPosition = target; cg.alpha = 1f;
    }

    private IEnumerator RotateForever(RectTransform rt, float degPerSec)
    {
        while (true) { rt.Rotate(0, 0, -degPerSec * Time.unscaledDeltaTime); yield return null; }
    }

    private IEnumerator PulseScale(RectTransform rt, float lo, float hi, float period)
    {
        float t = 0f;
        while (true)
        {
            t += Time.unscaledDeltaTime;
            float k = (Mathf.Sin(t / period * Mathf.PI * 2f) + 1f) * 0.5f;
            rt.localScale = Vector3.one * Mathf.Lerp(lo, hi, k);
            yield return null;
        }
    }

    private IEnumerator PulseAlpha(Graphic g, float lo, float hi, float period)
    {
        float t = 0f; Color c = g.color;
        while (true)
        {
            t += Time.unscaledDeltaTime;
            float k = (Mathf.Sin(t / period * Mathf.PI * 2f) + 1f) * 0.5f;
            c.a = Mathf.Lerp(lo, hi, k); g.color = c;
            yield return null;
        }
    }

    private IEnumerator SweepScanLine(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1080, 4);
        float top = 1100f, bottom = -1100f, t = 0f, dur = 6f;
        while (true)
        {
            t += Time.unscaledDeltaTime;
            float k = (t % dur) / dur;
            rt.anchoredPosition = new Vector2(0, Mathf.Lerp(top, bottom, k));
            yield return null;
        }
    }

    private void SpawnParticles(Transform parent, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Image p = MakeImage("Particle" + i, parent,
                new Color(accent.r, accent.g, accent.b, 0f), spriteOverride: softSprite);
            p.raycastTarget = false;
            float size = UnityEngine.Random.Range(6f, 16f);
            SetAnchored(p.rectTransform, new Vector2(UnityEngine.Random.value, 0f), new Vector2(size, size));
            StartCoroutine(DriftParticle(p));
        }
    }

    private IEnumerator DriftParticle(Image p)
    {
        RectTransform rt = p.rectTransform;
        while (true)
        {
            float dur = UnityEngine.Random.Range(6f, 12f);
            float x = UnityEngine.Random.Range(0.05f, 0.95f);
            rt.anchorMin = rt.anchorMax = new Vector2(x, 0f);
            float maxA = UnityEngine.Random.Range(0.15f, 0.5f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = t / dur;
                rt.anchoredPosition = new Vector2(Mathf.Sin(k * 6f) * 14f, Mathf.Lerp(-20, 2500, k));
                float a = Mathf.Sin(k * Mathf.PI) * maxA; // fade in then out
                p.color = new Color(accent.r, accent.g, accent.b, a);
                yield return null;
            }
        }
    }

    private float EaseOutCubic(float x) { return 1f - Mathf.Pow(1f - Mathf.Clamp01(x), 3f); }

    // ════════════════════════════════════════════════════════════
    //  UI BUILDERS
    // ════════════════════════════════════════════════════════════

    private Image MakeImage(string name, Transform parent, Color color,
                            bool gradient = false, Sprite spriteOverride = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        if (gradient) img.sprite = SpriteFromTexture(MakeGradientTexture(bgTop, bgBottom));
        else if (spriteOverride != null) img.sprite = spriteOverride;
        return img;
    }

    private TMP_Text MakeText(string name, Transform parent, string content,
                              float size, FontStyles style, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.richText = true;
        return t;
    }

    private Button MakeButton(string name, Transform parent, string label,
                              Vector2 anchor, Color bg, Color fg, UnityEngine.Events.UnityAction onClick,
                              float width = 760f, float height = 150f)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = bg;
        SetAnchored(go.GetComponent<RectTransform>(), anchor, new Vector2(width, height));

        Button btn = go.GetComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        go.AddComponent<UIButtonMotion>(); // press/hover scale feedback

        TMP_Text txt = MakeText(name + "_Label", go.transform, label, 40, FontStyles.Bold, fg);
        Stretch(txt.rectTransform);
        txt.raycastTarget = false;
        txt.characterSpacing = 4f;

        return btn;
    }

    private void MakeCorner(Transform parent, Vector2 anchor, Vector2 offset)
    {
        // L-shaped bracket from two thin bars
        float len = 70f, thick = 4f;
        Image h = MakeImage("CornerH", parent, accent, spriteOverride: bracketSprite);
        Image v = MakeImage("CornerV", parent, accent, spriteOverride: bracketSprite);
        h.raycastTarget = v.raycastTarget = false;

        float sx = anchor.x < 0.5f ? 1f : -1f;
        float sy = anchor.y < 0.5f ? 1f : -1f;

        var rh = h.rectTransform; rh.anchorMin = rh.anchorMax = anchor; rh.pivot = new Vector2(0.5f, 0.5f);
        rh.sizeDelta = new Vector2(len, thick);
        rh.anchoredPosition = offset + new Vector2(sx * len * 0.5f, 0);

        var rv = v.rectTransform; rv.anchorMin = rv.anchorMax = anchor; rv.pivot = new Vector2(0.5f, 0.5f);
        rv.sizeDelta = new Vector2(thick, len);
        rv.anchoredPosition = offset + new Vector2(0, sy * len * 0.5f);
    }

    private RectTransform MakeScrollView(Transform parent, Vector2 anchor, Vector2 size)
    {
        GameObject scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGO.transform.SetParent(parent, false);
        SetAnchored(scrollGO.GetComponent<RectTransform>(), anchor, size);
        ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Elastic;
        sr.scrollSensitivity = 30f;

        // Viewport with mask
        GameObject vp = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        vp.transform.SetParent(scrollGO.transform, false);
        Stretch(vp.GetComponent<RectTransform>());
        vp.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f); // near-invisible; mask needs a graphic

        // Content
        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(vp.transform, false);
        RectTransform crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero; crt.sizeDelta = new Vector2(0, 0);

        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.spacing = 22; vlg.padding = new RectOffset(20, 20, 10, 30);

        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = vp.GetComponent<RectTransform>();
        sr.content = crt;
        return crt;
    }

    // ════════════════════════════════════════════════════════════
    //  PROCEDURAL SPRITES
    // ════════════════════════════════════════════════════════════

    private void BuildSprites()
    {
        ringSprite    = SpriteFromTexture(MakeRingTexture(256));
        gridSprite    = SpriteFromTexture(MakeGridTexture(512));
        glowSprite    = SpriteFromTexture(MakeSoftCircle(256));
        softSprite    = SpriteFromTexture(MakeSoftCircle(64));
        bracketSprite = SpriteFromTexture(MakeSolid(8));
        lineSprite    = SpriteFromTexture(MakeHLineTexture(256));
    }

    private Sprite SpriteFromTexture(Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    private Texture2D NewTex(int w, int h)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Bilinear; t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    private Texture2D MakeSolid(int s)
    {
        Texture2D t = NewTex(s, s);
        Color32[] px = new Color32[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        t.SetPixels32(px); t.Apply(); return t;
    }

    private Texture2D MakeGradientTexture(Color top, Color bottom)
    {
        int h = 256, w = 4;
        Texture2D t = NewTex(w, h);
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            Color c = Color.Lerp(bottom, top, y / (float)(h - 1));
            for (int x = 0; x < w; x++) px[y * w + x] = c;
        }
        t.SetPixels32(px); t.Apply(); return t;
    }

    private Texture2D MakeSoftCircle(int s)
    {
        Texture2D t = NewTex(s, s);
        Color32[] px = new Color32[s * s];
        float c = (s - 1) * 0.5f, r = c;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / r;
                float a = Mathf.Clamp01(1f - d); a = a * a; // soft falloff
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        t.SetPixels32(px); t.Apply(); return t;
    }

    private Texture2D MakeHLineTexture(int s)
    {
        Texture2D t = NewTex(s, 1);
        Color32[] px = new Color32[s];
        for (int x = 0; x < s; x++)
        {
            float k = x / (float)(s - 1);
            float a = Mathf.Sin(k * Mathf.PI); // bright in the middle, faded at the ends
            px[x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        t.SetPixels32(px); t.Apply(); return t;
    }

    private Texture2D MakeRingTexture(int s)
    {
        Texture2D t = NewTex(s, s);
        Color32[] px = new Color32[s * s];
        float c = (s - 1) * 0.5f;
        float rOuter = c * 0.92f, rInner = c * 0.74f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;
                a = Mathf.Max(a, RingBand(d, rOuter, 2.2f));                 // outer solid ring
                float ang = Mathf.Atan2(dy, dx);
                float dash = (Mathf.Sin(ang * 18f) > 0f) ? 1f : 0f;         // inner dashed ring
                a = Mathf.Max(a, RingBand(d, rInner, 1.6f) * dash * 0.7f);
                if (d > rOuter && d < rOuter + 10f)                          // 4 tick marks
                {
                    float deg = ang * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.DeltaAngle(deg, 0f))   < 3 ||
                        Mathf.Abs(Mathf.DeltaAngle(deg, 90f))  < 3 ||
                        Mathf.Abs(Mathf.DeltaAngle(deg, 180f)) < 3 ||
                        Mathf.Abs(Mathf.DeltaAngle(deg, 270f)) < 3) a = Mathf.Max(a, 0.9f);
                }
                if (d < 4f) a = 1f;                                          // centre dot
                px[y * s + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        t.SetPixels32(px); t.Apply(); return t;
    }

    private float RingBand(float d, float r, float halfWidth)
    {
        float diff = Mathf.Abs(d - r);
        return Mathf.Clamp01(1f - diff / halfWidth);
    }

    // Faux-perspective floor grid: vertical lines fanning from a vanishing point,
    // horizontal lines spaced with perspective, fading toward the horizon.
    private Texture2D MakeGridTexture(int s)
    {
        Texture2D t = NewTex(s, s);
        Color32[] px = new Color32[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

        float vpX = s * 0.5f;   // vanishing point x
        float vpY = s * 0.95f;  // horizon near the top
        int rays = 14;
        for (int r = -rays; r <= rays; r++)
        {
            float bottomX = vpX + r * (s / (float)rays) * 0.9f;
            DrawLine(px, s, s, vpX, vpY, bottomX, 0, 0.55f);
        }
        int rows = 16;
        for (int row = 1; row <= rows; row++)
        {
            float k = row / (float)rows;
            float y = Mathf.Lerp(vpY, 0, k * k); // denser near the horizon
            float alpha = 0.55f * (1f - k * 0.55f);
            DrawHoriz(px, s, s, y, alpha);
        }
        t.SetPixels32(px); t.Apply(); return t;
    }

    private void DrawHoriz(Color32[] px, int w, int h, float yf, float alpha)
    {
        int y = Mathf.RoundToInt(yf);
        if (y < 0 || y >= h) return;
        byte a = (byte)(Mathf.Clamp01(alpha) * 255);
        for (int x = 0; x < w; x++) SetPx(px, w, h, x, y, a);
    }

    private void DrawLine(Color32[] px, int w, int h, float x0, float y0, float x1, float y1, float alpha)
    {
        int ix0 = Mathf.RoundToInt(x0), iy0 = Mathf.RoundToInt(y0);
        int ix1 = Mathf.RoundToInt(x1), iy1 = Mathf.RoundToInt(y1);
        int dx = Mathf.Abs(ix1 - ix0), dy = Mathf.Abs(iy1 - iy0);
        int sx = ix0 < ix1 ? 1 : -1, sy = iy0 < iy1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            float f = iy0 / (float)h; // fade as it nears the horizon (top)
            byte a = (byte)(Mathf.Clamp01(alpha * f) * 255);
            SetPx(px, w, h, ix0, iy0, a);
            if (ix0 == ix1 && iy0 == iy1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; ix0 += sx; }
            if (e2 <  dx) { err += dx; iy0 += sy; }
        }
    }

    private void SetPx(Color32[] px, int w, int h, int x, int y, byte a)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        int idx = y * w + x;
        if (a > px[idx].a) px[idx] = new Color32(255, 255, 255, a);
    }

    // ════════════════════════════════════════════════════════════
    //  RECT HELPERS
    // ════════════════════════════════════════════════════════════

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private void SetAnchored(RectTransform rt, Vector2 anchor, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    // ════════════════════════════════════════════════════════════
    //  EVENT SYSTEM
    // ════════════════════════════════════════════════════════════

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
}

/// <summary>
/// Press / hover scale feedback for a UI element. Added automatically to buttons.
/// (Lives in the same file; attached via code, so Unity won't complain about the file name.)
/// </summary>
public class UIButtonMotion : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    public float normal = 1f, hover = 1.04f, pressed = 0.94f, speed = 14f;
    private float target = 1f;

    private void OnEnable() { target = normal; transform.localScale = Vector3.one * normal; }

    private void Update()
    {
        float s = Mathf.Lerp(transform.localScale.x, target, Time.unscaledDeltaTime * speed);
        transform.localScale = new Vector3(s, s, 1f);
    }

    public void OnPointerEnter(PointerEventData e) { target = hover; }
    public void OnPointerExit (PointerEventData e) { target = normal; }
    public void OnPointerDown (PointerEventData e) { target = pressed; }
    public void OnPointerUp   (PointerEventData e) { target = hover; }
}