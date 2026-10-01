using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace WindFarm.UI
{
    /// <summary>
    /// Makes a button copy a text to the clipboard and confirm it with "COPIED" on the button for a moment. Used for
    /// the e-mail address on desktops, where a mailto: link often opens an empty browser tab (no mail app set up;
    /// webmail users). In WebGL the copy runs on the pointer release inside the browser's user gesture
    /// (ExternalLinks.jslib); elsewhere through <see cref="GUIUtility.systemCopyBuffer"/>.
    /// </summary>
    internal sealed class CopyButton
    {
        private const float ConfirmSeconds = 2f;
        private const string ConfirmText = "COPIED";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void WindFarm_CopyOnRelease(string text);
#endif

        private readonly Button button;
        private readonly string text;
        private readonly string label;
        private float confirmRemaining;

        public CopyButton(Button button, string text)
        {
            this.button = button;
            this.text = text;
            label = button.text;

            bool hasText = !string.IsNullOrEmpty(text);
            button.SetEnabled(hasText);
            if (!hasText)
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            button.RegisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
#endif
            button.clicked += Confirm;
        }

        public void Dispose()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            button.UnregisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
#endif
            button.clicked -= Confirm;
            button.text = label;
        }

        /// <summary>Called every frame with the unscaled frame time (restores the label after the confirmation).</summary>
        public void Tick(float deltaTime)
        {
            if (confirmRemaining <= 0f)
                return;

            confirmRemaining -= deltaTime;
            if (confirmRemaining <= 0f)
                button.text = label;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void HandlePointerDown(PointerDownEvent evt) => WindFarm_CopyOnRelease(text);
#endif

        private void Confirm()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            GUIUtility.systemCopyBuffer = text;
#endif
            button.text = ConfirmText;
            confirmRemaining = ConfirmSeconds;
        }
    }
}
