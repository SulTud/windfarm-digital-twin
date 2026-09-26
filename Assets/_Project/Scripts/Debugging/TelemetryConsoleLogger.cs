using UnityEngine;
using WindFarm.Simulation;

namespace WindFarm.Debugging
{
    /// <summary>
    /// Simülatörü Console üzerinden doğrulamak için basit abone. UI scriptleri de aynı kalıbı izler:
    /// OnEnable'da abone ol, OnDisable'da aboneliği bırak (sahne değişiminde sızıntı / null referans olmaz).
    /// </summary>
    public sealed class TelemetryConsoleLogger : MonoBehaviour
    {
        // Unity arayüzleri serialize edemez; somut bileşeni alıp arayüz üzerinden kullanıyoruz.
        [SerializeField] private TurbineDataSimulator simulator;
        [SerializeField, Min(0.1f)] private float logInterval = 2f;

        private ITurbineTelemetrySource source;
        private float nextLogTime;

        private void OnEnable()
        {
            // Unity'nin null kontrolü (destroyed / atanmamış obje) arayüz üzerinden değil, somut tip üzerinden yapılmalı.
            if (simulator == null)
            {
                Debug.LogWarning($"{nameof(TelemetryConsoleLogger)}: simulator atanmamış.", this);
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
            Debug.Log($"[{source.TurbineId}] State: {previous} → {current}", this);
    }
}
