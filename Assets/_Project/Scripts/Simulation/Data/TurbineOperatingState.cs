namespace WindFarm.Simulation
{
    /// <summary>
    /// Operating state of the turbine as decided by the supervisory controller.
    /// </summary>
    public enum TurbineOperatingState
    {
        /// <summary>Wind below cut-in speed; generator disconnected from the grid, rotor at standstill.</summary>
        Idle,

        /// <summary>Partial-load region; rotor tracks the wind at the optimal tip speed ratio.</summary>
        Producing,

        /// <summary>Rated power reached; blade pitch control holds power constant.</summary>
        RatedPower,

        /// <summary>Wind above cut-out speed; blades feathered and rotor braking.</summary>
        StormShutdown,

        /// <summary>
        /// Stopped by a protection trip (e.g. generator over-temperature); blades feathered and rotor braking. Restarts
        /// on its own only once the cause is cleared (see <see cref="TurbineTelemetry.Alarms"/>).
        /// </summary>
        FaultStop
    }
}
