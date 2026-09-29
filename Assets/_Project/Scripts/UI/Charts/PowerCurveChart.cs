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
    ///
    /// Cost: the chart itself (grid, curve, scatter) repaints only when a new sample arrives, the size or the palette
    /// changes, or it comes back on screen. The live point moves every frame, so it is a separate small element that
    /// is only translated (a transform change, no tessellation and no relayout), and so is its label.
    /// </summary>
    internal sealed class PowerCurveChart : ChartElement
    {
        private const float CurveStep = 0.25f;          // m/s between curve vertices
        private const float ScatterRadius = 2f;
        private const float LiveRadius = 4.5f;
        private const float WindTickStep = 5f;
        private const float PowerTickStep = 1f;

        // A 2 px dot as an octagon: looks the same as a circle, far fewer vertices than Arc.
        private static readonly Vector2[] OctagonCorners = BuildOctagon(ScatterRadius);

        private readonly Label[] xLabels = new Label[8];
        private readonly Label[] yLabels = new Label[5];
        private readonly Label xUnit;
        private readonly Label yUnit;
        private readonly LiveMarker liveMarker;
        private readonly Label liveLabel;

        private float shownLiveWind = float.NaN;
        private float shownLivePower = float.NaN;

        private bool wasShown;
        private Rect drawnPlot;
        private double drawnNewestTime = double.NaN;
        private bool drawnHasData;

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

            liveMarker = new LiveMarker(this, LiveRadius);
            Add(liveMarker);

            liveLabel = AddLabel("chart__live-label");
            liveLabel.style.left = 0f;
            liveLabel.style.top = 0f;
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

        /// <summary>
        /// Called every frame. Moves the live point; repaints the chart only when its content changed. Does nothing
        /// while the chart is off screen.
        /// </summary>
        public void Refresh()
        {
            bool shown = IsShown();
            bool becameShown = shown && !wasShown;
            wasShown = shown;
            if (!shown)
                return;

            Rect plot = PlotRect;
            UpdateLivePoint(plot);

            TelemetryHistory history = History;
            double newestTime = history != null && history.Count > 0 ? history[history.Count - 1].Time : double.NaN;
            bool sameSample = newestTime == drawnNewestTime || (double.IsNaN(newestTime) && double.IsNaN(drawnNewestTime));
            if (!becameShown && !StyleChanged && plot == drawnPlot && HasData == drawnHasData && sameSample)
                return;

            StyleChanged = false;
            drawnPlot = plot;
            drawnNewestTime = newestTime;
            drawnHasData = HasData;

            UpdateAxisLabels(plot);
            liveMarker.MarkDirtyRepaint();   // colors may have changed
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

        /// <summary>Moves the live marker and its label with translate only (no layout pass, no tessellation).</summary>
        private void UpdateLivePoint(Rect plot)
        {
            DisplayStyle display = HasData ? DisplayStyle.Flex : DisplayStyle.None;
            if (liveMarker.resolvedStyle.display != display)
            {
                liveMarker.style.display = display;
                liveLabel.style.display = display;
            }
            if (!HasData || plot.width < 4f || plot.height < 4f)
                return;

            float wind = Mathf.Round(LiveWind * 10f) / 10f;
            float power = Mathf.Round(LivePower * 100f) / 100f;
            if (wind != shownLiveWind || power != shownLivePower)
            {
                shownLiveWind = wind;
                shownLivePower = power;
                liveLabel.text = UiFormat.Format("{0:0.0} m/s · {1:0.00} MW", wind, power);
            }

            float x = X(plot, LiveWind);
            float y = Y(plot, LivePower);
            liveMarker.style.translate = new Translate(x - liveMarker.Extent, y - liveMarker.Extent);

            // Beside the point, flipping sides near the right and top edges to stay inside the card. The width is
            // from the last layout; a text change is at most one frame off.
            bool flipLeft = x > plot.xMin + plot.width * 0.55f;
            bool below = y < plot.yMin + 24f;
            float labelX = flipLeft ? x - 10f - liveLabel.layout.width : x + 10f;
            float labelY = below ? y + 8f : y - 22f;
            liveLabel.style.translate = new Translate(Mathf.Round(labelX), Mathf.Round(labelY));
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
            // The live point is the LiveMarker child, drawn on top of this.
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
                painter.MoveTo(center + OctagonCorners[0]);
                for (int corner = 1; corner < OctagonCorners.Length; corner++)
                    painter.LineTo(center + OctagonCorners[corner]);
                painter.ClosePath();
            }
            painter.Fill();
        }

        private static Vector2[] BuildOctagon(float radius)
        {
            var corners = new Vector2[8];
            for (int i = 0; i < corners.Length; i++)
            {
                float angle = (i + 0.5f) * Mathf.PI / 4f;
                corners[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return corners;
        }

        /// <summary>
        /// Live operating point (halo + dot with a surface ring), drawn once into a small box and then only moved.
        /// Uses the chart's palette.
        /// </summary>
        private sealed class LiveMarker : VisualElement
        {
            private readonly PowerCurveChart chart;
            private readonly float radius;

            public LiveMarker(PowerCurveChart chart, float radius)
            {
                this.chart = chart;
                this.radius = radius;
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0f;
                style.top = 0f;
                style.width = Extent * 2f;
                style.height = Extent * 2f;
                generateVisualContent += Draw;
            }

            /// <summary>Half the box size: the halo radius.</summary>
            public float Extent => radius * 2f;

            private void Draw(MeshGenerationContext context)
            {
                Painter2D painter = context.painter2D;
                var center = new Vector2(Extent, Extent);
                Color series = chart.SeriesColor;

                // Halo + dot: the one thing on this chart the eye should find first.
                FillCircle(painter, center, Extent, new Color(series.r, series.g, series.b, 0.18f));
                chart.DrawDot(painter, center, radius, series);
            }
        }
    }
}
