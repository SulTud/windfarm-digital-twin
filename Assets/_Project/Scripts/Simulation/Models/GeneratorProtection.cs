using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Generator protection function of the turbine controller (PLC). Graded like on a real turbine, each level a
    /// stronger action than the one before:
    ///
    ///   cooling fan feedback lost   CoolingFanFault alarm at once, before the temperature has moved
    ///   T >= warning (140 °C)       warning, operation continues
    ///   T >= alarm (150 °C)         alarm + derating: the power limit falls linearly from rated at the alarm
    ///                               temperature to DeratedPowerFraction at the trip temperature (less current, less
    ///                               I²R heat; buys time or holds the temperature, depending on the cooling left)
    ///   T >= trip (155 °C)          trip: the supervisory controller stops the turbine (FaultStop). Latched: it
    ///                               resets only once the winding is below GeneratorRestartTemperature and no cooling
    ///                               fault is active (a turbine that restarts with a dead fan just trips again).
    ///
    /// Warning and alarm are left 2 °C below their limits (hysteresis, same as the dashboard). The derate curve needs
    /// none: it is continuous in the temperature, so it cannot chatter.
    ///
    /// Acts on the winding temperature without sensor noise, standing in for the filtered PT100 signal a PLC uses.
    /// </summary>
    public sealed class GeneratorProtection
    {
        public const float Hysteresis = 2f; // °C

        private readonly TurbineSpecs specs;

        public GeneratorProtection(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
            PowerLimitMW = specs.RatedPowerMW;
        }

        public TurbineAlarms Alarms { get; private set; }

        /// <summary>Active power limit (MW): rated, or lower while derating.</summary>
        public float PowerLimitMW { get; private set; }

        /// <summary>True while the over-temperature trip is latched; the supervisory controller must keep the turbine stopped.</summary>
        public bool Tripped => (Alarms & TurbineAlarms.GeneratorOverTemperatureTrip) != 0;

        public void Step(float windingTemperature, bool coolingFanRunning)
        {
            TurbineAlarms previous = Alarms;
            TurbineAlarms alarms = TurbineAlarms.None;

            bool Exceeds(TurbineAlarms alarm, float limit) =>
                windingTemperature >= ((previous & alarm) != 0 ? limit - Hysteresis : limit);

            if (!coolingFanRunning)
                alarms |= TurbineAlarms.CoolingFanFault;
            if (Exceeds(TurbineAlarms.GeneratorTemperatureWarning, specs.GeneratorWarningTemperature))
                alarms |= TurbineAlarms.GeneratorTemperatureWarning;
            if (Exceeds(TurbineAlarms.GeneratorTemperatureAlarm, specs.GeneratorAlarmTemperature))
                alarms |= TurbineAlarms.GeneratorTemperatureAlarm;

            bool tripped = Tripped
                ? !(coolingFanRunning && windingTemperature <= specs.GeneratorRestartTemperature)
                : windingTemperature >= specs.GeneratorTripTemperature;
            if (tripped)
                alarms |= TurbineAlarms.GeneratorOverTemperatureTrip;

            Alarms = alarms;

            float derate = Mathf.InverseLerp(specs.GeneratorAlarmTemperature, specs.GeneratorTripTemperature, windingTemperature);
            PowerLimitMW = specs.RatedPowerMW * Mathf.Lerp(1f, specs.DeratedPowerFraction, derate);
        }
    }
}
