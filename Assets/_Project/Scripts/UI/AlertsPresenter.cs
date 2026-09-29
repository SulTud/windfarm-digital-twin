using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Simulation;

namespace WindFarm.UI
{
    internal enum GeneratorTemperatureLevel
    {
        Normal,
        Warning,
        Alarm,
        Trip,
    }

    /// <summary>
    /// Generator temperature status and the notification banner.
    ///   - Chip (NORMAL / WARNING / ALARM / TRIP) and card border tint from the protection limits in the specs.
    ///   - Scale: zone widths from the limits, marker follows the smoothed temperature.
    ///   - Banner: the most important active event in plain language (trip > alarm > storm shutdown > warning).
    ///
    /// Levels use hysteresis like the turbine controller: a level is left only 2 °C below its limit, so sensor noise
    /// at a threshold does not flicker the chip. The derate / trip actions themselves arrive with fault injection.
    /// </summary>
    internal sealed class AlertsPresenter
    {
        private const float LevelHysteresis = 2f;             // °C
        private const float ScaleHeadroom = 15f;              // °C shown above the trip limit
        private const float MarkerSmoothingTime = 1f;         // s; matches the temperature value and the 3D heat color
        private const string BannerClass = "dashboard--banner";

        private static readonly string[] ChipClasses = { "chip--good", "chip--warn", "chip--crit" };
        private static readonly string[] CardClasses = { "card--warn", "card--crit" };
        private static readonly string[] NotificationClasses = { "notification--warn", "notification--crit" };

        private readonly VisualElement root;
        private readonly TurbineSpecs specs;
        private readonly Label chip;
        private readonly VisualElement card;
        private readonly VisualElement marker;
        private readonly VisualElement notification;
        private readonly Label notificationTitle;
        private readonly Label notificationMessage;
        private readonly VisualElement zoneNormal;
        private readonly VisualElement zoneWarning;
        private readonly VisualElement zoneAlarm;
        private readonly VisualElement zoneTrip;
        private readonly Label limitsLabel;
        private readonly Label tripLabel;

        private float scaleMax;
        private float shownWarning = float.NaN;
        private float shownAlarm = float.NaN;
        private float shownTrip = float.NaN;

        private bool hasTelemetry;
        private TurbineTelemetry latest;
        private GeneratorTemperatureLevel level;
        private float smoothedTemperature;
        private float shownMarker = -1f;

        private string shownBannerKey;
        private int shownBannerTemperature = int.MinValue;

        public AlertsPresenter(VisualElement root, TurbineSpecs specs)
        {
            this.root = root;
            this.specs = specs;

            chip = root.Require<Label>("temp-chip");
            card = root.Require<VisualElement>("temp-card");
            marker = root.Require<VisualElement>("temp-marker");
            notification = root.Require<VisualElement>("notification");
            notificationTitle = root.Require<Label>("notification-title");
            notificationMessage = root.Require<Label>("notification-message");

            zoneNormal = root.Require<VisualElement>("temp-zone-normal");
            zoneWarning = root.Require<VisualElement>("temp-zone-warning");
            zoneAlarm = root.Require<VisualElement>("temp-zone-alarm");
            zoneTrip = root.Require<VisualElement>("temp-zone-trip");
            limitsLabel = root.Require<Label>("temp-limits");
            tripLabel = root.Require<Label>("temp-trip");

            RefreshScale();
            ApplyLevel(GeneratorTemperatureLevel.Normal);
        }

        /// <summary>Scale from 0 °C to a bit above the trip limit; zone widths are flex-grow in degrees.</summary>
        /// <remarks>Re-run when the limits change, so Inspector tweaks in Play mode show up at once.</remarks>
        private void RefreshScale()
        {
            float warning = specs.GeneratorWarningTemperature;
            float alarm = specs.GeneratorAlarmTemperature;
            float trip = specs.GeneratorTripTemperature;
            if (warning == shownWarning && alarm == shownAlarm && trip == shownTrip)
                return;

            shownWarning = warning;
            shownAlarm = alarm;
            shownTrip = trip;
            scaleMax = trip + ScaleHeadroom;

            zoneNormal.style.flexGrow = Mathf.Max(0f, warning);
            zoneWarning.style.flexGrow = Mathf.Max(0f, alarm - warning);
            zoneAlarm.style.flexGrow = Mathf.Max(0f, trip - alarm);
            zoneTrip.style.flexGrow = ScaleHeadroom;

            limitsLabel.text = UiFormat.Format("warn {0:0} · alarm {1:0}", warning, alarm);
            tripLabel.text = UiFormat.Format("trip {0:0}", trip);
            shownMarker = -1f;
        }

        public void Dispose()
        {
            root.RemoveFromClassList(BannerClass);
        }

        public void Show(in TurbineTelemetry telemetry)
        {
            latest = telemetry;
            if (!hasTelemetry)
            {
                hasTelemetry = true;
                smoothedTemperature = telemetry.GeneratorTemperature;
            }

            RefreshScale();

            GeneratorTemperatureLevel newLevel = Classify(telemetry.GeneratorTemperature, level);
            if (newLevel != level)
                ApplyLevel(newLevel);

            UpdateBanner(telemetry);
        }

        /// <summary>Called every frame with the unscaled frame time.</summary>
        public void Tick(float deltaTime)
        {
            if (!hasTelemetry)
                return;

            smoothedTemperature += (latest.GeneratorTemperature - smoothedTemperature) *
                                   (1f - Mathf.Exp(-deltaTime / MarkerSmoothingTime));

            float position = Mathf.Clamp01(smoothedTemperature / scaleMax);
            if (Mathf.Abs(position - shownMarker) > 0.001f)
            {
                shownMarker = position;
                marker.style.left = Length.Percent(position * 100f);
            }
        }

        private GeneratorTemperatureLevel Classify(float temperature, GeneratorTemperatureLevel current)
        {
            // Entering a level needs the limit; leaving it needs the limit minus the hysteresis.
            float Limit(GeneratorTemperatureLevel candidate, float limit) =>
                current >= candidate ? limit - LevelHysteresis : limit;

            if (temperature >= Limit(GeneratorTemperatureLevel.Trip, specs.GeneratorTripTemperature))
                return GeneratorTemperatureLevel.Trip;
            if (temperature >= Limit(GeneratorTemperatureLevel.Alarm, specs.GeneratorAlarmTemperature))
                return GeneratorTemperatureLevel.Alarm;
            if (temperature >= Limit(GeneratorTemperatureLevel.Warning, specs.GeneratorWarningTemperature))
                return GeneratorTemperatureLevel.Warning;
            return GeneratorTemperatureLevel.Normal;
        }

        private void ApplyLevel(GeneratorTemperatureLevel newLevel)
        {
            level = newLevel;

            string chipClass;
            string cardClass;
            switch (newLevel)
            {
                case GeneratorTemperatureLevel.Warning:
                    chip.text = "WARNING";
                    chipClass = "chip--warn";
                    cardClass = "card--warn";
                    break;
                case GeneratorTemperatureLevel.Alarm:
                    chip.text = "ALARM";
                    chipClass = "chip--crit";
                    cardClass = "card--crit";
                    break;
                case GeneratorTemperatureLevel.Trip:
                    chip.text = "TRIP";
                    chipClass = "chip--crit";
                    cardClass = "card--crit";
                    break;
                default:
                    chip.text = "NORMAL";
                    chipClass = "chip--good";
                    cardClass = null;
                    break;
            }

            foreach (string candidate in ChipClasses)
                chip.EnableInClassList(candidate, candidate == chipClass);
            foreach (string candidate in CardClasses)
                card.EnableInClassList(candidate, candidate == cardClass);
        }

        private void UpdateBanner(in TurbineTelemetry telemetry)
        {
            string key;
            string title;
            string message;
            bool critical;
            int temperature = Mathf.RoundToInt(telemetry.GeneratorTemperature);

            if (level == GeneratorTemperatureLevel.Trip)
            {
                key = "trip";
                critical = true;
                title = "Thermal trip";
                message = UiFormat.Format("Generator winding at {0} °C reached the {1:0} °C insulation limit. Protection stops the turbine.",
                    temperature, specs.GeneratorTripTemperature);
            }
            else if (level == GeneratorTemperatureLevel.Alarm)
            {
                key = "alarm";
                critical = true;
                title = "Generator overheating";
                message = UiFormat.Format("{0} °C, above the {1:0} °C alarm limit. Protection derates the output.",
                    temperature, specs.GeneratorAlarmTemperature);
            }
            else if (telemetry.State == TurbineOperatingState.StormShutdown)
            {
                key = "storm";
                critical = false;
                title = "Storm shutdown";
                message = OperatingStateText.Explanation(TurbineOperatingState.StormShutdown, specs);
                temperature = int.MinValue; // message does not contain the temperature
            }
            else if (level == GeneratorTemperatureLevel.Warning)
            {
                key = "warning";
                critical = false;
                title = "Generator temperature high";
                message = UiFormat.Format("{0} °C, above the {1:0} °C warning limit. Check the cooling.",
                    temperature, specs.GeneratorWarningTemperature);
            }
            else
            {
                SetBannerVisible(false);
                shownBannerKey = null;
                return;
            }

            if (key == shownBannerKey && temperature == shownBannerTemperature)
                return;

            shownBannerKey = key;
            shownBannerTemperature = temperature;
            notificationTitle.text = title;
            notificationMessage.text = message;
            notification.EnableInClassList(NotificationClasses[0], !critical);
            notification.EnableInClassList(NotificationClasses[1], critical);
            SetBannerVisible(true);
        }

        private void SetBannerVisible(bool visible)
        {
            notification.EnableInClassList("notification--visible", visible);
            // Lets the phone layout hide the state bubble, which sits in the same spot.
            root.EnableInClassList(BannerClass, visible);
        }
    }
}
