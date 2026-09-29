// Serves a Unity WebGL build on the local network, so a phone on the same Wi-Fi can open it.
//
// Usage (Unity's bundled Node.js works, no install needed):
//   "<Unity>\Editor\Data\PlaybackEngines\WebGLSupport\BuildTools\Emscripten\node\node.exe" Tools/serve-webgl.js [buildDir] [port]
// Defaults: buildDir = Builds/WebGL, port = 8000. Then open http://<this PC's LAN IP>:<port> on the phone.
//
// Unity's compressed files (*.gz / *.br) are sent with the matching Content-Encoding header, so the browser
// decompresses them natively and no "Decompression Fallback" is needed. Browsers accept gzip over plain HTTP;
// Brotli usually only over HTTPS, so use Gzip (or Disabled) compression for LAN tests.

"use strict";

const http = require("http");
const fs = require("fs");
const os = require("os");
const path = require("path");

const root = path.resolve(process.argv[2] || "Builds/WebGL");
const port = Number(process.argv[3]) || 8000;

const mimeTypes = {
  ".html": "text/html; charset=utf-8",
  ".js": "application/javascript",
  ".wasm": "application/wasm",
  ".data": "application/octet-stream",
  ".json": "application/json",
  ".css": "text/css",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".ico": "image/x-icon",
  ".svg": "image/svg+xml",
};

const encodings = { ".gz": "gzip", ".br": "br" };

if (!fs.existsSync(path.join(root, "index.html"))) {
  console.error("No index.html in " + root + " -> build WebGL there first (File -> Build Profiles -> Web).");
  process.exit(1);
}

const server = http.createServer((request, response) => {
  let urlPath = decodeURIComponent(request.url.split("?")[0]);
  if (urlPath.endsWith("/"))
    urlPath += "index.html";

  const filePath = path.join(root, path.normalize(urlPath));
  if (!filePath.startsWith(root)) {
    response.writeHead(403);
    response.end();
    return;
  }

  fs.stat(filePath, (error, stats) => {
    if (error || !stats.isFile()) {
      response.writeHead(404);
      response.end("Not found");
      console.log("404 " + urlPath);
      return;
    }

    // "Build/x.wasm.gz" -> encoding gzip, content type from ".wasm".
    let extension = path.extname(filePath).toLowerCase();
    const headers = { "Content-Length": stats.size, "Cache-Control": "no-cache" };
    if (encodings[extension]) {
      headers["Content-Encoding"] = encodings[extension];
      extension = path.extname(filePath.slice(0, -extension.length)).toLowerCase();
    }
    headers["Content-Type"] = mimeTypes[extension] || "application/octet-stream";

    response.writeHead(200, headers);
    fs.createReadStream(filePath).pipe(response);
  });
});

server.listen(port, "0.0.0.0", () => {
  console.log("Serving " + root);
  const interfaces = os.networkInterfaces();
  Object.keys(interfaces).forEach((name) => {
    interfaces[name]
      .filter((address) => address.family === "IPv4" && !address.internal)
      .forEach((address) => console.log("  " + name + ": http://" + address.address + ":" + port));
  });
  console.log("Stop with Ctrl+C.");
});
