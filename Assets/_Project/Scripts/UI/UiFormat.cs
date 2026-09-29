using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    internal static class UiFormat
    {
        // Invariant culture: the UI is English, and a Turkish/German system locale would print "1,5" instead of "1.5".
        public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Format(string format, params object[] args) => string.Format(Invariant, format, args);
    }

    /// <summary>
    /// A label showing a number that is updated every frame. The text is only rebuilt when the value rounded to the
    /// displayed precision changes, so smoothing does not allocate a string and trigger a relayout on every frame.
    /// </summary>
    internal sealed class NumberLabel
    {
        private readonly Label label;
        private readonly string format;
        private readonly float step;
        private float shownValue = float.NaN;

        /// <param name="format">.NET numeric format for the value, e.g. "0.00".</param>
        /// <param name="decimals">Decimals shown by the format; decides when the text must change.</param>
        public NumberLabel(Label label, string format, int decimals)
        {
            this.label = label;
            this.format = format;
            step = Mathf.Pow(10f, -decimals);
        }

        public void SetValue(float value)
        {
            float rounded = Mathf.Round(value / step) * step;
            if (label == null || rounded == shownValue)
                return;

            shownValue = rounded;
            label.text = rounded.ToString(format, UiFormat.Invariant);
        }
    }
}
