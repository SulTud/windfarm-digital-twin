mergeInto(LibraryManager.library, {
  // Canvas width in CSS pixels. C# divides Screen.width by it to get the pixel ratio Unity really renders with.
  WindFarm_GetCanvasCssWidth: function () {
    var canvas = (typeof Module !== "undefined" && Module.canvas) || document.querySelector("canvas");
    return canvas ? canvas.getBoundingClientRect().width : 0;
  }
});
