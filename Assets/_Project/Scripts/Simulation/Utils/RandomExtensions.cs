using System;

namespace WindFarm.Simulation
{
    internal static class RandomExtensions
    {
        public static float NextFloat(this Random random, float min, float max) =>
            min + (float)random.NextDouble() * (max - min);

        /// <summary>Normally distributed sample using the Box-Muller transform.</summary>
        public static float NextGaussian(this Random random, float standardDeviation = 1f)
        {
            double u1 = 1.0 - random.NextDouble(); // (0, 1] so Log(0) cannot occur
            double u2 = random.NextDouble();
            double standardNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return (float)(standardNormal * standardDeviation);
        }
    }
}
