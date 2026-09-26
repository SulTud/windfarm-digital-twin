using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Denetim kontrolcüsü: filtrelenmiş rüzgar hızına ve üretilen güce bakarak çalışma durumuna karar verir.
    /// Anlık gürültüyle durumun titrememesi (chattering) için tüm geçişlerde histerezis kullanılır.
    /// </summary>
    public sealed class TurbineController
    {
        private const float CutInHysteresis = 0.5f;          // m/s
        private const float RatedPowerEnterRatio = 0.99f;
        private const float RatedPowerExitRatio = 0.95f;
        private const float GustCutOutFactor = 1.12f;        // anlık ~28 m/s gust'ta ortalamayı beklemeden dur

        private readonly TurbineSpecs specs;
        private float averagedWindSpeed;
        private bool initialized;

        public TurbineOperatingState State { get; private set; } = TurbineOperatingState.Idle;

        public bool IsGeneratorConnected =>
            State == TurbineOperatingState.Producing || State == TurbineOperatingState.RatedPower;

        public TurbineController(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
        }

        /// <returns>Durum bu adımda değiştiyse true.</returns>
        public bool Step(float deltaTime, float windSpeed, float powerMW)
        {
            if (!initialized)
            {
                averagedWindSpeed = windSpeed;
                initialized = true;
            }
            else
            {
                float alpha = 1f - Mathf.Exp(-deltaTime / specs.ControlAveragingTime);
                averagedWindSpeed += (windSpeed - averagedWindSpeed) * alpha;
            }

            TurbineOperatingState next = EvaluateNextState(windSpeed, powerMW);
            if (next == State)
                return false;

            State = next;
            return true;
        }

        private TurbineOperatingState EvaluateNextState(float instantWindSpeed, float powerMW)
        {
            if (State == TurbineOperatingState.StormShutdown)
            {
                if (averagedWindSpeed >= specs.RestartWindSpeed)
                    return TurbineOperatingState.StormShutdown;

                return averagedWindSpeed >= specs.CutInWindSpeed
                    ? TurbineOperatingState.Producing
                    : TurbineOperatingState.Idle;
            }

            bool stormDetected = averagedWindSpeed >= specs.CutOutWindSpeed ||
                                 instantWindSpeed >= specs.CutOutWindSpeed * GustCutOutFactor;
            if (stormDetected)
                return TurbineOperatingState.StormShutdown;

            switch (State)
            {
                case TurbineOperatingState.Idle:
                    return averagedWindSpeed >= specs.CutInWindSpeed
                        ? TurbineOperatingState.Producing
                        : TurbineOperatingState.Idle;

                case TurbineOperatingState.Producing:
                    if (averagedWindSpeed < specs.CutInWindSpeed - CutInHysteresis)
                        return TurbineOperatingState.Idle;
                    return powerMW >= specs.RatedPowerMW * RatedPowerEnterRatio
                        ? TurbineOperatingState.RatedPower
                        : TurbineOperatingState.Producing;

                case TurbineOperatingState.RatedPower:
                    return powerMW < specs.RatedPowerMW * RatedPowerExitRatio
                        ? TurbineOperatingState.Producing
                        : TurbineOperatingState.RatedPower;

                default:
                    return State;
            }
        }
    }
}
