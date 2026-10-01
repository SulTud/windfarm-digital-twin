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
    ///   - Chip (NORMAL / WARNING / ALARM / TRIP) and card border tint from the alarms the turbine controller reports.
    ///   - Scale: zone widths from the limits, marker follows the smoothed temperature.
    ///   - Banner: the most important active event in plain language
    ///     (trip > alarm / derating > storm shutdown > temperature warning > cooling fan fault).
    ///
    /// The dashboard does not classify the temperature itself: the controller decides (with its hysteresis) and acts
    /// on it, the dashboard shows what it reports, as a SCADA screen does. That also keeps the chip, the banner and the
    /// real derate / trip in step.
    /// </summary>
    internal sealed class AlertsPresenter
    {
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
        private readonly Button notificationAction;
        private readonly System.Action showGenerator;
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
        private int shownBannerLimit = int.MinValue;

        /// <param name="showGenerator">SHOW action on generator banners (X-Ray + camera to the drivetrain); null hides the button.</param>
        public AlertsPresenter(VisualElement root, TurbineSpecs specs, System.Action showGenerator)
        {
            this.root = root;
            this.specs = specs;
            this.showGenerator = showGenerator;

            notificationAction = root.Require<Button>("notification-action");
            notificationAction.clicked += HandleShowClicked;

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
            notificationAction.clicked -= HandleShowClicked;
            root.RemoveFromClassList(BannerClass);
        }

        private void HandleShowClicked() => showGenerator?.Invoke();

        public void Show(in TurbineTelemetry telemetry)
        {
            latest = telemetry;
            if (!hasTelemetry)
            {
                hasTelemetry = true;
                smoothedTemperature = telemetry.GeneratorTemperature;
            }

            RefreshScale();

            GeneratorTemperatureLevel newLevel = Classify(telemetry.Alarms);
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

        private static GeneratorTemperatureLevel Classify(TurbineAlarms alarms)
        {
            if ((alarms & TurbineAlarms.GeneratorOverTemperatureTrip) != 0)
                return GeneratorTemperatureLevel.Trip;
            if ((alarms & TurbineAlarms.GeneratorTemperatureAlarm) != 0)
                return GeneratorTemperatureLevel.Alarm;
            if ((alarms & TurbineAlarms.GeneratorTemperatureWarning) != 0)
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
            int limitTenths = int.MinValue; // power limit in 0.1 MW, only where the message shows it
            bool fanFault = (telemetry.Alarms & TurbineAlarms.CoolingFanFault) != 0;

            if (level == GeneratorTemperatureLevel.Trip)
            {
                key = fanFault ? "trip-fan" : "trip";
                critical = true;
                title = "Thermal trip · turbine stopped";
                message = fanFault
                    ? UiFormat.Format("Winding reached the {0:0} °C insulation limit. Now {1} °C. Stays stopped until the cooling fan is repaired.",
                        specs.GeneratorTripTemperature, temperature)
                    : UiFormat.Format("Cooling works again. Winding {0} °C, restarts below {1:0} °C.",
                        temperature, specs.GeneratorRestartTemperature);
            }
            else if (level == GeneratorTemperatureLevel.Alarm)
            {
                key = fanFault ? "alarm-fan" : "alarm";
                critical = true;
                title = "Generator overheating · derated";
                limitTenths = Mathf.RoundToInt(telemetry.PowerLimitMW * 10f);
                message = UiFormat.Format("{0} °C, above the {1:0} °C alarm limit. Output limited to {2:0.0} MW. Trips at {3:0} °C.",
                    temperature, specs.GeneratorAlarmTemperature, limitTenths / 10f, specs.GeneratorTripTemperature);
                if (fanFault)
                    message += " Cause: cooling fan failed.";
            }
            else if (telemetry.State == TurbineOperatingState.StormShutdown)
            {
                key = "storm";
                critical = false;
                title = "Storm shutdown";
                message = OperatingStateText.Explanation(DisplayedState.StormShutdown, specs);
                temperature = int.MinValue; // message does not contain the temperature
            }
            else if (level == GeneratorTemperatureLevel.Warning)
            {
                key = fanFault ? "warning-fan" : "warning";
                critical = false;
                title = "Generator temperature high";
                message = UiFormat.Format(fanFault
                        ? "{0} °C, above the {1:0} °C warning limit. Cause: cooling fan failed."
                        : "{0} °C, above the {1:0} °C warning limit. Cooling down.",
                    temperature, specs.GeneratorWarningTemperature);
            }
            else if (fanFault)
            {
                // Reported before the temperature has moved: the cause is visible before the symptom.
                key = "fan";
                critical = false;
                title = "Cooling fan fault";
                message = "Generator fan stopped. The winding heats up under load; at low wind it can keep running.";
                temperature = int.MinValue;
            }
            else
            {
                SetBannerVisible(false);
                SetActionVisible(false);
                shownBannerKey = null;
                return;
            }

            // Every banner except the storm is about the generator: offer the close look at it.
            SetActionVisible(showGenerator != null && key != "storm");

            if (key == shownBannerKey && temperature == shownBannerTemperature && limitTenths == shownBannerLimit)
                return;

            shownBannerKey = key;
            shownBannerTemperature = temperature;
            shownBannerLimit = limitTenths;
            notificationTitle.text = title;
            notificationMessage.text = message;
            notification.EnableInClassList(NotificationClasses[0], !critical);
            notification.EnableInClassList(NotificationClasses[1], critical);
            SetBannerVisible(true);
        }

        // The banner fades with opacity and stays in the layout: the button must be removed, not only invisible,
        // or it would still catch taps on the hidden banner.
        private void SetActionVisible(bool visible) =>
            notificationAction.EnableInClassList("notification__action--on", visible);

        private void SetBannerVisible(bool visible)
        {
            notification.EnableInClassList("notification--visible", visible);
            // Lets the phone layout hide the state bubble, which sits in the same spot.
            root.EnableInClassList(BannerClass, visible);
        }
    }
}
