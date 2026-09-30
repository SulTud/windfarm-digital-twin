using System;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Physical faults the mock simulator can inject (demo and operator-training scenarios, "what-if").
    ///
    /// These are causes inside the machine and exist only in the simulator. Consumers never see them: like on a real
    /// turbine, they see only what the controller detects and reports, as <see cref="TurbineAlarms"/> in the telemetry.
    /// A real data source therefore needs no counterpart of this type.
    /// </summary>
    [Flags]
    public enum TurbineFaults
    {
        None = 0,

        /// <summary>
        /// The generator cooling fan stops (motor failure or its breaker trips). Only natural convection is left, so
        /// the winding heats up under load.
        /// </summary>
        CoolingFanFailure = 1 << 0,
    }
}
