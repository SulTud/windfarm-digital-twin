using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>
    /// "Power curve" card: the datasheet curve from the specs, the live operating point (smoothed like the value cards)
    /// and the trail it leaves behind.
    ///
    /// The trail records where the live point was each time a sample arrived, not the raw samples: raw samples appear
    /// at their target at once and the smoothed point reaches them only later, so dots popped up ahead of the point.
    /// Now every dot comes out from under the point. It is also closer to real practice: field power curves are drawn
    /// from averaged values (IEC 61400-12 uses 10 min averages), not from raw readings. The spread around the curve
    /// still shows the rotor lag, which is physics, not noise.
    /// </summary>
    internal sealed class PowerCurvePresenter
    {
        private const float LiveSmoothingTime = 0.6f;      // s; same as the value cards
        private const double TrailSeconds = 300.0;          // simulation time; same window as the trend chart
        private const double TrailSpacing = 0.5;            // simulation seconds between trail dots (~600 per window)

        private readonly PowerCurveChart chart;
        private readonly TelemetryHistory trail = new TelemetryHistory(1024, TrailSpacing);

        private bool hasTelemetry;
        private TurbineTelemetry latest;
        private float liveWind;
        private float livePower;

        public PowerCurvePresenter(VisualElement root, TurbineSpecs specs)
        {
            VisualElement container = root.Require<VisualElement>("curve-chart");
            container.Clear();                            // remove the skeleton placeholder
            container.AddToClassList("chart--live");
            chart = new PowerCurveChart { Specs = specs, Trail = trail, TrailSeconds = TrailSeconds };
            container.Add(chart);
        }

        public void Show(in TurbineTelemetry telemetry)
        {
            latest = telemetry;
            if (!hasTelemetry)
            {
                hasTelemetry = true;
                liveWind = telemetry.WindSpeed;
                livePower = telemetry.PowerOutputMW;
            }

            // Drop a dot where the live point is now; it moves on toward this sample over the next frames.
            trail.Add(new TelemetrySample(telemetry.SimulationTime, livePower, liveWind, telemetry.GeneratorTemperature));
        }

        /// <summary>Called every frame with the unscaled frame time.</summary>
        public void Tick(float deltaTime)
        {
            if (!hasTelemetry)
                return;

            float blend = 1f - Mathf.Exp(-deltaTime / LiveSmoothingTime);
            liveWind += (latest.WindSpeed - liveWind) * blend;
            livePower += (latest.PowerOutputMW - livePower) * blend;

            chart.LiveWind = liveWind;
            chart.LivePower = livePower;
            chart.Now = latest.SimulationTime;
            chart.HasData = true;
            chart.Refresh();
        }
    }
}
