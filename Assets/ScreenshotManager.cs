using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Captures the AR scene and saves to device gallery.
/// Uses MediaStore API on Android (API 29+) — no permissions needed.
/// Falls back to file storage on older Android.
/// </summary>
public class ScreenshotManager : MonoBehaviour
{
    [Header("UI to hide during capture")]
    public GameObject[] uiElementsToHide;

    [Header("Flash feedback (optional)")]
    public Image flashOverlay;
    public float flashDuration = 0.3f;

    [Header("Save settings")]
    public string albumName = "AR Furniture";

    // ─────────────────────────────────────────────
    // Public entry point
    // ─────────────────────────────────────────────

    public void TakeScreenshot()
    {
        StartCoroutine(CaptureRoutine());
    }

    // ─────────────────────────────────────────────
    // Capture
    // ─────────────────────────────────────────────

    private IEnumerator CaptureRoutine()
    {
        SetUIVisible(false);
        yield return new WaitForEndOfFrame();

        Texture2D screenshot = new Texture2D(
            Screen.width, Screen.height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenshot.Apply();

        SetUIVisible(true);

        if (flashOverlay != null)
            StartCoroutine(PlayFlash());

        byte[] pngBytes = screenshot.EncodeToPNG();
        Destroy(screenshot);

        string fileName = "ARFurniture_" +
                          System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

        SaveImage(pngBytes, fileName);
    }

    // ─────────────────────────────────────────────
    // Save
    // ─────────────────────────────────────────────

    private void SaveImage(byte[] pngBytes, string fileName)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        SaveToAndroidGallery(pngBytes, fileName);
#elif UNITY_IOS && !UNITY_EDITOR
        SaveToiOSGallery(pngBytes, fileName);
#else
        // Editor fallback
        string path = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, fileName);
        File.WriteAllBytes(path, pngBytes);
        Debug.Log("[Screenshot] Saved to: " + path);
#endif
    }

    // ─────────────────────────────────────────────
    // Android — uses MediaStore (API 29+, no permission needed)
    // Falls back to legacy storage for older devices
    // ─────────────────────────────────────────────

#if UNITY_ANDROID && !UNITY_EDITOR
    private void SaveToAndroidGallery(byte[] pngBytes, string fileName)
    {
        try
        {
            // Write to temp cache first
            string tempPath = Path.Combine(Application.temporaryCachePath, fileName);
            File.WriteAllBytes(tempPath, pngBytes);

            // Use MediaStore to insert into gallery (API 29+, no permission needed)
            AndroidJavaClass  mediaStore   = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
            AndroidJavaObject context      = GetAndroidContext();
            AndroidJavaObject resolver     = context.Call<AndroidJavaObject>("getContentResolver");

            AndroidJavaObject contentValues = new AndroidJavaObject("android.content.ContentValues");

            contentValues.Call("put", "title",               fileName);
            contentValues.Call("put", "display_name",        fileName);
            contentValues.Call("put", "mime_type",           "image/png");
            contentValues.Call("put", "relative_path",       "Pictures/" + albumName);
            contentValues.Call("put", "date_added",
                System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            contentValues.Call("put", "date_taken",
                System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            AndroidJavaObject externalUri = mediaStore.GetStatic<AndroidJavaObject>(
                "EXTERNAL_CONTENT_URI");

            AndroidJavaObject uri = resolver.Call<AndroidJavaObject>(
                "insert", externalUri, contentValues);

            if (uri != null)
            {
                // Write bytes to the MediaStore URI
                AndroidJavaObject outputStream = resolver.Call<AndroidJavaObject>(
                    "openOutputStream", uri);

                if (outputStream != null)
                {
                    outputStream.Call("write", pngBytes);
                    outputStream.Call("flush");
                    outputStream.Call("close");
                    Debug.Log("[Screenshot] Saved to gallery: " + uri.Call<string>("toString"));
                }
                else
                {
                    Debug.LogError("[Screenshot] Could not open output stream");
                    SaveToLegacyStorage(pngBytes, fileName);
                }
            }
            else
            {
                Debug.LogWarning("[Screenshot] MediaStore insert returned null — trying legacy");
                SaveToLegacyStorage(pngBytes, fileName);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Screenshot] MediaStore failed: " + e.Message);
            SaveToLegacyStorage(pngBytes, fileName);
        }
    }

    private void SaveToLegacyStorage(byte[] pngBytes, string fileName)
    {
        try
        {
            // Legacy path for Android API < 29
            string picturesPath = Path.Combine(
                "/sdcard/Pictures", albumName);

            if (!Directory.Exists(picturesPath))
                Directory.CreateDirectory(picturesPath);

            string savePath = Path.Combine(picturesPath, fileName);
            File.WriteAllBytes(savePath, pngBytes);

            // Notify gallery to scan the new file
            AndroidJavaClass  mediaScannerConnection =
                new AndroidJavaClass("android.media.MediaScannerConnection");
            AndroidJavaObject context = GetAndroidContext();
            mediaScannerConnection.CallStatic("scanFile",
                context, new string[] { savePath }, null, null);

            Debug.Log("[Screenshot] Saved (legacy): " + savePath);
        }
        catch (System.Exception e)
        {
            // Final fallback — persistent data path
            string fallback = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllBytes(fallback, pngBytes);
            Debug.LogWarning("[Screenshot] Saved to app storage (not gallery): "
                + fallback + " | Error was: " + e.Message);
        }
    }

    private AndroidJavaObject GetAndroidContext()
    {
        AndroidJavaClass  unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        return activity;
    }
#endif

    // ─────────────────────────────────────────────
    // iOS
    // ─────────────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void SaveImageToGallery(byte[] data, int length);

    private void SaveToiOSGallery(byte[] pngBytes, string fileName)
    {
        try
        {
            // Write to temp then save to iOS photo library
            string tempPath = Path.Combine(Application.temporaryCachePath, fileName);
            File.WriteAllBytes(tempPath, pngBytes);

            // Use NativeGallery if available, otherwise log path
            #if NATIVE_GALLERY
                NativeGallery.SaveImageToGallery(tempPath, albumName, fileName,
                    (success, path) => Debug.Log(success
                        ? "[Screenshot] iOS saved: " + path
                        : "[Screenshot] iOS save failed: " + path));
            #else
                Debug.Log("[Screenshot] iOS: saved to temp — install NativeGallery to save to Photos. Path: " + tempPath);
            #endif
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Screenshot] iOS save failed: " + e.Message);
        }
    }
#endif

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private void SetUIVisible(bool visible)
    {
        foreach (GameObject ui in uiElementsToHide)
            if (ui != null) ui.SetActive(visible);
    }

    private IEnumerator PlayFlash()
    {
        if (flashOverlay == null) yield break;
        flashOverlay.gameObject.SetActive(true);
        float half = flashDuration * 0.5f;

        for (float t = 0; t < half; t += Time.deltaTime)
        {
            flashOverlay.color = new Color(1, 1, 1, Mathf.Lerp(0, 0.9f, t / half));
            yield return null;
        }
        for (float t = 0; t < half; t += Time.deltaTime)
        {
            flashOverlay.color = new Color(1, 1, 1, Mathf.Lerp(0.9f, 0, t / half));
            yield return null;
        }
        flashOverlay.color = new Color(1, 1, 1, 0);
        flashOverlay.gameObject.SetActive(false);
    }
}
