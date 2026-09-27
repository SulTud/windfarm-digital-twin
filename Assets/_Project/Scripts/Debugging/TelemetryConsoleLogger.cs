using UnityEngine;
using WindFarm.Simulation;

namespace WindFarm.Debugging
{
    /// <summary>
    /// Simple subscriber for verifying the simulator through the Console. UI scripts follow the same pattern:
    /// subscribe in OnEnable, unsubscribe in OnDisable (no leaks or null references on scene changes).
    /// </summary>
    public sealed class TelemetryConsoleLogger : MonoBehaviour
    {
        // Unity cannot serialize interfaces; take the concrete component and use it through the interface.
        [SerializeField] private TurbineDataSimulator simulator;
        [SerializeField, Min(0.1f)] private float logInterval = 2f;

        private ITurbineTelemetrySource source;
        private float nextLogTime;

        private void OnEnable()
        {
            // Unity's null check (destroyed / unassigned object) must be done on the concrete type, not through the interface.
            if (simulator == null)
            {
                Debug.LogWarning($"{nameof(TelemetryConsoleLogger)}: simulator is not assigned.", this);
                return;
            }

            source = simulator;
            source.TelemetryUpdated += HandleTelemetry;
            source.OperatingStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (source == null)
                return;

            source.TelemetryUpdated -= HandleTelemetry;
            source.OperatingStateChanged -= HandleStateChanged;
        }

        private void HandleTelemetry(TurbineTelemetry telemetry)
        {
            if (Time.time < nextLogTime)
                return;

            nextLogTime = Time.time + logInterval;
            Debug.Log(telemetry.ToString(), this);
        }

        private void HandleStateChanged(TurbineOperatingState previous, TurbineOperatingState current) =>
            Debug.Log($"[{source.TurbineId}] State: {previous} -> {current}", this);
    }
}
