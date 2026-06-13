using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FurnitureDatabase", menuName = "AR Furniture/Furniture Database")]
public class FurnitureDatabase : ScriptableObject
{
    public List<FurnitureItem> items = new List<FurnitureItem>();
}
