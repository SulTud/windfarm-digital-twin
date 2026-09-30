using UnityEngine;
using UnityEngine.UIElements;
using WindFarm.Cameras;

namespace WindFarm.UI
{
    /// <summary>
    /// Turns pointer input on the scene into camera commands: one finger or mouse drag orbits, pinch or the mouse
    /// wheel zooms, a double tap / double click returns to the home view.
    ///
    /// Listens on the full-screen "scene-input" element that lies under the whole dashboard. UI Toolkit hands a press
    /// to the topmost pickable element, so a drag that starts on a card, the slider or the bottom sheet goes to that
    /// control and never reaches this surface: no separate "is the pointer over UI" check is needed. Mouse and touch
    /// arrive as the same pointer events, positions are in logical px (same feel on every pixel density).
    /// </summary>
    internal sealed class CameraGestures
    {
        // Finger travel (logical px) before a press becomes an orbit; below it a press is a tap.
        private const float DragStartDistance = 6f;
        private const float WheelZoomStep = 1.15f;        // distance factor per wheel notch
        private const long DoubleTapTime = 300;           // ms between the two taps
        private const float DoubleTapDistance = 30f;      // logical px between the two taps

        private readonly VisualElement surface;
        private readonly TurbineOrbitCamera orbitCamera;

        private int primaryId = PointerId.invalidPointerId;
        private int secondaryId = PointerId.invalidPointerId;
        private Vector2 primaryPosition;
        private Vector2 secondaryPosition;
        private Vector2 pressPosition;
        private float pinchDistance;
        private bool dragging;                            // this press orbited or pinched: it is not a tap

        private long lastTapTime = long.MinValue / 2;
        private Vector2 lastTapPosition;

        public CameraGestures(VisualElement surface, TurbineOrbitCamera orbitCamera)
        {
            this.surface = surface;
            this.orbitCamera = orbitCamera;

            surface.RegisterCallback<PointerDownEvent>(HandlePointerDown);
            surface.RegisterCallback<PointerMoveEvent>(HandlePointerMove);
            surface.RegisterCallback<PointerUpEvent>(HandlePointerUp);
            surface.RegisterCallback<PointerCancelEvent>(HandlePointerCancel);
            surface.RegisterCallback<PointerCaptureOutEvent>(HandleCaptureOut);
            surface.RegisterCallback<WheelEvent>(HandleWheel);
        }

        public void Dispose()
        {
            surface.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
            surface.UnregisterCallback<PointerMoveEvent>(HandlePointerMove);
            surface.UnregisterCallback<PointerUpEvent>(HandlePointerUp);
            surface.UnregisterCallback<PointerCancelEvent>(HandlePointerCancel);
            surface.UnregisterCallback<PointerCaptureOutEvent>(HandleCaptureOut);
            surface.UnregisterCallback<WheelEvent>(HandleWheel);
            Release(primaryId);
            Release(secondaryId);
            primaryId = secondaryId = PointerId.invalidPointerId;
        }

        private void HandlePointerDown(PointerDownEvent evt)
        {
            Vector2 position = evt.position;
            if (primaryId == PointerId.invalidPointerId)
            {
                primaryId = evt.pointerId;
                primaryPosition = position;
                pressPosition = position;
                dragging = false;
            }
            else if (secondaryId == PointerId.invalidPointerId && evt.pointerId != primaryId)
            {
                // Second finger: pinch. The press is no longer a tap.
                secondaryId = evt.pointerId;
                secondaryPosition = position;
                pinchDistance = Vector2.Distance(primaryPosition, secondaryPosition);
                dragging = true;
            }
            else
            {
                return;
            }

            // Keep receiving moves when the finger slides over a card or leaves the canvas.
            surface.CapturePointer(evt.pointerId);
        }

        private void HandlePointerMove(PointerMoveEvent evt)
        {
            Vector2 position = evt.position;
            if (evt.pointerId == primaryId)
            {
                Vector2 delta = position - primaryPosition;
                primaryPosition = position;

                if (secondaryId != PointerId.invalidPointerId)
                {
                    UpdatePinch();
                    return;
                }

                if (!dragging)
                {
                    if (Vector2.Distance(position, pressPosition) < DragStartDistance)
                        return;
                    dragging = true;
                }

                orbitCamera.Orbit(delta);
            }
            else if (evt.pointerId == secondaryId)
            {
                secondaryPosition = position;
                UpdatePinch();
            }
        }

        private void UpdatePinch()
        {
            float distance = Vector2.Distance(primaryPosition, secondaryPosition);
            if (distance > 1f && pinchDistance > 1f)
                orbitCamera.Zoom(pinchDistance / distance);   // fingers apart -> closer
            pinchDistance = distance;
        }

        private void HandlePointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId == primaryId && !dragging)
                HandleTap(evt.position, evt.timestamp);

            EndPointer(evt.pointerId);
        }

        private void HandlePointerCancel(PointerCancelEvent evt) => EndPointer(evt.pointerId);

        private void HandleCaptureOut(PointerCaptureOutEvent evt) => EndPointer(evt.pointerId);

        private void EndPointer(int pointerId)
        {
            if (pointerId == primaryId)
            {
                // The remaining finger carries on orbiting from where it is, without a jump.
                primaryId = secondaryId;
                primaryPosition = secondaryPosition;
                secondaryId = PointerId.invalidPointerId;
            }
            else if (pointerId == secondaryId)
            {
                secondaryId = PointerId.invalidPointerId;
            }
            else
            {
                return;
            }

            Release(pointerId);
        }

        private void Release(int pointerId)
        {
            if (pointerId != PointerId.invalidPointerId && surface.HasPointerCapture(pointerId))
                surface.ReleasePointer(pointerId);
        }

        private void HandleTap(Vector2 position, long timestamp)
        {
            if (timestamp - lastTapTime <= DoubleTapTime && Vector2.Distance(position, lastTapPosition) <= DoubleTapDistance)
            {
                orbitCamera.ResetView();
                lastTapTime = long.MinValue / 2;   // a third tap starts a new pair
                return;
            }

            lastTapTime = timestamp;
            lastTapPosition = position;
        }

        private void HandleWheel(WheelEvent evt)
        {
            // Wheel deltas differ a lot between platforms and browsers (one notch can be 1, 3 or 100). A notch counts
            // as one step at most; smaller trackpad deltas give partial steps.
            float steps = Mathf.Clamp(evt.delta.y, -1f, 1f);
            if (steps != 0f)
                orbitCamera.Zoom(Mathf.Pow(WheelZoomStep, steps));   // wheel down / toward the user -> farther
            evt.StopPropagation();
        }
    }
}
