using UnityEngine;

namespace WindFarm.Cameras
{
    /// <summary>
    /// Math for showing an object inside a sub-rectangle of the screen (the free space between the dashboard panels)
    /// instead of the whole screen.
    ///
    /// The camera keeps looking at its pivot, but the projection is shifted so the pivot appears at the center of the
    /// rectangle (a shift lens, as architecture photographers use; Cinemachine does the same). Orbiting therefore still
    /// turns around the turbine, and only the picture moves. The distance is then chosen so every envelope point
    /// lands inside the rectangle.
    ///
    /// Plain math on Vector3 / Rect / Matrix4x4 fields, so it can be checked in a console harness outside Unity.
    /// </summary>
    public static class CameraFraming
    {
        /// <summary>
        /// Smallest camera distance at which all points fit into the viewport rectangle.
        /// </summary>
        /// <param name="pointsFromPivot">Envelope points relative to the pivot the camera looks at (world axes).</param>
        /// <param name="right">Camera right axis (world).</param>
        /// <param name="up">Camera up axis (world).</param>
        /// <param name="forward">Camera forward axis (world); the camera sits at pivot - forward * distance.</param>
        /// <param name="verticalFov">Vertical field of view (deg).</param>
        /// <param name="aspect">Screen width / height.</param>
        /// <param name="viewport">Target rectangle, normalized (0-1, origin bottom-left like Camera.rect).</param>
        /// <param name="margin">Free border as a fraction of the rectangle's half size (0.1 = 10 %).</param>
        public static float FitDistance(Vector3[] pointsFromPivot, Vector3 right, Vector3 up, Vector3 forward,
            float verticalFov, float aspect, Rect viewport, float margin)
        {
            // A point at depth z is inside the rectangle if |y| <= z * tan(fov/2) * rect height fraction (the rectangle
            // covers that fraction of the screen's normalized device range), same for x with the aspect ratio.
            float tanHalfFov = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
            float fill = 1f - Mathf.Clamp01(margin);
            float slopeY = tanHalfFov * viewport.height * fill;
            float slopeX = tanHalfFov * aspect * viewport.width * fill;
            if (slopeX <= 0f || slopeY <= 0f)
                return 0f;

            float distance = 0f;
            foreach (Vector3 point in pointsFromPivot)
            {
                // Depth of the point = its offset along forward + the camera distance: solve for the distance.
                float x = Vector3.Dot(point, right);
                float y = Vector3.Dot(point, up);
                float z = Vector3.Dot(point, forward);
                distance = Mathf.Max(distance, Mathf.Abs(x) / slopeX - z, Mathf.Abs(y) / slopeY - z);
            }

            return distance;
        }

        /// <summary>
        /// Perspective projection whose optical axis (the pivot) appears at the center of the viewport rectangle.
        /// The camera still renders the full screen; only the picture is offset.
        /// </summary>
        public static Matrix4x4 ShiftedProjection(Matrix4x4 perspective, Rect viewport)
        {
            // OpenGL-style matrix (Camera.projectionMatrix): ndc.x = m00 * x / -z - m02, so m02 = -offset moves the
            // axis to that normalized device offset.
            perspective.m02 = -(2f * viewport.center.x - 1f);
            perspective.m12 = -(2f * viewport.center.y - 1f);
            return perspective;
        }
    }
}
