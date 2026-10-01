using UnityEditor;
using UnityEngine;

namespace WindFarm.EditorTools
{
    /// <summary>
    /// Saves the Game view, dashboard included, as the source of the WebGL page's link preview image (Open Graph
    /// og:image, 1200 x 630, referenced by the WindFarm template's index.html). Captured at 1920 x 1008 (same aspect),
    /// where the full PC layout applies, and scaled down afterwards: at 1200 x 630 the dashboard switches to its narrow
    /// layout and the picture looks cramped. Set a fixed 1920 x 1008 Game view (any scale), frame the shot in Play
    /// mode, then use WindFarm -> Capture Link Preview Image. The file lands in the project's Temp folder (not shipped).
    /// </summary>
    internal static class PreviewImageCapture
    {
        private const string Path = "Temp/preview-source.png";
        private const int ExpectedWidth = 1920;
        private const int ExpectedHeight = 1008;

        [MenuItem("WindFarm/Capture Link Preview Image")]
        private static void Capture()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Capture Link Preview Image: enter Play mode first (the dashboard draws only then).");
                return;
            }

            if (Screen.width != ExpectedWidth || Screen.height != ExpectedHeight)
                Debug.LogWarning($"Capture Link Preview Image: the Game view is {Screen.width} x {Screen.height}; " +
                                 $"use a fixed {ExpectedWidth} x {ExpectedHeight} resolution.");

            ScreenCapture.CaptureScreenshot(Path);
            Debug.Log($"Link preview source saved -> {Path} (written at the end of the frame).");
        }
    }
}
