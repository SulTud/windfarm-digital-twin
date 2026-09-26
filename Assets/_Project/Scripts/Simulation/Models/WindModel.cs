using System;
using UnityEngine;
using Random = System.Random;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Rüzgar hızını üç bileşenin toplamı olarak üretir:
    ///   v(t) = [ortalama + yavaş salınım(t)] + türbülans(t) + gust(t)
    /// - Yavaş salınım: Perlin gürültüsü (hava durumu ölçeğinde, dakikalar).
    /// - Türbülans: Ornstein-Uhlenbeck süreci (ortalamaya dönen, zamanla ilişkili rastgelelik, saniyeler).
    /// - Gust: Poisson zamanlı, IEC 61400-1 benzeri (1 - cos) profilli ani rüzgarlar.
    /// </summary>
    public sealed class WindModel
    {
        private readonly SiteWindConditions conditions;
        private readonly Random random;
        private readonly float perlinRow;

        private double time;
        private float turbulence;

        private bool gustActive;
        private float gustElapsed;
        private float gustDuration;
        private float gustAmplitude;

        public float CurrentSpeed { get; private set; }

        public WindModel(SiteWindConditions conditions, Random random)
        {
            this.conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            perlinRow = random.NextFloat(0f, 1000f);
            CurrentSpeed = conditions.MeanWindSpeed;
        }

        public void Step(float deltaTime)
        {
            time += deltaTime;

            float localMean = Mathf.Max(0f, conditions.MeanWindSpeed + SampleDrift());
            StepTurbulence(deltaTime, localMean);
            float gust = StepGust(deltaTime);

            CurrentSpeed = Mathf.Max(0f, localMean + turbulence + gust);
        }

        private float SampleDrift()
        {
            float phase = (float)(time / conditions.DriftPeriod);
            float noise = Mathf.PerlinNoise(phase, perlinRow) * 2f - 1f; // ~[-1, 1]
            return noise * conditions.DriftAmplitude;
        }

        /// <summary>
        /// OU sürecinin kesin (exact) ayrıklaştırması; her dt için kararlıdır.
        /// Durağan standart sapma = TI × yerel ortalama, yani rüzgar sertleştikçe türbülans da büyür.
        /// </summary>
        private void StepTurbulence(float deltaTime, float localMean)
        {
            float decay = Mathf.Exp(-deltaTime / conditions.TurbulenceTimeConstant);
            float stationaryStdDev = conditions.TurbulenceIntensity * localMean;
            float diffusion = stationaryStdDev * Mathf.Sqrt(1f - decay * decay);

            turbulence = turbulence * decay + random.NextGaussian(diffusion);
        }

        private float StepGust(float deltaTime)
        {
            if (!gustActive)
            {
                float probability = conditions.GustsPerMinute / 60f * deltaTime;
                if (random.NextDouble() < probability)
                    StartGust();
                else
                    return 0f;
            }

            gustElapsed += deltaTime;
            if (gustElapsed >= gustDuration)
            {
                gustActive = false;
                return 0f;
            }

            float normalizedTime = gustElapsed / gustDuration;
            return gustAmplitude * 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * normalizedTime));
        }

        private void StartGust()
        {
            gustActive = true;
            gustElapsed = 0f;
            gustAmplitude = random.NextFloat(conditions.GustAmplitudeRange.x, conditions.GustAmplitudeRange.y);
            gustDuration = Mathf.Max(0.5f, random.NextFloat(conditions.GustDurationRange.x, conditions.GustDurationRange.y));
        }
    }
}
