mergeInto(LibraryManager.library, {
  // Canvas width in CSS pixels. C# divides Screen.width by it to get the pixel ratio Unity really renders with.
  WindFarm_GetCanvasCssWidth: function () {
    var canvas = (typeof Module !== "undefined" && Module.canvas) || document.querySelector("canvas");
    return canvas ? canvas.getBoundingClientRect().width : 0;
  },

  // Safe-area inset in CSS pixels: 0 top, 1 right, 2 bottom, 3 left (notch, rounded corners, iOS home indicator).
  // CSS env() values can only be read through a computed style, so a hidden probe element carries them as padding.
  // They are non-zero only when the page's viewport meta has viewport-fit=cover (the WindFarm WebGL template does).
  WindFarm_GetSafeAreaInset: function (side) {
    var probe = document.getElementById("windfarm-safe-area-probe");
    if (!probe) {
      probe = document.createElement("div");
      probe.id = "windfarm-safe-area-probe";
      probe.style.cssText =
        "position:fixed;left:0;top:0;width:0;height:0;visibility:hidden;pointer-events:none;" +
        "padding:env(safe-area-inset-top,0px) env(safe-area-inset-right,0px) " +
        "env(safe-area-inset-bottom,0px) env(safe-area-inset-left,0px);";
      document.body.appendChild(probe);
    }

    var style = window.getComputedStyle(probe);
    var values = [style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft];
    return parseFloat(values[side]) || 0;
  }
});
