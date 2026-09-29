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
    /// Base for the Painter2D charts: plot area with room for tick labels, palette from USS custom properties on
    /// .chart-canvas (read via CustomStyleResolvedEvent), label factory and shared drawing helpers.
    /// Subclasses draw in <see cref="DrawContent"/> and move their labels outside the draw callback, because styles
    /// must not change while the mesh is generated.
    /// </summary>
    internal abstract class ChartElement : VisualElement
    {
        protected const float PadLeft = 30f;     // room for the y tick labels
        protected const float PadRight = 8f;
        protected const float PadTop = 8f;
        protected const float PadBottom = 18f;   // room for the x labels

        private const float DashLength = 4f;
        private const float GapLength = 4f;

        private static readonly CustomStyleProperty<Color> SeriesColorProperty = new CustomStyleProperty<Color>("--chart-series");
        private static readonly CustomStyleProperty<Color> GridColorProperty = new CustomStyleProperty<Color>("--chart-grid");
        private static readonly CustomStyleProperty<Color> MutedColorProperty = new CustomStyleProperty<Color>("--chart-muted");
        private static readonly CustomStyleProperty<Color> CurveColorProperty = new CustomStyleProperty<Color>("--chart-curve");
        private static readonly CustomStyleProperty<Color> WarningColorProperty = new CustomStyleProperty<Color>("--chart-warn");
        private static readonly CustomStyleProperty<Color> CriticalColorProperty = new CustomStyleProperty<Color>("--chart-crit");
        private static readonly CustomStyleProperty<Color> SurfaceColorProperty = new CustomStyleProperty<Color>("--chart-surface");

        protected Color SeriesColor { get; private set; } = new Color(0.36f, 0.78f, 0.88f);
        protected Color GridColor { get; private set; } = new Color(0.55f, 0.64f, 0.68f, 0.16f);
        protected Color MutedColor { get; private set; } = new Color(0.55f, 0.64f, 0.68f);
        protected Color CurveColor { get; private set; } = new Color(0.72f, 0.78f, 0.81f);
        protected Color WarningColor { get; private set; } = new Color(0.94f, 0.69f, 0.24f);
        protected Color CriticalColor { get; private set; } = new Color(0.94f, 0.35f, 0.29f);
        protected Color SurfaceColor { get; private set; } = new Color(0.07f, 0.14f, 0.18f);

        protected ChartElement()
        {
            AddToClassList("chart-canvas");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(HandleCustomStyle);
        }

        protected Rect PlotRect
        {
            get
            {
                Rect content = contentRect;
                return new Rect(PadLeft, PadTop,
                    Mathf.Max(0f, content.width - PadLeft - PadRight),
                    Mathf.Max(0f, content.height - PadTop - PadBottom));
            }
        }

        /// <summary>Set when the palette changed; the subclass repaints on its next Refresh.</summary>
        protected bool StyleChanged { get; set; } = true;

        protected abstract void DrawContent(Painter2D painter, Rect plot);

        /// <summary>
        /// True if the chart is on screen: attached, not inside a display:none page and not hidden (closed bottom
        /// sheet). Painter2D tessellates on the CPU (single-threaded in WebGL), so hidden charts must not repaint.
        /// </summary>
        protected bool IsShown()
        {
            if (panel == null || resolvedStyle.visibility != Visibility.Visible)   // visibility is inherited
                return false;

            for (VisualElement element = this; element != null; element = element.parent)
            {
                if (element.resolvedStyle.display == DisplayStyle.None)
                    return false;
            }
            return true;
        }

        protected Label AddLabel(string classes)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            foreach (string className in classes.Split(' '))
                label.AddToClassList(className);
            Add(label);
            return label;
        }

        protected Color ColorOf(ReferenceLineKind kind)
        {
            switch (kind)
            {
                case ReferenceLineKind.Accent: return new Color(SeriesColor.r, SeriesColor.g, SeriesColor.b, 0.6f);
                case ReferenceLineKind.Warning: return WarningColor;
                case ReferenceLineKind.Critical: return CriticalColor;
                default: return MutedColor;
            }
        }

        protected static void DashedHorizontal(Painter2D painter, float y, float xMin, float xMax)
        {
            y = Mathf.Round(y) + 0.5f;
            painter.BeginPath();
            for (float x = xMin; x < xMax; x += DashLength + GapLength)
            {
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(Mathf.Min(x + DashLength, xMax), y));
            }
            painter.Stroke();
        }

        protected static void DashedVertical(Painter2D painter, float x, float yMin, float yMax)
        {
            x = Mathf.Round(x) + 0.5f;
            painter.BeginPath();
            for (float y = yMin; y < yMax; y += DashLength + GapLength)
            {
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x, Mathf.Min(y + DashLength, yMax)));
            }
            painter.Stroke();
        }

        /// <summary>Filled dot with a surface-colored ring, so it stays readable where it overlaps a line.</summary>
        protected void DrawDot(Painter2D painter, Vector2 center, float radius, Color color)
        {
            FillCircle(painter, center, radius + 2f, SurfaceColor);
            FillCircle(painter, center, radius, color);
        }

        protected static void FillCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f), new Angle(360f));
            painter.Fill();
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect plot = PlotRect;
            if (plot.width < 4f || plot.height < 4f)
                return;

            Painter2D painter = context.painter2D;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Butt;
            DrawContent(painter, plot);
        }

        private void HandleCustomStyle(CustomStyleResolvedEvent evt)
        {
            ICustomStyle custom = evt.customStyle;
            if (custom.TryGetValue(SeriesColorProperty, out Color series)) SeriesColor = series;
            if (custom.TryGetValue(GridColorProperty, out Color grid)) GridColor = grid;
            if (custom.TryGetValue(MutedColorProperty, out Color muted)) MutedColor = muted;
            if (custom.TryGetValue(CurveColorProperty, out Color curve)) CurveColor = curve;
            if (custom.TryGetValue(WarningColorProperty, out Color warning)) WarningColor = warning;
            if (custom.TryGetValue(CriticalColorProperty, out Color critical)) CriticalColor = critical;
            if (custom.TryGetValue(SurfaceColorProperty, out Color surface)) SurfaceColor = surface;
            StyleChanged = true;
            MarkDirtyRepaint();
        }
    }
}
