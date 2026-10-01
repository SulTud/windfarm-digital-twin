using UnityEngine;

namespace WindFarm.Visuals
{
    /// <summary>
    /// Picks the quality level for the device at startup. WebGL has one default level for every browser
    /// (Mobile: render scale 0.8, one shadow cascade, hard shadows), so desktop browsers switch to the PC level.
    /// Runs before the first scene loads, because <see cref="SceneEnvironment"/> caches the active URP asset
    /// in OnEnable. The Editor keeps the level chosen in Project Settings.
    /// </summary>
    public static class PlatformQuality
    {
        private const string DesktopLevelName = "PC";
        private const string MobileLevelName = "Mobile";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SelectQualityLevel()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string wanted = Application.isMobilePlatform ? MobileLevelName : DesktopLevelName;
            int level = System.Array.IndexOf(QualitySettings.names, wanted);
            if (level < 0)
            {
                Debug.LogWarning($"[PlatformQuality] Quality level '{wanted}' not found -> keeping '{QualitySettings.names[QualitySettings.GetQualityLevel()]}'.");
                return;
            }

            if (level != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(level, true);

            Debug.Log($"[PlatformQuality] {(Application.isMobilePlatform ? "Mobile" : "Desktop")} browser -> quality level '{wanted}'.");
#endif
        }
    }
}
