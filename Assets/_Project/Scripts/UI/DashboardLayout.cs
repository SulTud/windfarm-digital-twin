using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    /// <summary>
    /// Moves dashboard elements between containers when the layout switches, for arrangements USS alone cannot express
    /// (like SetParent into another layout group):
    ///   Landscape (PC): the control dock sits centered at the bottom of the free space between the two columns,
    ///                   so it can never overlap the value cards or the charts. The state explanation follows the pill.
    ///   Portrait (phone): the dock becomes a full-width row between the value cards and the bottom sheet. The state
    ///                   explanation becomes the last child of the scene space, so its bubble draws above the X-Ray
    ///                   button (UI Toolkit has no z-index; later siblings draw on top). The trend and power curve
    ///                   cards go into the sheet's Trends / Curve pages; energy, sim speed and the sim clock + data
    ///                   source go into its More page.
    /// Also owns the bottom sheet behavior and keeps the layout out of the notch / home indicator (safe area).
    /// Presenters find their elements by name, so they keep working wherever an element is moved.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument), typeof(ResponsivePanel))]
    public sealed class DashboardLayout : MonoBehaviour
    {
        // The open sheet covers the value cards and the dock completely: its top edge lands this far above the power
        // card. A fixed height left half of the card showing on some phones.
        private const float SheetCoverMargin = 8f;
        private const float SheetContentMarginTop = 12f;   // .sheet__content margin-top in the USS
        private const float MinSheetContentHeight = 220f;
        private const float MaxSheetContentHeight = 420f;

        private UIDocument document;
        private ResponsivePanel responsivePanel;

        private VisualElement dashboard;
        private VisualElement topBar;
        private VisualElement statusGroup;
        private VisualElement stateExplanation;
        private VisualElement meta;
        private VisualElement leftColumn;
        private VisualElement energyCard;
        private VisualElement sceneSpace;
        private VisualElement rightColumn;
        private VisualElement trendCard;
        private VisualElement curveCard;
        private VisualElement dock;
        private VisualElement speedGroup;
        private VisualElement xRayGroup;
        private VisualElement lightsGroup;
        private VisualElement sheet;
        private VisualElement sheetContent;
        private VisualElement trendsPage;
        private VisualElement curvePage;
        private VisualElement moreEnergy;
        private VisualElement moreSpeed;
        private VisualElement moreLights;
        private VisualElement moreMeta;
        private VisualElement sheetSafeArea;

        private BottomSheet bottomSheet;
        private float sheetContentHeight = float.NaN;
        private bool subscribed;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            responsivePanel = GetComponent<ResponsivePanel>();
        }

        private void OnEnable()
        {
            VisualElement root = document.rootVisualElement;
            if (root == null || !FindElements(root))
                return;

            bottomSheet = new BottomSheet(root);

            responsivePanel.LayoutChanged += ApplyLayout;
            responsivePanel.SafeAreaChanged += ApplySafeArea;
            leftColumn.RegisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
            sheet.RegisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
            subscribed = true;
            ApplyLayout(responsivePanel.IsPortrait);
        }

        private void OnDisable()
        {
            if (!subscribed)
                return;

            responsivePanel.LayoutChanged -= ApplyLayout;
            responsivePanel.SafeAreaChanged -= ApplySafeArea;
            leftColumn.UnregisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
            sheet.UnregisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
            subscribed = false;
            sheetContentHeight = float.NaN;

            bottomSheet.Dispose();
            bottomSheet = null;
        }

        private bool FindElements(VisualElement root)
        {
            dashboard = root.Require<VisualElement>("dashboard");
            topBar = root.Require<VisualElement>("top-bar");
            statusGroup = root.Require<VisualElement>("top-bar-status");
            stateExplanation = root.Require<VisualElement>("state-explanation");
            meta = root.Require<VisualElement>("top-bar-meta");
            leftColumn = root.Require<VisualElement>("left-column");
            energyCard = root.Require<VisualElement>("energy-card");
            sceneSpace = root.Require<VisualElement>("scene-space");
            rightColumn = root.Require<VisualElement>("right-column");
            trendCard = root.Require<VisualElement>("trend-card");
            curveCard = root.Require<VisualElement>("curve-card");
            dock = root.Require<VisualElement>("dock");
            speedGroup = root.Require<VisualElement>("sim-speed-group");
            xRayGroup = root.Require<VisualElement>("xray-group");
            lightsGroup = root.Require<VisualElement>("lights-group");
            sheet = root.Require<VisualElement>("sheet");
            sheetContent = root.Require<VisualElement>("sheet-content");
            trendsPage = root.Require<VisualElement>("sheet-page-trends");
            curvePage = root.Require<VisualElement>("sheet-page-curve");
            moreEnergy = root.Require<VisualElement>("sheet-more-energy");
            moreSpeed = root.Require<VisualElement>("sheet-more-speed");
            moreLights = root.Require<VisualElement>("sheet-more-lights");
            moreMeta = root.Require<VisualElement>("sheet-more-meta");
            sheetSafeArea = root.Require<VisualElement>("sheet-safe-area");

            return AllFound(dashboard, topBar, statusGroup, stateExplanation, meta, leftColumn, energyCard, sceneSpace,
                rightColumn, trendCard, curveCard, dock, speedGroup, xRayGroup, lightsGroup, sheet, sheetContent, trendsPage,
                curvePage, moreEnergy, moreSpeed, moreLights, moreMeta, sheetSafeArea);
        }

        private static bool AllFound(params VisualElement[] elements)
        {
            foreach (VisualElement element in elements)
            {
                if (element == null)
                    return false;
            }
            return true;
        }

        private void ApplyLayout(bool portrait)
        {
            // Detach everything that moves first, so the insert positions below are not shifted by the old places.
            dock.RemoveFromHierarchy();
            speedGroup.RemoveFromHierarchy();
            lightsGroup.RemoveFromHierarchy();
            stateExplanation.RemoveFromHierarchy();
            meta.RemoveFromHierarchy();
            energyCard.RemoveFromHierarchy();
            trendCard.RemoveFromHierarchy();
            curveCard.RemoveFromHierarchy();

            if (portrait)
            {
                dashboard.Insert(dashboard.IndexOf(sheet), dock);
                sceneSpace.Add(stateExplanation);
                trendsPage.Add(trendCard);
                curvePage.Add(curveCard);
                moreEnergy.Add(energyCard);
                moreSpeed.Add(speedGroup);
                moreLights.Add(lightsGroup);
                moreMeta.Add(meta);
            }
            else
            {
                // A sheet left open would pop up again on the next switch to portrait.
                bottomSheet.SetOpen(false);

                sceneSpace.Add(dock);
                dock.Insert(dock.IndexOf(xRayGroup), speedGroup);
                dock.Insert(dock.IndexOf(xRayGroup) + 1, lightsGroup);
                statusGroup.Add(stateExplanation);
                topBar.Add(meta);
                leftColumn.Add(energyCard);
                rightColumn.Add(trendCard);
                rightColumn.Add(curveCard);
            }

            ApplySafeArea(responsivePanel.SafeArea);
        }

        private void HandleGeometryChanged(GeometryChangedEvent evt) => FitSheetToCards();

        /// <summary>
        /// Sizes the sheet content so the open sheet ends just above the power card. The closed sheet reserves only its
        /// header (negative bottom margin = content + margin), so resizing the content never moves the cards: no
        /// layout feedback loop, the second GeometryChangedEvent finds the same height and stops.
        /// </summary>
        private void FitSheetToCards()
        {
            if (!responsivePanel.IsPortrait)
                return;

            // layout.y excludes the slide translate, so this is the closed position even while the sheet is open.
            float sheetTop = dashboard.worldBound.yMin + sheet.layout.y;
            float cardsTop = leftColumn.worldBound.yMin;
            if (float.IsNaN(sheetTop) || float.IsNaN(cardsTop))
                return;

            float contentHeight = Mathf.Round(Mathf.Clamp(sheetTop - cardsTop + SheetCoverMargin - SheetContentMarginTop,
                MinSheetContentHeight, MaxSheetContentHeight));
            if (contentHeight == sheetContentHeight)
                return;

            sheetContentHeight = contentHeight;
            float openOffset = contentHeight + SheetContentMarginTop;
            sheetContent.style.height = contentHeight;
            sheet.style.marginBottom = -openOffset;
            bottomSheet.OpenOffset = openOffset;
        }

        /// <summary>
        /// Keeps content out of the notch and the home indicator. Top and sides are plain padding (the 3D scene shows
        /// behind the notch, like an edge-to-edge game). At the bottom on phones the sheet still paints under the home
        /// indicator and only its spacer grows, so there is no strip of bare scene below the sheet.
        /// </summary>
        private void ApplySafeArea(SafeAreaInsets insets)
        {
            bool portrait = responsivePanel.IsPortrait;
            dashboard.style.paddingTop = insets.Top;
            dashboard.style.paddingRight = insets.Right;
            dashboard.style.paddingLeft = insets.Left;
            dashboard.style.paddingBottom = portrait ? 0f : insets.Bottom;
            sheetSafeArea.style.height = insets.Bottom;
        }
    }
}
