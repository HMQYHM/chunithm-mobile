// Cache only the local studio shell. User skin files are never cached.
const CACHE = "skin-studio-shell-v3";
const ASSETS = ["index.html", "style.css", "app.js", "live2d.css", "live2d-config.js", "live2d-panel.js", "manifest.webmanifest"];
self.addEventListener("activate", event => event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key.startsWith("skin-studio-shell-") && key !== CACHE).map(key => caches.delete(key))))));
self.addEventListener("install", event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(ASSETS))));
self.addEventListener("fetch", event => {
  if (new URL(event.request.url).origin !== self.location.origin) return;
  event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request)));
});
