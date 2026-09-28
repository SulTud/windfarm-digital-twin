using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    /// <summary>
    /// Moves dashboard elements between containers when the layout switches, for arrangements USS alone cannot express
    /// (like SetParent into another layout group):
    ///   Landscape (PC): the control dock sits centered at the bottom of the free space between the two columns,
    ///                   so it can never overlap the value cards or the charts.
    ///   Portrait (phone): the dock becomes a full-width row between the value cards and the bottom sheet.
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

            dashboard = root.Q("dashboard");
            sceneSpace = root.Q("scene-space");
            dock = root.Q("dock");
            sheet = root.Q("sheet");

            if (dashboard == null || sceneSpace == null || dock == null || sheet == null)
            {
                Debug.LogWarning($"{nameof(DashboardLayout)}: dashboard elements not found. Check the UXML names.", this);
                return;
            }

            responsivePanel.LayoutChanged += ApplyLayout;
            ApplyLayout(responsivePanel.IsPortrait);
        }

        private void OnDisable()
        {
            responsivePanel.LayoutChanged -= ApplyLayout;
        }

        private void ApplyLayout(bool portrait)
        {
            dock.RemoveFromHierarchy();

            if (portrait)
                dashboard.Insert(dashboard.IndexOf(sheet), dock);
            else
                sceneSpace.Add(dock);
        }
    }
}
