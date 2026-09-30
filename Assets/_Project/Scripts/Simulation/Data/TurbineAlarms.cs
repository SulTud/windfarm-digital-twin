using System;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Active alarms reported by the turbine controller, as a real SCADA system receives them (IEC 61400-25 models
    /// them as status and alarm information of the turbine and generator logical nodes). Several can be active at once.
    ///
    /// The controller decides these, the dashboard only displays them: the protection acts on the machine whether or
    /// not anyone is watching the screen.
    /// </summary>
    [Flags]
    public enum TurbineAlarms
    {
        None = 0,

        /// <summary>The generator cooling fan does not run (motor protection feedback). Raised before any temperature rise.</summary>
        CoolingFanFault = 1 << 0,

        /// <summary>Winding above the warning limit. Operation continues.</summary>
        GeneratorTemperatureWarning = 1 << 1,

        /// <summary>Winding above the alarm limit. The controller derates the output (see <see cref="TurbineTelemetry.PowerLimitMW"/>).</summary>
        GeneratorTemperatureAlarm = 1 << 2,

        /// <summary>Winding reached the trip limit and the turbine was stopped. Latched until the winding has cooled down.</summary>
        GeneratorOverTemperatureTrip = 1 << 3,
    }
}
