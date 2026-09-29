using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>
    /// Value cards: power (hero, with % of rated, fill bar and homes equivalent), wind (+ 30 s average), rotor speed,
    /// generator temperature and produced energy.
    ///
    /// Telemetry arrives at 5 Hz with sensor noise; every value glides toward the latest sample each frame with
    /// exponential smoothing (the same approach as the 3D turbine), so numbers roll instead of jumping.
    /// </summary>
    internal sealed class ValueCardsPresenter
    {
        // Average EU household: ~3,600 kWh per year = ~0.41 kW continuous.
        private const float HouseholdAverageKW = 0.41f;

        private const float ValueSmoothingTime = 0.6f;       // s; hides 5 Hz steps and noise, still feels live
        private const float TemperatureSmoothingTime = 1f;   // s; matches the 3D heat color
        private const float WindAverageTime = 30f;           // s; same window the turbine controller decides on

        private readonly TurbineSpecs specs;

        private readonly NumberLabel power;
        private readonly Label powerPercent;
        private readonly VisualElement powerFill;
        private readonly Label homes;
        private readonly NumberLabel wind;
        private readonly Label windAverageLabel;
        private readonly NumberLabel rotor;
        private readonly NumberLabel temperature;
        private readonly Label energy;

        private bool hasTelemetry;
        private TurbineTelemetry latest;
        private float smoothedPower;
        private float smoothedWind;
        private float smoothedRpm;
        private float smoothedTemperature;
        private float windAverage;

        private float shownFill = -1f;
        private int shownPercent = -1;
        private int shownHomes = -1;
        private float shownWindAverage = float.NaN;
        private float shownEnergy = float.NaN;

        public ValueCardsPresenter(VisualElement root, TurbineSpecs specs)
        {
            this.specs = specs;

            power = new NumberLabel(root.Require<Label>("power-value"), "0.00", 2);
            powerPercent = root.Require<Label>("power-percent");
            powerFill = root.Require<VisualElement>("power-fill");
            homes = root.Require<Label>("homes-label");
            wind = new NumberLabel(root.Require<Label>("wind-value"), "0.0", 1);
            windAverageLabel = root.Require<Label>("wind-aside");
            rotor = new NumberLabel(root.Require<Label>("rotor-value"), "0.0", 1);
            temperature = new NumberLabel(root.Require<Label>("temp-value"), "0.0", 1);
            energy = root.Require<Label>("energy-value");

            root.Require<Label>("power-rated").text = UiFormat.Format("rated {0:0.0} MW", specs.RatedPowerMW);
            root.Require<Label>("rotor-range").text =
                UiFormat.Format("range {0:0.#}–{1:0.#}", specs.MinRotorRpm, specs.RatedRotorRpm);
        }

        public void Show(in TurbineTelemetry telemetry)
        {
            latest = telemetry;
            if (hasTelemetry)
                return;

            // First sample: snap instead of rolling up from zero (like a particle system Prewarm).
            hasTelemetry = true;
            smoothedPower = telemetry.PowerOutputMW;
            smoothedWind = telemetry.WindSpeed;
            smoothedRpm = telemetry.RotorRpm;
            smoothedTemperature = telemetry.GeneratorTemperature;
            windAverage = telemetry.WindSpeed;
        }

        /// <summary>Called every frame with the unscaled frame time.</summary>
        public void Tick(float deltaTime)
        {
            if (!hasTelemetry)
                return;

            smoothedPower = Smooth(smoothedPower, latest.PowerOutputMW, ValueSmoothingTime, deltaTime);
            smoothedWind = Smooth(smoothedWind, latest.WindSpeed, ValueSmoothingTime, deltaTime);
            smoothedRpm = Smooth(smoothedRpm, latest.RotorRpm, ValueSmoothingTime, deltaTime);
            smoothedTemperature = Smooth(smoothedTemperature, latest.GeneratorTemperature, TemperatureSmoothingTime, deltaTime);
            windAverage = Smooth(windAverage, latest.WindSpeed, WindAverageTime, deltaTime);

            ShowPower(smoothedPower);
            wind.SetValue(smoothedWind);
            ShowWindAverage(windAverage);
            rotor.SetValue(smoothedRpm);
            temperature.SetValue(smoothedTemperature);
            ShowEnergy((float)latest.TotalEnergyMWh);
        }

        private void ShowPower(float powerMW)
        {
            power.SetValue(powerMW);

            float ratio = Mathf.Clamp01(powerMW / specs.RatedPowerMW);
            if (Mathf.Abs(ratio - shownFill) > 0.001f)
            {
                shownFill = ratio;
                powerFill.style.width = Length.Percent(ratio * 100f);
            }

            int percent = Mathf.RoundToInt(ratio * 100f);
            if (percent != shownPercent)
            {
                shownPercent = percent;
                powerPercent.text = UiFormat.Format("{0} % of rated", percent);
            }

            // Rounded so the estimate reads as an estimate and does not flicker with sensor noise.
            float households = powerMW * 1000f / HouseholdAverageKW;
            int roundTo = households >= 1000f ? 100 : 10;
            int homesRounded = Mathf.RoundToInt(households / roundTo) * roundTo;
            if (homesRounded != shownHomes)
            {
                shownHomes = homesRounded;
                homes.text = UiFormat.Format("≈ {0:N0} homes", homesRounded);
            }
        }

        private void ShowWindAverage(float average)
        {
            float rounded = Mathf.Round(average * 10f) / 10f;
            if (rounded == shownWindAverage)
                return;

            shownWindAverage = rounded;
            windAverageLabel.text = UiFormat.Format("30 s avg {0:0.0}", rounded);
        }

        private void ShowEnergy(float megawattHours)
        {
            // Fewer decimals as the total grows (a 20x demo run reaches tens of MWh).
            string format = megawattHours < 10f ? "0.00" : megawattHours < 100f ? "0.0" : "0";
            float step = megawattHours < 10f ? 0.01f : megawattHours < 100f ? 0.1f : 1f;
            float rounded = Mathf.Floor(megawattHours / step) * step;
            if (rounded == shownEnergy)
                return;

            shownEnergy = rounded;
            energy.text = rounded.ToString(format, UiFormat.Invariant);
        }

        /// <summary>Frame-rate independent exponential smoothing toward a moving target.</summary>
        private static float Smooth(float current, float target, float timeConstant, float deltaTime) =>
            current + (target - current) * (1f - Mathf.Exp(-deltaTime / timeConstant));
    }
}
