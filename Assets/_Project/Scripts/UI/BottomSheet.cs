using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    /// <summary>
    /// Phone bottom sheet with the Trends / Curve / More pages. Closed ("peek") it shows only the handle and the tabs;
    /// tapping a tab opens it on that page, tapping the active tab or the header closes it. Dragging the header moves
    /// the sheet with the finger; on release it settles open or closed by the flick direction, or by position for a
    /// slow release (like the iOS sheets). Every action also works with a plain tap.
    ///
    /// Open / closed is one class on the sheet (sheet--open) plus a translate by <see cref="OpenOffset"/>; USS plays
    /// the settle transition, like setting an Animator bool and letting the state transition animate. While dragging,
    /// sheet--dragging turns the transition off so the sheet sticks to the finger.
    /// </summary>
    internal sealed class BottomSheet
    {
        private const string OpenClass = "sheet--open";
        private const string DraggingClass = "sheet--dragging";
        private const string TabOnClass = "tab--on";
        private const string PageOnClass = "sheet__page--on";

        // Finger travel (logical px) before a press becomes a drag. Small, so the sheet follows right away, but above
        // the jitter of a tap.
        private const float DragStartDistance = 6f;

        // Release speed (logical px/s) that counts as a flick: the sheet goes the way it was thrown, wherever it is.
        private const float FlickSpeed = 400f;

        // Blend factor for the finger velocity estimate (smooths uneven touch event timing).
        private const float VelocitySmoothing = 0.5f;

        private static readonly string[] TabNames = { "sheet-tab-trends", "sheet-tab-curve", "sheet-tab-more" };
        private static readonly string[] PageNames = { "sheet-page-trends", "sheet-page-curve", "sheet-page-more" };

        private readonly VisualElement sheet;
        private readonly VisualElement header;
        private readonly Button[] tabs = new Button[TabNames.Length];
        private readonly VisualElement[] pages = new VisualElement[PageNames.Length];
        private readonly Action[] tabHandlers = new Action[TabNames.Length];

        private float openOffset = 260f;   // USS default; DashboardLayout measures the real value
        private int selectedPage;

        private int trackedPointerId = PointerId.invalidPointerId;
        private float pointerStartY;
        private float offsetAtPress;       // sheet translate (0 closed, -openOffset open) when the finger went down
        private float dragOffset;          // current translate while dragging
        private float lastPointerY;
        private long lastPointerTime;      // ms (event timestamp)
        private float velocity;            // px/s, negative = upward
        private bool dragging;
        private bool dragged;              // this press became a drag: the click that follows must not act

        public BottomSheet(VisualElement root)
        {
            sheet = root.Require<VisualElement>("sheet");
            header = root.Require<VisualElement>("sheet-header");

            for (int i = 0; i < tabs.Length; i++)
            {
                int page = i;
                tabs[i] = root.Require<Button>(TabNames[i]);
                pages[i] = root.Require<VisualElement>(PageNames[i]);
                tabHandlers[i] = () => HandleTab(page);
                tabs[i].clicked += tabHandlers[i];
            }

            // TrickleDown: the header sees the pointer before a tab button handles it, so a drag that starts on a
            // tab still moves the sheet. A pressed tab captures the pointer; moves still pass the header on the way.
            header.RegisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
            header.RegisterCallback<PointerMoveEvent>(HandlePointerMove, TrickleDown.TrickleDown);
            header.RegisterCallback<PointerUpEvent>(HandlePointerUp, TrickleDown.TrickleDown);
            header.RegisterCallback<PointerCancelEvent>(HandlePointerCancel, TrickleDown.TrickleDown);
            header.RegisterCallback<PointerCaptureOutEvent>(HandleCaptureOut);
            header.RegisterCallback<ClickEvent>(HandleHeaderClicked);

            SelectPage(0);
            ApplyTranslate();
        }

        public bool IsOpen => sheet.ClassListContains(OpenClass);

        /// <summary>How far the sheet slides up when open (logical px): the content height plus its top margin.</summary>
        public float OpenOffset
        {
            get => openOffset;
            set
            {
                openOffset = value;
                ApplyTranslate();
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < tabs.Length; i++)
                tabs[i].clicked -= tabHandlers[i];

            header.UnregisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
            header.UnregisterCallback<PointerMoveEvent>(HandlePointerMove, TrickleDown.TrickleDown);
            header.UnregisterCallback<PointerUpEvent>(HandlePointerUp, TrickleDown.TrickleDown);
            header.UnregisterCallback<PointerCancelEvent>(HandlePointerCancel, TrickleDown.TrickleDown);
            header.UnregisterCallback<PointerCaptureOutEvent>(HandleCaptureOut);
            header.UnregisterCallback<ClickEvent>(HandleHeaderClicked);
            EndPress();
        }

        public void SetOpen(bool open)
        {
            sheet.EnableInClassList(OpenClass, open);
            ApplyTranslate();
        }

        // Inline because the offset is measured; the USS transition on translate animates the change.
        private void ApplyTranslate() => SetTranslate(IsOpen ? -openOffset : 0f);

        private void SetTranslate(float offset) => sheet.style.translate = new Translate(0f, offset);

        // ---- Taps ----

        private void HandleTab(int page)
        {
            if (dragged)
                return;

            if (IsOpen && page == selectedPage)
            {
                SetOpen(false);
                return;
            }

            SelectPage(page);
            SetOpen(true);
        }

        private void SelectPage(int page)
        {
            selectedPage = page;
            for (int i = 0; i < pages.Length; i++)
            {
                tabs[i].EnableInClassList(TabOnClass, i == page);
                pages[i].EnableInClassList(PageOnClass, i == page);
            }
        }

        private void HandleHeaderClicked(ClickEvent evt)
        {
            // Tabs have their own handler; this is a tap on the handle or the empty part of the header.
            if (dragged || evt.target is Button)
                return;

            SetOpen(!IsOpen);
        }

        // ---- Drag ----

        private void HandlePointerDown(PointerDownEvent evt)
        {
            if (trackedPointerId != PointerId.invalidPointerId)
                return;   // a second finger does not take over the sheet

            trackedPointerId = evt.pointerId;
            pointerStartY = evt.position.y;
            lastPointerY = evt.position.y;
            lastPointerTime = evt.timestamp;
            velocity = 0f;
            offsetAtPress = IsOpen ? -openOffset : 0f;
            dragging = false;
            dragged = false;

            // Keep receiving moves after the finger leaves the thin header. A pressed tab captures the pointer
            // itself (it needs that to click) and only loses it once the press turns into a drag.
            if (!(evt.target is Button))
                header.CapturePointer(evt.pointerId);
        }

        private void HandlePointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != trackedPointerId)
                return;

            float y = evt.position.y;   // panel space: y grows downward
            TrackVelocity(y, evt.timestamp);

            if (!dragging)
            {
                if (Mathf.Abs(y - pointerStartY) < DragStartDistance)
                    return;

                // Taking the capture from a pressed tab cancels its click.
                dragging = true;
                dragged = true;
                header.CapturePointer(evt.pointerId);
                sheet.AddToClassList(DraggingClass);
            }

            dragOffset = Mathf.Clamp(offsetAtPress + (y - pointerStartY), -openOffset, 0f);
            SetTranslate(dragOffset);
        }

        private void TrackVelocity(float y, long timestamp)
        {
            long elapsed = timestamp - lastPointerTime;
            if (elapsed <= 0)
                return;

            float sample = (y - lastPointerY) / (elapsed / 1000f);
            velocity += (sample - velocity) * VelocitySmoothing;
            lastPointerY = y;
            lastPointerTime = timestamp;
        }

        private void HandlePointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != trackedPointerId)
                return;

            if (dragging)
            {
                // A finger that rested before lifting has no speed left: then the position decides.
                if (evt.timestamp - lastPointerTime > 100)
                    velocity = 0f;

                bool open = Mathf.Abs(velocity) >= FlickSpeed ? velocity < 0f : dragOffset < -openOffset * 0.5f;
                Settle(open);
            }

            EndPress();
        }

        private void HandlePointerCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId != trackedPointerId)
                return;

            if (dragging)
                Settle(dragOffset < -openOffset * 0.5f);
            EndPress();
        }

        // The system took the pointer away (e.g. another element captured it): do not leave the sheet half open.
        private void HandleCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!dragging || evt.pointerId != trackedPointerId)
                return;

            Settle(dragOffset < -openOffset * 0.5f);
            EndPress();
        }

        /// <summary>Re-enables the transition first, so the sheet glides from the finger position to its rest.</summary>
        private void Settle(bool open)
        {
            dragging = false;
            sheet.RemoveFromClassList(DraggingClass);
            SetOpen(open);
        }

        private void EndPress()
        {
            int pointerId = trackedPointerId;
            trackedPointerId = PointerId.invalidPointerId;
            dragging = false;
            sheet.RemoveFromClassList(DraggingClass);

            if (pointerId != PointerId.invalidPointerId && header.HasPointerCapture(pointerId))
                header.ReleasePointer(pointerId);
        }
    }
}
