using System;
using UnityEngine;
using Random = System.Random;

namespace WindFarm.Simulation
{
    /// <summary>
    /// Generates wind speed as the sum of three components:
    ///   v(t) = [mean + slow drift(t)] + turbulence(t) + gust(t)
    /// - Slow drift: Perlin noise (weather scale, minutes).
    /// - Turbulence: Ornstein-Uhlenbeck process (mean-reverting, time-correlated randomness, seconds).
    /// - Gust: Poisson-timed gusts with an IEC 61400-1 style (1 - cos) profile.
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
        /// Exact discretization of the OU process; stable for any dt.
        /// Stationary standard deviation = TI × local mean, so turbulence grows as the wind gets stronger.
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
