using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    /// <summary>
    /// First-visit hint over the scene: how to move the camera and which button starts the fault scenario (most
    /// visitors would otherwise leave without finding the strongest moment of the demo). Shown on every page load,
    /// hidden at the first press anywhere or after <see cref="ShowSeconds"/>; fades with a USS transition.
    /// Two quiet lines of equal weight next to the button they name (level with the FAULT button on phones, above the
    /// dock on PC); the text follows the layout: pinch and FAULT on phones, scroll and FAN FAILURE on PC. The fault
    /// line says to raise the wind first (user feedback): at the default 9 m/s and 1x a fan fault takes 3-6 minutes
    /// to warn, so a visitor pressing it at once would see nothing happen.
    /// </summary>
    internal sealed class HintPresenter
    {
        private const float ShowSeconds = 12f;
        private const float MaxTickDelta = 0.1f;    // loading hitches must not eat the display time
        private const string VisibleClass = "hint--visible";

        private readonly VisualElement root;
        private readonly VisualElement hint;
        private readonly Label gesture;
        private readonly Label action;
        private float remaining = ShowSeconds;
        private bool shown;
        private bool? shownPortrait;

        public HintPresenter(VisualElement root)
        {
            this.root = root;
            hint = root.Require<VisualElement>("hint");
            gesture = root.Require<Label>("hint-gesture");
            action = root.Require<Label>("hint-action");
            root.RegisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(HandleWheel, TrickleDown.TrickleDown);
        }

        public void Dispose()
        {
            root.UnregisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<WheelEvent>(HandleWheel, TrickleDown.TrickleDown);
            Hide();
        }

        /// <summary>Called every frame with the unscaled frame time.</summary>
        public void Tick(float deltaTime)
        {
            if (remaining <= 0f)
                return;

            bool portrait = root.ClassListContains(ResponsivePanel.PortraitClass);
            if (shownPortrait != portrait)
            {
                shownPortrait = portrait;
                gesture.text = portrait ? "Drag to orbit · Pinch to zoom" : "Drag to orbit · Scroll to zoom";
                // In that order: at the default wind a fan fault needs minutes to show; above rated it plays in one.
                action.text = portrait ? "Raise the wind, then tap FAULT" : "Raise the wind, then try FAN FAILURE";
            }

            if (!shown)
            {
                shown = true;
                hint.AddToClassList(VisibleClass);
            }

            remaining -= Mathf.Min(deltaTime, MaxTickDelta);
            if (remaining <= 0f)
                Hide();
        }

        private void HandlePointerDown(PointerDownEvent evt) => Hide();

        private void HandleWheel(WheelEvent evt) => Hide();

        private void Hide()
        {
            remaining = 0f;
            hint.RemoveFromClassList(VisibleClass);
        }
    }
}
