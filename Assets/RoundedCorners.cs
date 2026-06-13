using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generates a rounded rectangle sprite at runtime.
/// You can choose which corners to round individually.
/// </summary>
[RequireComponent(typeof(Image))]
public class RoundedCorners : MonoBehaviour
{
    [Range(0, 100)] public int cornerRadius = 20;
    public int textureSize = 256;

    [Header("Which corners to round")]
    public bool topLeft     = true;
    public bool topRight    = true;
    public bool bottomLeft  = true;
    public bool bottomRight = true;

    private void Start()
    {
        Apply();
    }

    public void Apply()
    {
        GetComponent<Image>().sprite = CreateSprite();
    }

    private Sprite CreateSprite()
    {
        int w = textureSize;
        int h = textureSize;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                pixels[y * w + x] = IsInside(x, y, w, h) ? Color.white : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(
            tex,
            new Rect(0, 0, w, h),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(cornerRadius, cornerRadius, cornerRadius, cornerRadius)
        );
    }

    private bool IsInside(int x, int y, int w, int h)
    {
        int r = cornerRadius;

        // Corners: determine which corner zone this pixel is in
        bool inLeft   = x < r;
        bool inRight  = x > w - r - 1;
        bool inBottom = y < r;
        bool inTop    = y > h - r - 1;

        // Top-left corner
        if (inTop && inLeft)
        {
            if (!topLeft) return true; // square corner
            return Vector2.Distance(new Vector2(x, y), new Vector2(r, h - r)) <= r;
        }

        // Top-right corner
        if (inTop && inRight)
        {
            if (!topRight) return true;
            return Vector2.Distance(new Vector2(x, y), new Vector2(w - r, h - r)) <= r;
        }

        // Bottom-left corner
        if (inBottom && inLeft)
        {
            if (!bottomLeft) return true;
            return Vector2.Distance(new Vector2(x, y), new Vector2(r, r)) <= r;
        }

        // Bottom-right corner
        if (inBottom && inRight)
        {
            if (!bottomRight) return true;
            return Vector2.Distance(new Vector2(x, y), new Vector2(w - r, r)) <= r;
        }

        // Everything else is inside
        return true;
    }
}
