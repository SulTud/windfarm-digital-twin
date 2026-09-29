using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>
    /// "Power curve" card: the datasheet curve from the specs, the last two minutes of measured samples and the live
    /// operating point (smoothed like the value cards). Shares the telemetry history with the trend chart.
    /// </summary>
    internal sealed class PowerCurvePresenter
    {
        private const float LiveSmoothingTime = 0.6f;   // s; same as the value cards

        private readonly PowerCurveChart chart;

        private bool hasTelemetry;
        private TurbineTelemetry latest;
        private float liveWind;
        private float livePower;

        public PowerCurvePresenter(VisualElement root, TurbineSpecs specs, TelemetryHistory history)
        {
            VisualElement container = root.Require<VisualElement>("curve-chart");
            container.Clear();                            // remove the skeleton placeholder
            container.AddToClassList("chart--live");
            chart = new PowerCurveChart { Specs = specs, History = history };
            container.Add(chart);
        }

        public void Show(in TurbineTelemetry telemetry)
        {
            latest = telemetry;
            if (hasTelemetry)
                return;

            hasTelemetry = true;
            liveWind = telemetry.WindSpeed;
            livePower = telemetry.PowerOutputMW;
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
