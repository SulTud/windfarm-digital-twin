mergeInto(LibraryManager.library, {
  // Opens a URL on the browser's next pointer / touch release. Browsers (iOS Safari above all) open a new tab only
  // inside a real user gesture, but Unity handles a click a frame later, outside that gesture, so a plain
  // Application.OpenURL can be blocked. C# calls this on the button's pointer down; the release that follows is the
  // gesture. mailto: links replace the location instead of opening an empty tab.
  WindFarm_OpenUrlOnRelease: function (urlPointer) {
    var url = UTF8ToString(urlPointer);
    var opened = false;

    function open() {
      if (opened) return;
      opened = true;
      document.removeEventListener("pointerup", open, true);
      document.removeEventListener("touchend", open, true);
      if (url.indexOf("mailto:") === 0) {
        window.location.href = url;
      } else {
        // No "noopener" feature: with it window.open always returns null, so a blocked popup could not be told
        // apart from an opened tab and the demo page navigated away as well. Cut the opener link by hand instead.
        var tab = window.open(url, "_blank");
        if (tab) {
          try { tab.opener = null; } catch (e) { }
        } else {
          window.location.href = url;
        }
      }
    }

    document.addEventListener("pointerup", open, true);
    document.addEventListener("touchend", open, true);
  },

  // Copies text to the clipboard on the next pointer / touch release (same user-gesture reason as above). Uses the
  // Clipboard API where the page is secure (HTTPS, e.g. GitHub Pages), else a hidden textarea + execCommand("copy"),
  // which also works on a plain-HTTP LAN test server.
  WindFarm_CopyOnRelease: function (textPointer) {
    var text = UTF8ToString(textPointer);
    var done = false;

    function fallback() {
      var area = document.createElement("textarea");
      area.value = text;
      area.setAttribute("readonly", "");
      area.style.cssText = "position:fixed;left:-9999px;top:0;opacity:0;";
      document.body.appendChild(area);
      area.select();
      try { document.execCommand("copy"); } catch (e) { }
      document.body.removeChild(area);
    }

    function copy() {
      if (done) return;
      done = true;
      document.removeEventListener("pointerup", copy, true);
      document.removeEventListener("touchend", copy, true);
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(text).catch(fallback);
      } else {
        fallback();
      }
    }

    document.addEventListener("pointerup", copy, true);
    document.addEventListener("touchend", copy, true);
  }
});
