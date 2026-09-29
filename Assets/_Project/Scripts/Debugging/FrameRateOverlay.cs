using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.Debugging
{
    /// <summary>
    /// Small frame-rate readout on top of the dashboard, for measuring on real devices (a WebGL build on a phone has
    /// no profiler at hand). Shows the average FPS and the slowest frame of the last half second: stutter shows up in
    /// the slowest frame long before it moves the average. Disable or remove the component to hide it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class FrameRateOverlay : MonoBehaviour
    {
        private const float SampleWindow = 0.5f;   // s between readout updates

        private Label label;
        private float windowTime;
        private int windowFrames;
        private float slowestFrame;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;

            label = new Label("-- fps") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("fps-overlay");
            root.Add(label);   // last child: drawn on top of the dashboard
            ResetWindow();
        }

        private void OnDisable()
        {
            label?.RemoveFromHierarchy();
            label = null;
        }

        private void Update()
        {
            if (label == null)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            windowTime += deltaTime;
            windowFrames++;
            slowestFrame = Mathf.Max(slowestFrame, deltaTime);
            if (windowTime < SampleWindow)
                return;

            label.text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:0} fps · worst {1:0} ms", windowFrames / windowTime, slowestFrame * 1000f);
            ResetWindow();
        }

        private void ResetWindow()
        {
            windowTime = 0f;
            windowFrames = 0;
            slowestFrame = 0f;
        }
    }
}
