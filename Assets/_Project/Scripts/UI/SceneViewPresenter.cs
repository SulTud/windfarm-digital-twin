using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Cameras;

namespace WindFarm.UI
{
    /// <summary>
    /// Connects the dashboard to the orbit camera:
    ///   - reports the free scene area (scene-space, minus the dock on PC) as the rectangle the turbine is framed into,
    ///     every frame, so layout switches, rotation and safe-area changes are followed without extra events;
    ///   - owns the scene gestures (orbit, zoom, double tap);
    ///   - counts any press or wheel anywhere on the dashboard as activity, so the idle sway waits while the user
    ///     works the controls too.
    /// </summary>
    internal sealed class SceneViewPresenter
    {
        private const float DockGap = 8f;   // logical px kept free above the PC dock

        private readonly VisualElement root;
        private readonly VisualElement sceneSpace;
        private readonly VisualElement dock;
        private readonly TurbineOrbitCamera orbitCamera;
        private readonly CameraGestures gestures;

        public SceneViewPresenter(VisualElement root, TurbineOrbitCamera orbitCamera)
        {
            this.root = root;
            this.orbitCamera = orbitCamera;
            sceneSpace = root.Require<VisualElement>("scene-space");
            dock = root.Require<VisualElement>("dock");

            VisualElement surface = root.Require<VisualElement>("scene-input");
            if (surface != null)
                gestures = new CameraGestures(surface, orbitCamera);

            // TrickleDown: seen on the way down, before any control handles (and possibly stops) the event.
            root.RegisterCallback<PointerDownEvent>(HandleActivity, TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(HandleActivity, TrickleDown.TrickleDown);
        }

        public void Dispose()
        {
            gestures?.Dispose();
            root.UnregisterCallback<PointerDownEvent>(HandleActivity, TrickleDown.TrickleDown);
            root.UnregisterCallback<WheelEvent>(HandleActivity, TrickleDown.TrickleDown);
        }

        public void Tick()
        {
            if (sceneSpace == null || dock == null)
                return;

            Rect panel = root.worldBound;
            Rect area = sceneSpace.worldBound;
            if (float.IsNaN(panel.width) || float.IsNaN(area.width) || panel.width <= 0f || panel.height <= 0f)
                return;

            // On PC the dock sits at the bottom of the scene space; the turbine is framed above it.
            float bottom = area.yMax;
            if (dock.parent == sceneSpace)
                bottom = Mathf.Min(bottom, dock.worldBound.yMin - DockGap);

            // Panel space has its origin top-left with y down; the camera viewport is bottom-left with y up.
            orbitCamera.SetViewport(Rect.MinMaxRect(
                (area.xMin - panel.xMin) / panel.width,
                1f - (bottom - panel.yMin) / panel.height,
                (area.xMax - panel.xMin) / panel.width,
                1f - (area.yMin - panel.yMin) / panel.height));
        }

        private void HandleActivity(EventBase evt) => orbitCamera.NotifyUserInput();
    }
}
