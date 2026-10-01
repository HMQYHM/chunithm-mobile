// Cache only the local studio shell. User skin files are never cached.
const CACHE = "skin-studio-shell-v1";
const ASSETS = ["index.html", "style.css", "app.js", "manifest.webmanifest"];
self.addEventListener("install", event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(ASSETS))));
self.addEventListener("fetch", event => {
  if (new URL(event.request.url).origin !== self.location.origin) return;
  event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request)));
});
