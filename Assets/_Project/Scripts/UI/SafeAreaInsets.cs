using System;
using UnityEngine;

namespace WindFarm.UI
{
    /// <summary>
    /// Screen edges covered by a notch, rounded corners or the home indicator, in logical (CSS-like) pixels.
    /// Values are rounded to whole pixels, so tiny float changes do not trigger a relayout.
    /// </summary>
    public readonly struct SafeAreaInsets : IEquatable<SafeAreaInsets>
    {
        public SafeAreaInsets(float top, float right, float bottom, float left)
        {
            Top = Clean(top);
            Right = Clean(right);
            Bottom = Clean(bottom);
            Left = Clean(left);
        }

        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }
        public float Left { get; }

        public bool Equals(SafeAreaInsets other) =>
            Top == other.Top && Right == other.Right && Bottom == other.Bottom && Left == other.Left;

        public override bool Equals(object obj) => obj is SafeAreaInsets other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Top, Right, Bottom, Left);

        public override string ToString() => UiFormat.Format("top {0} right {1} bottom {2} left {3}", Top, Right, Bottom, Left);

        private static float Clean(float value) => float.IsNaN(value) ? 0f : Mathf.Max(0f, Mathf.Round(value));
    }
}
