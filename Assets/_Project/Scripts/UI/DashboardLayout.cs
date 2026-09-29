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
    ///                   button (UI Toolkit has no z-index; later siblings draw on top).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument), typeof(ResponsivePanel))]
    public sealed class DashboardLayout : MonoBehaviour
    {
        private UIDocument document;
        private ResponsivePanel responsivePanel;

        private VisualElement dashboard;
        private VisualElement sceneSpace;
        private VisualElement dock;
        private VisualElement sheet;
        private VisualElement statusGroup;
        private VisualElement stateExplanation;
        private bool subscribed;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            responsivePanel = GetComponent<ResponsivePanel>();
        }

        private void OnEnable()
        {
            VisualElement root = document.rootVisualElement;
            if (root == null)
                return;

            dashboard = root.Require<VisualElement>("dashboard");
            sceneSpace = root.Require<VisualElement>("scene-space");
            dock = root.Require<VisualElement>("dock");
            sheet = root.Require<VisualElement>("sheet");
            statusGroup = root.Require<VisualElement>("top-bar-status");
            stateExplanation = root.Require<VisualElement>("state-explanation");

            if (dashboard == null || sceneSpace == null || dock == null || sheet == null ||
                statusGroup == null || stateExplanation == null)
                return;

            responsivePanel.LayoutChanged += ApplyLayout;
            subscribed = true;
            ApplyLayout(responsivePanel.IsPortrait);
        }

        private void OnDisable()
        {
            if (!subscribed)
                return;

            responsivePanel.LayoutChanged -= ApplyLayout;
            subscribed = false;
        }

        private void ApplyLayout(bool portrait)
        {
            dock.RemoveFromHierarchy();
            stateExplanation.RemoveFromHierarchy();

            if (portrait)
            {
                dashboard.Insert(dashboard.IndexOf(sheet), dock);
                sceneSpace.Add(stateExplanation);
            }
            else
            {
                sceneSpace.Add(dock);
                statusGroup.Add(stateExplanation);
            }
        }
    }
}
