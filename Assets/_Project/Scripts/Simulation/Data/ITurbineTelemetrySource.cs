using System;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Abstraction that UI and other consumers bind to. Today the mock simulator implements it;
    /// later a real SCADA / MQTT / WebSocket feed can implement the same interface without touching the UI.
    /// </summary>
    public interface ITurbineTelemetrySource
    {
        string TurbineId { get; }

        /// <summary>Most recently published reading. Late subscribers can read their initial value from here.</summary>
        TurbineTelemetry LatestTelemetry { get; }

        /// <summary>Raised every sampling period with a new sensor reading.</summary>
        event Action<TurbineTelemetry> TelemetryUpdated;

        /// <summary>Raised when the operating state changes: (previous, current).</summary>
        event Action<TurbineOperatingState, TurbineOperatingState> OperatingStateChanged;
    }
}
