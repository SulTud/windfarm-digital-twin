using System;
using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>
    /// "Trend · last 5 min" card: feeds the selected series (power, wind or generator temperature) from the shared
    /// telemetry history to the <see cref="TrendChart"/> with a fixed, meaningful scale and reference lines.
    ///
    /// The time axis is simulation time. Between 5 Hz samples "now" is extrapolated from the observed simulation rate,
    /// so the chart scrolls smoothly even at 20x, where each sample jumps 4 s ahead.
    /// </summary>
    internal sealed class TrendChartPresenter
    {
        private enum Series
        {
            Power,
            Wind,
            Temperature,
        }

        public const double WindowSeconds = 300.0;
        private const float HeadSmoothingTime = 0.6f;     // s; same as the value cards
        private const float RateSmoothing = 0.3f;         // blend factor for the observed simulation rate

        private static readonly Func<TelemetrySample, float> PowerOf = sample => sample.PowerMW;
        private static readonly Func<TelemetrySample, float> WindOf = sample => sample.WindSpeed;
        private static readonly Func<TelemetrySample, float> TemperatureOf = sample => sample.Temperature;

        private readonly TurbineSpecs specs;
        private readonly TelemetryHistory history;
        private readonly TrendChart chart;
        private readonly Button powerTab;
        private readonly Button windTab;
        private readonly Button temperatureTab;
        private readonly Action selectPower;
        private readonly Action selectWind;
        private readonly Action selectTemperature;

        private Series series = Series.Power;
        private bool hasTelemetry;
        private TurbineTelemetry latest;
        private float headPower;
        private float headWind;
        private float headTemperature;

        private double lastSimulationTime;
        private double lastSampleStep;
        private float lastSampleRealTime;
        private double simulationRate = 1.0;             // simulation seconds per real second

        public TrendChartPresenter(VisualElement root, TurbineSpecs specs, TelemetryHistory history)
        {
            this.specs = specs;
            this.history = history;

            VisualElement container = root.Require<VisualElement>("trend-chart");
            container.Clear();                            // remove the skeleton placeholder
            container.AddToClassList("chart--live");
            chart = new TrendChart { History = history, WindowSeconds = WindowSeconds };
            container.Add(chart);

            powerTab = root.Require<Button>("trend-tab-power");
            windTab = root.Require<Button>("trend-tab-wind");
            temperatureTab = root.Require<Button>("trend-tab-temp");

            selectPower = () => Select(Series.Power);
            selectWind = () => Select(Series.Wind);
            selectTemperature = () => Select(Series.Temperature);
            powerTab.clicked += selectPower;
            windTab.clicked += selectWind;
            temperatureTab.clicked += selectTemperature;

            Select(Series.Power);
        }

        public void Dispose()
        {
            powerTab.clicked -= selectPower;
            windTab.clicked -= selectWind;
            temperatureTab.clicked -= selectTemperature;
        }

        public void Show(in TurbineTelemetry telemetry)
        {
            float realTime = Time.unscaledTime;

            if (!hasTelemetry)
            {
                hasTelemetry = true;
                headPower = telemetry.PowerOutputMW;
                headWind = telemetry.WindSpeed;
                headTemperature = telemetry.GeneratorTemperature;
            }
            else
            {
                double simulationStep = telemetry.SimulationTime - lastSimulationTime;
                float realStep = realTime - lastSampleRealTime;
                if (simulationStep >= 0.0 && realStep > 0.01f)
                {
                    simulationRate += (simulationStep / realStep - simulationRate) * RateSmoothing;
                    lastSampleStep = simulationStep;
                }
            }

            latest = telemetry;
            lastSimulationTime = telemetry.SimulationTime;
            lastSampleRealTime = realTime;
        }

        /// <summary>Called every frame with the unscaled frame time.</summary>
        public void Tick(float deltaTime)
        {
            if (!hasTelemetry)
                return;

            float blend = 1f - Mathf.Exp(-deltaTime / HeadSmoothingTime);
            headPower += (latest.PowerOutputMW - headPower) * blend;
            headWind += (latest.WindSpeed - headWind) * blend;
            headTemperature += (latest.GeneratorTemperature - headTemperature) * blend;

            // Extrapolate at most one sample step ahead, so "now" never runs past the next sample.
            double sinceSample = (Time.unscaledTime - lastSampleRealTime) * simulationRate;
            chart.Now = lastSimulationTime + Math.Min(sinceSample, lastSampleStep);
            chart.HasData = true;

            ConfigureSeries();
            chart.Refresh();
        }

        private void Select(Series selected)
        {
            series = selected;
            powerTab.EnableInClassList("tab--on", selected == Series.Power);
            windTab.EnableInClassList("tab--on", selected == Series.Wind);
            temperatureTab.EnableInClassList("tab--on", selected == Series.Temperature);
        }

        /// <summary>Fixed, meaningful scales: a chart that rescales itself hides how close a value is to its limit.</summary>
        private void ConfigureSeries()
        {
            chart.ReferenceLines.Clear();

            switch (series)
            {
                case Series.Wind:
                    // The only auto-range: from calm to storm the useful range differs by a factor of ten.
                    float maxWind = Mathf.Max(headWind, MaxInWindow(WindOf));
                    float top = Mathf.Max(15f, Mathf.Ceil(maxWind * 1.15f / 5f) * 5f);
                    chart.ValueOf = WindOf;
                    chart.HeadValue = headWind;
                    chart.Min = 0f;
                    chart.Max = top;
                    chart.TickStep = top > 20f ? 10f : 5f;
                    chart.ValueFormat = "0.0";
                    chart.Unit = "m/s";
                    chart.ReferenceLines.Add((specs.CutInWindSpeed, ReferenceLineKind.Muted));
                    chart.ReferenceLines.Add((specs.CutOutWindSpeed, ReferenceLineKind.Warning));
                    break;

                case Series.Temperature:
                    chart.ValueOf = TemperatureOf;
                    chart.HeadValue = headTemperature;
                    chart.Min = 0f;
                    chart.Max = specs.GeneratorTripTemperature + 15f;   // same range as the card scale
                    chart.TickStep = 50f;
                    chart.ValueFormat = "0.0";
                    chart.Unit = "°C";
                    chart.ReferenceLines.Add((specs.GeneratorWarningTemperature, ReferenceLineKind.Warning));
                    chart.ReferenceLines.Add((specs.GeneratorTripTemperature, ReferenceLineKind.Critical));
                    break;

                default:
                    chart.ValueOf = PowerOf;
                    chart.HeadValue = headPower;
                    chart.Min = 0f;
                    chart.Max = specs.RatedPowerMW * 1.1f;
                    chart.TickStep = 1f;
                    chart.ValueFormat = "0.00";
                    chart.Unit = "MW";
                    chart.ReferenceLines.Add((specs.RatedPowerMW, ReferenceLineKind.Accent));
                    break;
            }
        }

        private float MaxInWindow(Func<TelemetrySample, float> valueOf)
        {
            float max = 0f;
            for (int i = history.LowerBound(chart.Now - WindowSeconds); i < history.Count; i++)
                max = Mathf.Max(max, valueOf(history[i]));
            return max;
        }
    }
}
