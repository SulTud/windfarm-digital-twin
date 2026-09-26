using System;

namespace WindFarm.Simulation
{
    /// <summary>
    /// UI ve diğer tüketicilerin bağlandığı soyutlama. Bugün mock simülatör bu arayüzü uyguluyor;
    /// yarın gerçek bir SCADA / MQTT / WebSocket kaynağı aynı arayüzle UI'a hiç dokunmadan takılabilir.
    /// </summary>
    public interface ITurbineTelemetrySource
    {
        string TurbineId { get; }

        /// <summary>En son yayınlanan okuma. Geç abone olan UI'lar ilk değeri buradan alabilir.</summary>
        TurbineTelemetry LatestTelemetry { get; }

        /// <summary>Her örnekleme periyodunda yeni sensör okumasıyla tetiklenir.</summary>
        event Action<TurbineTelemetry> TelemetryUpdated;

        /// <summary>Çalışma durumu değiştiğinde tetiklenir: (önceki, yeni).</summary>
        event Action<TurbineOperatingState, TurbineOperatingState> OperatingStateChanged;
    }
}
