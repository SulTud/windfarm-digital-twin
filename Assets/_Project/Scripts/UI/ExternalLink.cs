using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace WindFarm.UI
{
    /// <summary>
    /// Makes a button open an external URL (web page or mailto:). In WebGL the URL is handed to the page on pointer
    /// down and opened on the release, inside the browser's user gesture (ExternalLinks.jslib): Application.OpenURL
    /// runs a frame later, outside the gesture, and popup blockers (iOS Safari) may swallow it. Elsewhere the click
    /// calls Application.OpenURL.
    /// </summary>
    internal sealed class ExternalLink
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void WindFarm_OpenUrlOnRelease(string url);
#endif

        private readonly Button button;
        private readonly string url;

        public ExternalLink(Button button, string url)
        {
            this.button = button;
            this.url = url;

            bool hasUrl = !string.IsNullOrEmpty(url);
            button.SetEnabled(hasUrl);
            if (!hasUrl)
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            // TrickleDown: a Button captures the pointer on press; we only need to know that a press started.
            button.RegisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
#else
            button.clicked += Open;
#endif
        }

        public void Dispose()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            button.UnregisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
#else
            button.clicked -= Open;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void HandlePointerDown(PointerDownEvent evt) => WindFarm_OpenUrlOnRelease(url);
#else
        private void Open() => Application.OpenURL(url);
#endif
    }
}
