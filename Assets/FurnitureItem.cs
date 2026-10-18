using UnityEngine;

[CreateAssetMenu(fileName = "NewFurnitureItem", menuName = "AR Furniture/Furniture Item")]
public class FurnitureItem : ScriptableObject
{
    public string furnitureName;
    public Sprite icon;
    public GameObject prefab;

    [Tooltip("Moves the object up after placement. " +
             "Use this if the object sinks into the floor. " +
             "Set to half the object's real-world height in meters. " +
             "Example: a 20cm tall can = 0.1")]
    public float yOffset = 0.1f;

    [Header("Catalog Info (shown on the Home screen and catalog sheet)")]
    [Tooltip("Category chip on the Home screen, e.g. Chair, Table, Shelves. Items with an empty category only appear under \"All\".")]
    public string category = "";

    [Tooltip("Room tag, e.g. \"Living Room\". Leave empty to hide.")]
    public string room = "";

    [Tooltip("Star rating, e.g. 5 or 4.5. 0 hides the rating and the star.")]
    [Range(0f, 5f)] public float rating = 0f;

    [Tooltip("Number of reviews. 0 hides the \"(N Reviews)\" label.")]
    public int reviewCount = 0;

    [Tooltip("Sale price in Rupiah, e.g. 2750000. 0 hides the price.")]
    public int price = 0;

    [Tooltip("Original price in Rupiah, drawn struck-through. 0 (or <= price) hides it.")]
    public int originalPrice = 0;

    [Tooltip("Color names for the dropdown, e.g. Cream, Walnut. Empty shows \"Default\".")]
    public string[] colors = new string[0];

    [Tooltip("Optional link opened by the Buy Now button.")]
    public string buyUrl = "";
}
