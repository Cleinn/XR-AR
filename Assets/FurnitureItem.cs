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
}
