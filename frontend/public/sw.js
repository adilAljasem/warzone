// Lightweight passthrough Service Worker so Chrome/Android mints a real WebAPK
// while always fetching live updates directly from the server.
self.addEventListener("install", () => {
  self.skipWaiting();
});

self.addEventListener("activate", event => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("fetch", event => {
  if (event.request.mode === "navigate") {
    event.respondWith(fetch(event.request));
  }
});
