using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARPlaneToggle : MonoBehaviour
{
    [Header("References")]
    public ARPlaneManager arPlaneManager;
    public Button planeToggleButton;

    [Header("Plane Filtering")]
    [Tooltip("Planes smaller than this area (m²) are hidden AND blocked from placement.")]
    public float minimumPlaneArea = 0.5f;

    [Header("Button Colors")]
    public Color activeColor   = Color.white;
    public Color inactiveColor = new Color(1, 1, 1, 0.3f);

    // List of plane trackable IDs that pass the size filter
    // FurnitureDragController checks this to allow/block placement
    public static HashSet<UnityEngine.XR.ARSubsystems.TrackableId> ValidPlaneIds
        = new HashSet<UnityEngine.XR.ARSubsystems.TrackableId>();

    private bool planesVisible = true;

    private void Start()
    {
        arPlaneManager.requestedDetectionMode =
            PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;

        planeToggleButton.onClick.AddListener(Toggle);
        UpdateButtonColor();
        StartCoroutine(ContinuousFilterCheck());
    }

    private void OnEnable()
    {
        if (arPlaneManager != null)
            arPlaneManager.trackablesChanged.AddListener(OnPlanesChanged);
    }

    private void OnDisable()
    {
        if (arPlaneManager != null)
            arPlaneManager.trackablesChanged.RemoveListener(OnPlanesChanged);
    }

    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        foreach (var plane in args.added)   ApplyToPlane(plane);
        foreach (var plane in args.updated) ApplyToPlane(plane);

        foreach (var plane in args.removed)
            ValidPlaneIds.Remove(plane.Key);
    }

    private IEnumerator ContinuousFilterCheck()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);
            ApplyToAllPlanes();
        }
    }

    private void Toggle()
    {
        planesVisible = !planesVisible;
        ApplyToAllPlanes();
        StartCoroutine(ReapplyAfterFrames());
        UpdateButtonColor();
    }

    private IEnumerator ReapplyAfterFrames()
    {
        yield return null;
        ApplyToAllPlanes();
        yield return null;
        ApplyToAllPlanes();
        yield return new WaitForSeconds(0.2f);
        ApplyToAllPlanes();
    }

    private void ApplyToAllPlanes()
    {
        foreach (var plane in arPlaneManager.trackables)
            ApplyToPlane(plane);
    }

    private void ApplyToPlane(ARPlane plane)
    {
        if (plane == null) return;

        bool validSize = IsValidSize(plane);

        // Update valid plane IDs — used by FurnitureDragController
        // Valid = passes size filter regardless of toggle state
        if (validSize)
            ValidPlaneIds.Add(plane.trackableId);
        else
            ValidPlaneIds.Remove(plane.trackableId);

        // Visual — hide if toggle is off OR size is invalid
        bool shouldShow = planesVisible && validSize;

        ARPlaneMeshVisualizer visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
        if (visualizer != null) visualizer.enabled = shouldShow;

        MeshRenderer mr = plane.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = shouldShow;

        LineRenderer lr = plane.GetComponent<LineRenderer>();
        if (lr != null) lr.enabled = shouldShow;

        foreach (Renderer r in plane.GetComponentsInChildren<Renderer>(true))
            r.enabled = shouldShow;
    }

    private bool IsValidSize(ARPlane plane)
    {
        float area = plane.size.x * plane.size.y;
        if (area < minimumPlaneArea) return false;
        if (plane.subsumedBy != null) return false;
        if (plane.trackingState != TrackingState.Tracking) return false;
        return true;
    }

    private void UpdateButtonColor()
    {
        Image icon = planeToggleButton.GetComponent<Image>();
        if (icon != null)
            icon.color = planesVisible ? activeColor : inactiveColor;
    }
}
