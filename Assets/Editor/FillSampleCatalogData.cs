#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > AR Furniture > Fill Sample Catalog Data
/// Fills EMPTY catalog fields (category, room, rating, reviews, price, colors) on every item in
/// the FurnitureDatabase so the Home screen and catalog sheet have something to show.
/// Never overwrites a value you already entered. Replace the sample values with real ones.
/// </summary>
public static class FillSampleCatalogData
{
    [MenuItem("Tools/AR Furniture/Fill Sample Catalog Data")]
    public static void Fill()
    {
        FurnitureDatabase db = Resources.Load<FurnitureDatabase>("FurnitureDatabase");
        if (db == null) { Debug.LogError("FurnitureDatabase not found in a Resources folder."); return; }

        int changed = 0;
        foreach (FurnitureItem it in db.items)
        {
            if (it == null) continue;
            string n = (it.furnitureName ?? it.name).ToLowerInvariant();
            uint h = Hash(n);

            if (string.IsNullOrEmpty(it.category)) it.category = GuessCategory(n);
            if (string.IsNullOrEmpty(it.room))     it.room     = GuessRoom(n);
            if (it.rating <= 0f)  it.rating = 4f + (h % 3) * 0.5f;                 // 4, 4.5, 5
            if (it.reviewCount <= 0) it.reviewCount = 20 + (int)(h % 280);
            if (it.price <= 0)
            {
                it.price = (500 + (int)(h % 40) * 50) * 1000;                     // 500.000 - 2.450.000
                if (it.originalPrice <= 0 && h % 2 == 0) it.originalPrice = it.price / 100 * 120 / 1000 * 1000;
            }
            if (it.colors == null || it.colors.Length == 0) it.colors = new[] { "Natural", "Cream" };

            EditorUtility.SetDirty(it);
            changed++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[FillSampleCatalogData] Filled empty catalog fields on " + changed + " items.");
    }

    private static string GuessCategory(string n)
    {
        if (n.Contains("chair")) return "Chair";
        if (n.Contains("table")) return "Table";
        if (n.Contains("shelf") || n.Contains("wardrobe") || n.Contains("vanity")) return "Shelves";
        if (n.Contains("bed")) return "Bed";
        if (n.Contains("stove") || n.Contains("oven") || n.Contains("microwave") ||
            n.Contains("exhaust") || n.Contains("refrigerator")) return "Kitchen";
        if (n.Contains("bath")) return "Bathroom";
        return "";
    }

    private static string GuessRoom(string n)
    {
        if (n.Contains("bed") || n.Contains("wardrobe")) return "Bedroom";
        if (n.Contains("stove") || n.Contains("oven") || n.Contains("microwave") ||
            n.Contains("exhaust") || n.Contains("refrigerator")) return "Kitchen";
        if (n.Contains("bath") || n.Contains("vanity")) return "Bathroom";
        return "Living Room";
    }

    private static uint Hash(string s)   // stable across runs (string.GetHashCode is not)
    {
        uint h = 5381;
        foreach (char c in s) h = h * 33 + c;
        return h;
    }
}
#endif
