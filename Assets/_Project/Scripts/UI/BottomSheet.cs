using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    /// <summary>
    /// Phone bottom sheet with the Trends / Curve / More pages. Closed ("peek") it shows only the handle and the tabs;
    /// tapping a tab opens it on that page, tapping the active tab or the header closes it, and a vertical swipe on the
    /// header opens or closes it. Dragging is optional: every action also works with a plain tap.
    ///
    /// Open / closed is one class on the sheet (sheet--open) plus a translate by <see cref="OpenOffset"/>; USS plays
    /// the slide transition, like setting an Animator bool and letting the state transition animate.
    /// </summary>
    internal sealed class BottomSheet
    {
        private const string OpenClass = "sheet--open";
        private const string TabOnClass = "tab--on";
        private const string PageOnClass = "sheet__page--on";

        // Vertical finger travel (logical px) that counts as a swipe instead of a tap. Below a tab's height, so a
        // swipe is recognized while the finger is still on the header.
        private const float SwipeDistance = 16f;

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
        private bool swiped;

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

            // TrickleDown: the header sees the pointer before a tab button handles it, so a swipe that starts on a
            // tab still counts. Tab buttons capture the pointer while pressed; moves then still pass the header.
            header.RegisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
            header.RegisterCallback<PointerMoveEvent>(HandlePointerMove, TrickleDown.TrickleDown);
            header.RegisterCallback<PointerUpEvent>(HandlePointerUp, TrickleDown.TrickleDown);
            header.RegisterCallback<PointerCancelEvent>(HandlePointerCancel, TrickleDown.TrickleDown);
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
            header.UnregisterCallback<ClickEvent>(HandleHeaderClicked);
            StopTracking();
        }

        public void SetOpen(bool open)
        {
            sheet.EnableInClassList(OpenClass, open);
            ApplyTranslate();
        }

        // Inline because the offset is measured; the USS transition on translate still animates the change.
        private void ApplyTranslate() => sheet.style.translate = new Translate(0f, IsOpen ? -openOffset : 0f);

        private void HandleTab(int page)
        {
            // The finger lifted on the tab it started on after a swipe: the swipe already acted.
            if (swiped)
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
            if (swiped || evt.target is Button)
                return;

            SetOpen(!IsOpen);
        }

        private void HandlePointerDown(PointerDownEvent evt)
        {
            trackedPointerId = evt.pointerId;
            pointerStartY = evt.position.y;
            swiped = false;

            // Keep receiving moves after the finger leaves the thin header. Tab buttons capture the pointer themselves
            // (so they can still click); taking it from them would break the tap.
            if (!(evt.target is Button))
                header.CapturePointer(evt.pointerId);
        }

        private void HandlePointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != trackedPointerId)
                return;

            float travel = evt.position.y - pointerStartY;   // panel space: y grows downward
            if (Mathf.Abs(travel) < SwipeDistance)
                return;

            swiped = true;
            StopTracking();
            SetOpen(travel < 0f);
        }

        private void HandlePointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId == trackedPointerId)
                StopTracking();
        }

        private void HandlePointerCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId == trackedPointerId)
                StopTracking();
        }

        private void StopTracking()
        {
            if (trackedPointerId != PointerId.invalidPointerId && header.HasPointerCapture(trackedPointerId))
                header.ReleasePointer(trackedPointerId);
            trackedPointerId = PointerId.invalidPointerId;
        }
    }
}
