using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    /// <summary>
    /// A 3D annotation in the digital twin style: a dot on a part of the turbine, a leader line (a diagonal, then
    /// horizontal) and a label box with a title, a value and an optional note.
    ///
    /// The element itself has zero size and sits at the anchor point; <see cref="MoveTo"/> moves it with
    /// <c>style.translate</c> (no relayout, like the power curve's live marker). The line is drawn with Painter2D and
    /// repainted only when the callout flips side or its <see cref="Lift"/> changes, so following a moving camera
    /// costs nothing. Colors come from the USS (<c>.callout</c>, <c>.callout--warn</c>); the line uses the resolved
    /// text color of its own element.
    /// </summary>
    internal sealed class SceneCallout : VisualElement
    {
        private const float BaseRise = 26f;     // px, diagonal part of the leader line (45 degrees)
        private const float Run = 22f;          // px, horizontal part
        private const float LinePadding = 4f;   // px around the line inside its element
        private const float MoveThreshold = 0.5f;
        private const float LiftThreshold = 1f;
        private const float FallbackBoxHeight = 56f;
        private const float FallbackBoxWidth = 130f;

        private readonly VisualElement line;
        private readonly VisualElement box;
        private readonly Label value;
        private readonly Label note;

        private bool pointsLeft;
        private float lift;
        private bool shown;
        private Vector2 shownPosition = new Vector2(float.NaN, float.NaN);

        public SceneCallout(string titleText, string modifierClass = null)
        {
            AddToClassList("callout");
            if (modifierClass != null)
                AddToClassList(modifierClass);
            pickingMode = PickingMode.Ignore;

            line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("callout__line");
            line.generateVisualContent += DrawLine;
            Add(line);

            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("callout__dot");
            Add(dot);

            box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("callout__box");
            Add(box);

            var title = new Label(titleText) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("callout__title");
            box.Add(title);

            value = new Label { pickingMode = PickingMode.Ignore };
            value.AddToClassList("callout__value");
            box.Add(value);

            note = new Label { pickingMode = PickingMode.Ignore };
            note.AddToClassList("callout__note");
            note.style.display = DisplayStyle.None;
            box.Add(note);

            ApplyGeometry();
        }

        public bool PointsLeft
        {
            get => pointsLeft;
            set
            {
                if (value == pointsLeft)
                    return;
                pointsLeft = value;
                ApplyGeometry();
            }
        }

        /// <summary>Extra rise of the leader line (px), so a second label can stack above another one.</summary>
        public float Lift
        {
            get => lift;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Abs(value - lift) < LiftThreshold)
                    return;
                lift = value;
                ApplyGeometry();
            }
        }

        /// <summary>Label box height (px) once laid out.</summary>
        public float BoxHeight
        {
            get
            {
                float height = box.resolvedStyle.height;
                return float.IsNaN(height) || height <= 0f ? FallbackBoxHeight : height;
            }
        }

        /// <summary>Label box width (px) once laid out.</summary>
        public float BoxWidth
        {
            get
            {
                float width = box.resolvedStyle.width;
                return float.IsNaN(width) || width <= 0f ? FallbackBoxWidth : width;
            }
        }

        /// <summary>
        /// Panel rectangle of the label box for an anchor position, side and lift (the box is vertically centered on
        /// the end of the line, which rises 45 degrees and then runs horizontally).
        /// </summary>
        public Rect BoxRect(Vector2 anchor, bool left, float withLift)
        {
            float rise = BaseRise + Mathf.Max(0f, withLift);
            float end = rise + Run;
            float width = BoxWidth;
            float height = BoxHeight;
            float x = left ? anchor.x - end - width : anchor.x + end;
            return new Rect(x, anchor.y - rise - height * 0.5f, width, height);
        }

        public bool IsShown => shown;

        /// <summary>Fades in or out (USS transition on opacity).</summary>
        public void SetShown(bool visible)
        {
            if (visible == shown)
                return;
            shown = visible;
            EnableInClassList("callout--on", visible);
        }

        public void MoveTo(Vector2 panelPosition)
        {
            if (Mathf.Abs(panelPosition.x - shownPosition.x) < MoveThreshold &&
                Mathf.Abs(panelPosition.y - shownPosition.y) < MoveThreshold)
                return;

            shownPosition = panelPosition;
            style.translate = new Translate(panelPosition.x, panelPosition.y);
        }

        /// <summary>Text setters write only on change (the caller formats; see <see cref="UiFormat"/>).</summary>
        public void SetValue(string text)
        {
            if (value.text != text)
                value.text = text;
        }

        public void SetNote(string text)
        {
            if (note.text == text)
                return;
            note.text = text;
            note.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private float Rise => BaseRise + lift;

        /// <summary>The line element is a box around the anchor that just holds the line, mirrored on both sides.</summary>
        private void ApplyGeometry()
        {
            EnableInClassList("callout--left", pointsLeft);

            float rise = Rise;
            float end = rise + Run;
            line.style.left = -(end + LinePadding);
            line.style.top = -(rise + LinePadding);
            line.style.width = 2f * (end + LinePadding);
            line.style.height = rise + 2f * LinePadding;

            box.style.left = pointsLeft ? StyleKeyword.Auto : new StyleLength(end);
            box.style.right = pointsLeft ? new StyleLength(end) : StyleKeyword.Auto;
            box.style.top = -rise;
            line.MarkDirtyRepaint();
        }

        private void DrawLine(MeshGenerationContext context)
        {
            float rise = Rise;
            float direction = pointsLeft ? -1f : 1f;
            var origin = new Vector2(rise + Run + LinePadding, rise + LinePadding);

            Painter2D painter = context.painter2D;
            painter.strokeColor = line.resolvedStyle.color;
            painter.lineWidth = 1.5f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(origin);
            painter.LineTo(origin + new Vector2(direction * rise, -rise));
            painter.LineTo(origin + new Vector2(direction * (rise + Run), -rise));
            painter.Stroke();
        }
    }
}
