using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to any UI panel that should block touches/clicks
/// from passing through to the AR scene behind it.
/// Automatically adds an invisible Image that acts as a raycast blocker.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BlockRaycast : MonoBehaviour
{
    private void Awake()
    {
        // If no Image exists, add one as a blocker
        Image img = GetComponent<Image>();
        if (img == null)
        {
            img = gameObject.AddComponent<Image>();
            img.color = Color.clear; // invisible but blocks raycasts
        }

        // Always ensure raycast target is on
        img.raycastTarget = true;
    }
}
