using System;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace WindFarm.UI
{
    /// <summary>
    /// Makes one UI layout work on phones and PCs, like a web page:
    ///   1. Scale: the panel is laid out in CSS-like logical pixels. A phone with a 3x screen gets scale 3, so a
    ///      14 px label has the same physical size in the WebGL build as on a web page.
    ///   2. Layout: toggles USS classes on the root (portrait / landscape / short / rotate) so the stylesheet can
    ///      rearrange the dashboard. USS has no media queries; this plays that role.
    ///   3. Safe area: reports the screen edges covered by a notch or the home indicator (logical px), so the layout
    ///      can keep controls out of them (CSS env(safe-area-inset-*) in WebGL, Screen.safeArea elsewhere, which
    ///      the Device Simulator also fills in).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ResponsivePanel : MonoBehaviour
    {
        public const string PortraitClass = "dashboard--portrait";
        public const string LandscapeClass = "dashboard--landscape";
        public const string ShortClass = "dashboard--short";
        public const string NarrowClass = "dashboard--narrow";
        public const string RotateClass = "dashboard--rotate";

        // Screens above this DPI are treated as phones in the Editor fallback (Device Simulator).
        private const float PhoneDpiThreshold = 200f;
        private const float PhoneReferenceDpi = 160f;   // Android dp / iOS point (~163)
        private const float DesktopReferenceDpi = 96f;  // CSS pixel on a desktop monitor

        [SerializeField, Range(0.5f, 2f), Tooltip("Extra multiplier on top of the detected pixel ratio. 1 = web page size.")]
        private float scaleMultiplier = 1f;

        [SerializeField, Min(0f), Tooltip("Below this logical width (px) the portrait (phone) layout is used even in landscape.")]
        private float minLandscapeWidth = 720f;

        [SerializeField, Min(0f), Tooltip("Landscape screens lower than this (logical px) get the compact 'short' layout.")]
        private float shortHeight = 560f;

        // A 1280-1366 px laptop browser window is narrower than the full PC layout: the dock pushed the side columns
        // together and the top bar texts overlapped (found when capturing the 1200 x 630 link preview image).
        [SerializeField, Min(0f), Tooltip("Landscape screens narrower than this (logical px) get the 'narrow' layout: " +
            "two-row dock, no state explanation in the top bar.")]
        private float narrowWidth = 1440f;

        [SerializeField, Min(0f), Tooltip("Mobile devices held sideways with a height below this (logical px) show a " +
            "'rotate your phone' hint over the portrait layout. Tablets stay above it.")]
        private float rotateHintMaxHeight = 600f;

        private UIDocument document;
        private VisualElement root;
        private int lastScreenWidth;
        private int lastScreenHeight;

#if UNITY_EDITOR
        private PanelScaleMode originalScaleMode;
        private float originalScale;
        private bool hasOriginalSettings;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern float WindFarm_GetCanvasCssWidth();

        [DllImport("__Internal")]
        private static extern float WindFarm_GetSafeAreaInset(int side);
#endif

        private bool hasLayout;

        /// <summary>Raised when the layout switches between portrait (true) and landscape (false), and once at startup.</summary>
        public event Action<bool> LayoutChanged;

        /// <summary>Raised when the safe-area insets change (rotation, browser toolbar, another device in the simulator).</summary>
        public event Action<SafeAreaInsets> SafeAreaChanged;

        /// <summary>True while the phone (portrait) layout is active.</summary>
        public bool IsPortrait { get; private set; }

        /// <summary>Screen edges covered by a notch or the home indicator, in logical px.</summary>
        public SafeAreaInsets SafeArea { get; private set; }

        private void Awake()
        {
            document = GetComponent<UIDocument>();

#if UNITY_EDITOR
            // PanelSettings is an asset: remember the values so leaving Play mode does not leave the asset modified.
            if (document.panelSettings != null)
            {
                originalScaleMode = document.panelSettings.scaleMode;
                originalScale = document.panelSettings.scale;
                hasOriginalSettings = true;
            }
#endif
        }

        private void OnEnable()
        {
            root = document.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning($"{nameof(ResponsivePanel)}: UIDocument has no root element (missing Source Asset?).", this);
                return;
            }

            root.RegisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
            ApplyScale();
        }

        private void OnDisable()
        {
            root?.UnregisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
            root = null;
        }

#if UNITY_EDITOR
        private void OnDestroy()
        {
            if (!hasOriginalSettings || document == null || document.panelSettings == null)
                return;

            document.panelSettings.scaleMode = originalScaleMode;
            document.panelSettings.scale = originalScale;
        }
#endif

        private void Update()
        {
            // Browser resize, rotation or moving the window to another monitor all change the canvas size.
            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
                ApplyScale();
        }

        private void ApplyScale()
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            PanelSettings settings = document.panelSettings;
            if (settings == null)
                return;

            float scale = Mathf.Clamp(DetectPixelRatio() * scaleMultiplier, 0.5f, 4f);
            if (settings.scaleMode != PanelScaleMode.ConstantPixelSize)
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            if (!Mathf.Approximately(settings.scale, scale))
                settings.scale = scale;

            SafeAreaInsets safeArea = DetectSafeArea(scale);
            if (safeArea.Equals(SafeArea))
                return;

            SafeArea = safeArea;
            SafeAreaChanged?.Invoke(safeArea);
        }

        /// <summary>Safe-area insets in logical px for the given panel scale (physical px per logical px).</summary>
        private static SafeAreaInsets DetectSafeArea(float scale)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Screen.safeArea is always the full screen in a browser; the page exposes the real insets as CSS values,
            // which are already in CSS (logical) px.
            return new SafeAreaInsets(WindFarm_GetSafeAreaInset(0), WindFarm_GetSafeAreaInset(1),
                WindFarm_GetSafeAreaInset(2), WindFarm_GetSafeAreaInset(3));
#else
            // Screen space has its origin bottom-left.
            Rect safe = Screen.safeArea;
            return new SafeAreaInsets((Screen.height - safe.yMax) / scale, (Screen.width - safe.xMax) / scale,
                safe.yMin / scale, safe.xMin / scale);
#endif
        }

        /// <summary>Physical pixels per logical (CSS) pixel.</summary>
        private static float DetectPixelRatio()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Exact: the canvas backing size divided by its CSS size. Also correct when the WebGL template lowers
            // devicePixelRatio for performance, because it measures what Unity actually renders.
            float cssWidth = WindFarm_GetCanvasCssWidth();
            if (cssWidth > 0f)
                return Screen.width / cssWidth;
#endif
            // Editor / other platforms: estimate from DPI. Phones are designed around ~160 dpi per logical pixel,
            // desktop monitors around 96 dpi (Windows 125 % scaling -> 120 dpi -> 1.25, same as a browser).
            float dpi = Screen.dpi;
            if (dpi <= 0f)
                return 1f;

            float reference = dpi >= PhoneDpiThreshold ? PhoneReferenceDpi : DesktopReferenceDpi;
            return Mathf.Max(1f, dpi / reference);
        }

        private void HandleGeometryChanged(GeometryChangedEvent evt)
        {
            float width = evt.newRect.width;
            float height = evt.newRect.height;
            if (width <= 0f || height <= 0f)
                return;

            // A phone held sideways has too little height for any dashboard layout, and iOS Safari cannot lock the
            // orientation. It keeps the portrait layout (no elements moving back and forth) under a rotate hint.
            // isMobilePlatform is true for mobile browsers in WebGL and in the Device Simulator; a short desktop
            // browser window never gets the hint.
            bool rotateHint = Application.isMobilePlatform && width > height && height < rotateHintMaxHeight;
            bool portrait = rotateHint || width < height || width < minLandscapeWidth;

            root.EnableInClassList(PortraitClass, portrait);
            root.EnableInClassList(LandscapeClass, !portrait);
            root.EnableInClassList(ShortClass, !portrait && height < shortHeight);
            root.EnableInClassList(NarrowClass, !portrait && width < narrowWidth);
            root.EnableInClassList(RotateClass, rotateHint);

            if (hasLayout && portrait == IsPortrait)
                return;

            hasLayout = true;
            IsPortrait = portrait;
            LayoutChanged?.Invoke(portrait);
        }
    }
}
