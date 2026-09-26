using System;
using UnityEngine;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Rotor devrini birinci dereceden gecikme (first-order lag) ile modeller: rotor ağır bir kütledir,
    /// rüzgar anında değişse bile devir hedefe üstel olarak, "yavaşça" yaklaşır.
    ///
    /// Hedef devir, optimum uç hız oranından (λ = ωR / v) türetilir:
    ///   RPM_hedef = λ_opt · v · 60 / (2πR), [MinRpm, RatedRpm] aralığına kırpılır.
    /// Nominal devrin üstünde pitch kontrolü devri sabitler (kırpmanın fiziksel karşılığı).
    /// </summary>
    public sealed class RotorModel
    {
        private const float BrakingTimeConstantFactor = 0.6f; // mekanik fren + feather, serbest ivmelenmeden hızlı

        private readonly TurbineSpecs specs;

        public float Rpm { get; private set; }

        public RotorModel(TurbineSpecs specs)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
        }

        public void Step(float deltaTime, float windSpeed, bool isOperating)
        {
            float targetRpm = isOperating ? CalculateTargetRpm(windSpeed) : 0f;
            float timeConstant = isOperating
                ? specs.RotorTimeConstant
                : specs.RotorTimeConstant * BrakingTimeConstantFactor;

            float alpha = 1f - Mathf.Exp(-deltaTime / timeConstant);
            Rpm += (targetRpm - Rpm) * alpha;
        }

        public float CalculateTargetRpm(float windSpeed)
        {
            float optimalRpm = specs.OptimalTipSpeedRatio * windSpeed * 60f / (2f * Mathf.PI * specs.RotorRadius);
            return Mathf.Clamp(optimalRpm, specs.MinRotorRpm, specs.RatedRotorRpm);
        }
    }
}
