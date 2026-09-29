using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Simulation;

namespace WindFarm.UI
{
    /// <summary>
    /// Power curve (power vs. wind speed), the signature chart of a wind turbine datasheet:
    ///   - the theoretical curve from the specs, with cut-in / cut-out markers,
    ///   - recent measured samples as a faint scatter (they spread around the curve because the rotor lags the wind,
    ///     exactly like real SCADA data),
    ///   - the live operating point, emphasized and labeled.
    /// </summary>
    internal sealed class PowerCurveChart : ChartElement
    {
        private const float CurveStep = 0.25f;          // m/s between curve vertices
        private const float ScatterRadius = 2f;
        private const float LiveRadius = 4.5f;
        private const float WindTickStep = 5f;
        private const float PowerTickStep = 1f;

        private readonly Label[] xLabels = new Label[8];
        private readonly Label[] yLabels = new Label[5];
        private readonly Label xUnit;
        private readonly Label yUnit;
        private readonly Label liveLabel;

        private float shownLiveWind = float.NaN;
        private float shownLivePower = float.NaN;

        public PowerCurveChart()
        {
            for (int i = 0; i < xLabels.Length; i++)
                xLabels[i] = AddLabel("chart__tick chart__tick--x");
            for (int i = 0; i < yLabels.Length; i++)
                yLabels[i] = AddLabel("chart__tick chart__tick--y");

            xUnit = AddLabel("chart__tick chart__unit");
            xUnit.text = "m/s";
            yUnit = AddLabel("chart__tick chart__unit");
            yUnit.text = "MW";
            liveLabel = AddLabel("chart__live-label");
        }

        // ---- Data set by the presenter before Refresh() ----

        public TurbineSpecs Specs { get; set; }
        public TelemetryHistory History { get; set; }
        public double Now { get; set; }
        public double ScatterSeconds { get; set; } = 120.0;
        public float LiveWind { get; set; }
        public float LivePower { get; set; }
        public bool HasData { get; set; }

        /// <summary>Room for storm winds to the right of cut-out.</summary>
        private float MaxWind => Specs != null ? Mathf.Ceil((Specs.CutOutWindSpeed + 5f) / WindTickStep) * WindTickStep : 30f;

        private float MaxPower => Specs != null ? Specs.RatedPowerMW * 1.1f : 3.3f;

        private float X(Rect plot, float wind) => plot.xMin + Mathf.Clamp01(wind / MaxWind) * plot.width;

        private float Y(Rect plot, float power) => plot.yMax - Mathf.Clamp01(power / MaxPower) * plot.height;

        /// <summary>Repositions the labels for the current size and live point, then repaints.</summary>
        public void Refresh()
        {
            Rect plot = PlotRect;
            UpdateAxisLabels(plot);
            UpdateLiveLabel(plot);
            MarkDirtyRepaint();
        }

        private void UpdateAxisLabels(Rect plot)
        {
            for (int i = 0; i < xLabels.Length; i++)
            {
                float wind = i * WindTickStep;
                bool used = wind <= MaxWind;
                xLabels[i].style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used)
                    continue;

                xLabels[i].text = wind.ToString("0", UiFormat.Invariant);
                xLabels[i].style.left = X(plot, wind);
                xLabels[i].style.top = plot.yMax + 3f;
            }

            for (int i = 0; i < yLabels.Length; i++)
            {
                float power = i * PowerTickStep;
                bool used = power <= MaxPower;
                yLabels[i].style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used)
                    continue;

                yLabels[i].text = power.ToString("0", UiFormat.Invariant);
                yLabels[i].style.top = Y(plot, power) - 7f;
                yLabels[i].style.width = PadLeft - 6f;
            }

            // Units in the plot corners: MW top-left, m/s bottom-right above the last tick.
            yUnit.style.left = plot.xMin + 4f;
            yUnit.style.top = plot.yMin;
            xUnit.style.right = PadRight;
            xUnit.style.top = plot.yMax - 16f;
        }

        private void UpdateLiveLabel(Rect plot)
        {
            liveLabel.style.display = HasData ? DisplayStyle.Flex : DisplayStyle.None;
            if (!HasData)
                return;

            float wind = Mathf.Round(LiveWind * 10f) / 10f;
            float power = Mathf.Round(LivePower * 100f) / 100f;
            if (wind != shownLiveWind || power != shownLivePower)
            {
                shownLiveWind = wind;
                shownLivePower = power;
                liveLabel.text = UiFormat.Format("{0:0.0} m/s · {1:0.00} MW", wind, power);
            }

            // Place the label beside the point, flipping sides near the right and top edges to stay inside the card.
            float x = X(plot, LiveWind);
            float y = Y(plot, LivePower);
            bool flipLeft = x > plot.xMin + plot.width * 0.55f;
            bool below = y < plot.yMin + 24f;

            liveLabel.style.left = flipLeft ? StyleKeyword.Auto : new StyleLength(x + 10f);
            liveLabel.style.right = flipLeft ? new StyleLength(contentRect.width - x + 10f) : StyleKeyword.Auto;
            liveLabel.style.top = below ? y + 8f : y - 22f;
        }

        protected override void DrawContent(Painter2D painter, Rect plot)
        {
            if (Specs == null)
                return;

            DrawGrid(painter, plot);
            DrawOperatingLimits(painter, plot);
            DrawCurve(painter, plot);

            if (!HasData)
                return;

            DrawScatter(painter, plot);
            // Halo + dot: the one thing on this chart the eye should find first.
            FillCircle(painter, new Vector2(X(plot, LiveWind), Y(plot, LivePower)), LiveRadius * 2f,
                new Color(SeriesColor.r, SeriesColor.g, SeriesColor.b, 0.18f));
            DrawDot(painter, new Vector2(X(plot, LiveWind), Y(plot, LivePower)), LiveRadius, SeriesColor);
        }

        private void DrawGrid(Painter2D painter, Rect plot)
        {
            painter.lineWidth = 1f;
            painter.strokeColor = GridColor;
            painter.BeginPath();
            for (float power = 0f; power <= MaxPower + 0.0001f; power += PowerTickStep)
            {
                float y = Mathf.Round(Y(plot, power)) + 0.5f;
                painter.MoveTo(new Vector2(plot.xMin, y));
                painter.LineTo(new Vector2(plot.xMax, y));
            }
            painter.Stroke();
        }

        private void DrawOperatingLimits(Painter2D painter, Rect plot)
        {
            painter.lineWidth = 1f;
            painter.strokeColor = ColorOf(ReferenceLineKind.Muted);
            DashedVertical(painter, X(plot, Specs.CutInWindSpeed), plot.yMin, plot.yMax);
            painter.strokeColor = ColorOf(ReferenceLineKind.Warning);
            DashedVertical(painter, X(plot, Specs.CutOutWindSpeed), plot.yMin, plot.yMax);
        }

        private void DrawCurve(Painter2D painter, Rect plot)
        {
            painter.lineWidth = 2f;
            painter.strokeColor = CurveColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(X(plot, 0f), Y(plot, 0f)));

            // Vertical steps at cut-in and cut-out are part of the curve: power switches on and off there.
            float cutIn = Specs.CutInWindSpeed;
            float cutOut = Specs.CutOutWindSpeed;
            painter.LineTo(new Vector2(X(plot, cutIn), Y(plot, 0f)));
            for (float wind = cutIn; wind < cutOut; wind += CurveStep)
                painter.LineTo(new Vector2(X(plot, wind), Y(plot, Specs.PowerCurveMW(wind))));
            painter.LineTo(new Vector2(X(plot, cutOut), Y(plot, Specs.PowerCurveMW(cutOut))));
            painter.LineTo(new Vector2(X(plot, cutOut), Y(plot, 0f)));
            painter.LineTo(new Vector2(X(plot, MaxWind), Y(plot, 0f)));
            painter.Stroke();
        }

        private void DrawScatter(Painter2D painter, Rect plot)
        {
            TelemetryHistory history = History;
            if (history == null || history.Count == 0)
                return;

            // One path with a sub-path per dot and a single fill: far cheaper than a fill call per dot.
            painter.fillColor = new Color(SeriesColor.r, SeriesColor.g, SeriesColor.b, 0.35f);
            painter.BeginPath();
            for (int i = history.LowerBound(Now - ScatterSeconds); i < history.Count; i++)
            {
                TelemetrySample sample = history[i];
                var center = new Vector2(X(plot, sample.WindSpeed), Y(plot, sample.PowerMW));
                painter.MoveTo(center + new Vector2(ScatterRadius, 0f));
                painter.Arc(center, ScatterRadius, new Angle(0f), new Angle(360f));
                painter.ClosePath();
            }
            painter.Fill();
        }
    }
}
