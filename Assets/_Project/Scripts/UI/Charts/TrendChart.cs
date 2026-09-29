using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    internal enum ReferenceLineKind
    {
        Accent,
        Muted,
        Warning,
        Critical,
    }

    /// <summary>
    /// Scrolling time-series chart drawn with Painter2D: grid, dashed reference lines, area + line of one series over a
    /// fixed time window, an emphasized live endpoint, and a crosshair with a tooltip on hover / touch.
    ///
    /// The presenter sets the data and scale every frame and calls <see cref="Refresh"/>. Labels (ticks, tooltip) are
    /// child Labels positioned here, outside the draw callback, because styles must not change while drawing.
    /// Colors come from USS custom properties on the element (see .chart-canvas), so the palette stays in the stylesheet.
    /// </summary>
    internal sealed class TrendChart : VisualElement
    {
        private const float PadLeft = 30f;     // room for the y tick labels
        private const float PadRight = 8f;
        private const float PadTop = 8f;
        private const float PadBottom = 18f;   // room for the x labels
        private const float MinPointSpacing = 1.5f; // px; skip denser samples (several per pixel add nothing)
        private const float DashLength = 4f;
        private const float GapLength = 4f;
        private const int MaxTicks = 8;

        private static readonly CustomStyleProperty<Color> SeriesColorProperty = new CustomStyleProperty<Color>("--chart-series");
        private static readonly CustomStyleProperty<Color> GridColorProperty = new CustomStyleProperty<Color>("--chart-grid");
        private static readonly CustomStyleProperty<Color> MutedColorProperty = new CustomStyleProperty<Color>("--chart-muted");
        private static readonly CustomStyleProperty<Color> WarningColorProperty = new CustomStyleProperty<Color>("--chart-warn");
        private static readonly CustomStyleProperty<Color> CriticalColorProperty = new CustomStyleProperty<Color>("--chart-crit");
        private static readonly CustomStyleProperty<Color> SurfaceColorProperty = new CustomStyleProperty<Color>("--chart-surface");

        private readonly Label[] yLabels = new Label[MaxTicks];
        private readonly Label[] xLabels = new Label[3];
        private readonly Label tooltipLabel;
        private readonly List<Vector2> points = new List<Vector2>(512);
        private readonly List<float> tickValues = new List<float>(MaxTicks);

        private Color seriesColor = new Color(0.36f, 0.78f, 0.88f);
        private Color gridColor = new Color(0.55f, 0.64f, 0.68f, 0.16f);
        private Color mutedColor = new Color(0.55f, 0.64f, 0.68f);
        private Color warningColor = new Color(0.94f, 0.69f, 0.24f);
        private Color criticalColor = new Color(0.94f, 0.35f, 0.29f);
        private Color surfaceColor = new Color(0.07f, 0.14f, 0.18f);

        private float pointerX = float.NaN;

        public TrendChart()
        {
            AddToClassList("chart-canvas");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(HandleCustomStyle);

            for (int i = 0; i < yLabels.Length; i++)
                yLabels[i] = AddLabel("chart__tick chart__tick--y");
            for (int i = 0; i < xLabels.Length; i++)
                xLabels[i] = AddLabel("chart__tick chart__tick--x");
            xLabels[0].text = "-5 min";
            xLabels[1].text = "-2.5";
            xLabels[2].text = "now";

            tooltipLabel = AddLabel("chart__tooltip");
            tooltipLabel.style.display = DisplayStyle.None;

            // Mouse hover on PC; on touch screens the pointer only exists while the finger is down.
            RegisterCallback<PointerMoveEvent>(evt => pointerX = evt.localPosition.x);
            RegisterCallback<PointerDownEvent>(evt => pointerX = evt.localPosition.x);
            RegisterCallback<PointerLeaveEvent>(_ => pointerX = float.NaN);
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerType != "mouse")
                    pointerX = float.NaN;
            });
        }

        // ---- Data set by the presenter before Refresh() ----

        public TelemetryHistory History { get; set; }
        public Func<TelemetrySample, float> ValueOf { get; set; }
        public double WindowSeconds { get; set; } = 300.0;
        public double Now { get; set; }
        public float HeadValue { get; set; }
        public bool HasData { get; set; }
        public float Min { get; set; }
        public float Max { get; set; } = 1f;
        public float TickStep { get; set; } = 1f;
        public string ValueFormat { get; set; } = "0.00";
        public string Unit { get; set; } = "";
        public List<(float value, ReferenceLineKind kind)> ReferenceLines { get; } = new List<(float, ReferenceLineKind)>(4);

        private Rect PlotRect
        {
            get
            {
                Rect content = contentRect;
                return new Rect(PadLeft, PadTop,
                    Mathf.Max(0f, content.width - PadLeft - PadRight),
                    Mathf.Max(0f, content.height - PadTop - PadBottom));
            }
        }

        private double WindowStart => Now - WindowSeconds;

        /// <summary>Repositions the labels for the current scale and size, then repaints.</summary>
        public void Refresh()
        {
            UpdateTickLabels();
            UpdateTooltip();
            MarkDirtyRepaint();
        }

        private Label AddLabel(string classes)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            foreach (string className in classes.Split(' '))
                label.AddToClassList(className);
            Add(label);
            return label;
        }

        private void HandleCustomStyle(CustomStyleResolvedEvent evt)
        {
            ICustomStyle custom = evt.customStyle;
            if (custom.TryGetValue(SeriesColorProperty, out Color series)) seriesColor = series;
            if (custom.TryGetValue(GridColorProperty, out Color grid)) gridColor = grid;
            if (custom.TryGetValue(MutedColorProperty, out Color muted)) mutedColor = muted;
            if (custom.TryGetValue(WarningColorProperty, out Color warning)) warningColor = warning;
            if (custom.TryGetValue(CriticalColorProperty, out Color critical)) criticalColor = critical;
            if (custom.TryGetValue(SurfaceColorProperty, out Color surface)) surfaceColor = surface;
            MarkDirtyRepaint();
        }

        // ---- Scale ----

        private float X(Rect plot, double time) => plot.xMin + (float)((time - WindowStart) / WindowSeconds) * plot.width;

        private float Y(Rect plot, float value) =>
            plot.yMax - Mathf.Clamp01((value - Min) / Mathf.Max(0.0001f, Max - Min)) * plot.height;

        private void CollectTicks()
        {
            tickValues.Clear();
            if (TickStep <= 0f)
                return;

            float first = Mathf.Ceil(Min / TickStep) * TickStep;
            for (float value = first; value <= Max + 0.0001f && tickValues.Count < MaxTicks; value += TickStep)
                tickValues.Add(value);
        }

        private void UpdateTickLabels()
        {
            Rect plot = PlotRect;
            CollectTicks();

            for (int i = 0; i < yLabels.Length; i++)
            {
                Label label = yLabels[i];
                bool used = i < tickValues.Count;
                label.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used)
                    continue;

                label.text = tickValues[i].ToString("0.#", UiFormat.Invariant);
                label.style.top = Y(plot, tickValues[i]) - 7f;
                label.style.width = PadLeft - 6f;
            }

            float labelTop = plot.yMax + 3f;
            for (int i = 0; i < xLabels.Length; i++)
            {
                xLabels[i].style.top = labelTop;
                xLabels[i].style.left = plot.xMin + plot.width * i / (xLabels.Length - 1);
            }
        }

        private void UpdateTooltip()
        {
            Rect plot = PlotRect;
            if (!TryGetPointerTime(plot, out double time))
            {
                tooltipLabel.style.display = DisplayStyle.None;
                return;
            }

            float value = ValueAt(time);
            double ago = Math.Max(0.0, Now - time);
            int minutes = (int)(ago / 60.0);
            int seconds = (int)(ago % 60.0);

            tooltipLabel.text = UiFormat.Format("-{0}:{1:00} · {2} {3}", minutes, seconds,
                value.ToString(ValueFormat, UiFormat.Invariant), Unit);
            tooltipLabel.style.display = DisplayStyle.Flex;

            // Flip to the left of the crosshair near the right edge so the tooltip stays inside the card.
            bool flip = pointerX > plot.xMin + plot.width * 0.6f;
            tooltipLabel.style.left = flip ? StyleKeyword.Auto : new StyleLength(pointerX + 8f);
            tooltipLabel.style.right = flip ? new StyleLength(contentRect.width - pointerX + 8f) : StyleKeyword.Auto;
        }

        private double TimeAt(Rect plot, float x) => WindowStart + (x - plot.xMin) / plot.width * WindowSeconds;

        /// <summary>
        /// True if the pointer is over the plot where a line is drawn. Before the oldest sample (window not filled yet)
        /// there is no data, and showing the oldest value there would invent readings.
        /// </summary>
        private bool TryGetPointerTime(Rect plot, out double time)
        {
            time = 0.0;
            if (!HasData || float.IsNaN(pointerX) || pointerX < plot.xMin || pointerX > plot.xMax)
                return false;

            time = TimeAt(plot, pointerX);
            TelemetryHistory history = History;
            return history != null && history.Count > 0 && time >= history[0].Time;
        }

        /// <summary>Series value at a time, interpolated between stored samples; the live head covers the newest gap.</summary>
        private float ValueAt(double time)
        {
            TelemetryHistory history = History;
            int count = history?.Count ?? 0;
            if (count == 0 || time >= history[count - 1].Time)
            {
                if (count == 0)
                    return HeadValue;

                TelemetrySample newest = history[count - 1];
                return Lerp(newest.Time, ValueOf(newest), Now, HeadValue, time);
            }

            int index = history.LowerBound(time);
            if (index <= 0)
                return ValueOf(history[0]);

            TelemetrySample before = history[index - 1];
            TelemetrySample after = history[index];
            return Lerp(before.Time, ValueOf(before), after.Time, ValueOf(after), time);
        }

        private static float Lerp(double t0, float v0, double t1, float v1, double t)
        {
            if (t1 - t0 <= 1e-6)
                return v1;
            return Mathf.Lerp(v0, v1, (float)((t - t0) / (t1 - t0)));
        }

        // ---- Drawing ----

        private void Draw(MeshGenerationContext context)
        {
            Rect plot = PlotRect;
            if (plot.width < 4f || plot.height < 4f)
                return;

            Painter2D painter = context.painter2D;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Butt;

            DrawGrid(painter, plot);
            DrawReferenceLines(painter, plot);

            if (!HasData || ValueOf == null)
                return;

            BuildPoints(plot);
            if (points.Count < 2)
                return;

            DrawArea(painter, plot);
            DrawLine(painter);
            DrawDot(painter, points[points.Count - 1], 3.5f);
            DrawCrosshair(painter, plot);
        }

        private void DrawGrid(Painter2D painter, Rect plot)
        {
            painter.lineWidth = 1f;
            painter.strokeColor = gridColor;
            painter.BeginPath();
            foreach (float value in tickValues)
            {
                float y = Mathf.Round(Y(plot, value)) + 0.5f;
                painter.MoveTo(new Vector2(plot.xMin, y));
                painter.LineTo(new Vector2(plot.xMax, y));
            }
            painter.Stroke();
        }

        private void DrawReferenceLines(Painter2D painter, Rect plot)
        {
            painter.lineWidth = 1f;
            foreach ((float value, ReferenceLineKind kind) in ReferenceLines)
            {
                if (value < Min || value > Max)
                    continue;

                painter.strokeColor = ColorOf(kind);
                float y = Mathf.Round(Y(plot, value)) + 0.5f;
                painter.BeginPath();
                for (float x = plot.xMin; x < plot.xMax; x += DashLength + GapLength)
                {
                    painter.MoveTo(new Vector2(x, y));
                    painter.LineTo(new Vector2(Mathf.Min(x + DashLength, plot.xMax), y));
                }
                painter.Stroke();
            }
        }

        private Color ColorOf(ReferenceLineKind kind)
        {
            switch (kind)
            {
                case ReferenceLineKind.Accent: return new Color(seriesColor.r, seriesColor.g, seriesColor.b, 0.6f);
                case ReferenceLineKind.Warning: return warningColor;
                case ReferenceLineKind.Critical: return criticalColor;
                default: return mutedColor;
            }
        }

        private void BuildPoints(Rect plot)
        {
            points.Clear();
            TelemetryHistory history = History;
            int count = history?.Count ?? 0;
            double windowStart = WindowStart;

            // Start exactly at the left edge: interpolate across the window boundary instead of drawing outside it.
            int first = count > 0 ? history.LowerBound(windowStart) : 0;
            if (first > 0)
                points.Add(new Vector2(plot.xMin, Y(plot, ValueAt(windowStart))));

            float lastX = float.NegativeInfinity;
            for (int i = first; i < count; i++)
            {
                TelemetrySample sample = history[i];
                float x = X(plot, sample.Time);
                if (x - lastX < MinPointSpacing)
                    continue;

                points.Add(new Vector2(x, Y(plot, ValueOf(sample))));
                lastX = x;
            }

            // Live head: the smoothed current value at "now" keeps the right end moving between samples.
            points.Add(new Vector2(plot.xMax, Y(plot, HeadValue)));
        }

        private void DrawArea(Painter2D painter, Rect plot)
        {
            painter.fillColor = new Color(seriesColor.r, seriesColor.g, seriesColor.b, 0.14f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(points[0].x, plot.yMax));
            foreach (Vector2 point in points)
                painter.LineTo(point);
            painter.LineTo(new Vector2(points[points.Count - 1].x, plot.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private void DrawLine(Painter2D painter)
        {
            painter.lineWidth = 2f;
            painter.strokeColor = seriesColor;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Count; i++)
                painter.LineTo(points[i]);
            painter.Stroke();
        }

        private void DrawDot(Painter2D painter, Vector2 center, float radius)
        {
            // Surface ring first so the dot stays readable where it overlaps the line.
            painter.fillColor = surfaceColor;
            painter.BeginPath();
            painter.Arc(center, radius + 2f, new Angle(0f), new Angle(360f));
            painter.Fill();

            painter.fillColor = seriesColor;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f), new Angle(360f));
            painter.Fill();
        }

        private void DrawCrosshair(Painter2D painter, Rect plot)
        {
            if (!TryGetPointerTime(plot, out double time))
                return;

            painter.lineWidth = 1f;
            painter.strokeColor = mutedColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(pointerX, plot.yMin));
            painter.LineTo(new Vector2(pointerX, plot.yMax));
            painter.Stroke();

            DrawDot(painter, new Vector2(pointerX, Y(plot, ValueAt(time))), 3f);
        }
    }
}
