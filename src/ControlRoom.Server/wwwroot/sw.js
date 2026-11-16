"use strict";

// Caches only the app shell. Backlog, story and job data are never cached, so an unreachable server shows the
// app's fixed text instead of old data. Bump the version when the shell list changes.
const CacheVersion = "v3";
const CacheName = "controlroom-shell-" + CacheVersion;

const Shell = [
  "/",
  "/index.html",
  "/app.css",
  "/app.js",
  "/board.js",
  "/token.js",
  "/manifest.webmanifest",
  "/icons/apple-touch-icon.png",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(CacheName).then((cache) => cache.addAll(Shell)));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((names) =>
      Promise.all(names.filter((name) => name !== CacheName).map((name) => caches.delete(name)))));
});

function isShellRequest(request) {
  if (request.method !== "GET") {
    return false;
  }
  const url = new URL(request.url);
  if (url.origin !== self.location.origin) {
    return false;
  }
  if (url.pathname.startsWith("/api/")) {
    return false;
  }
  return Shell.includes(url.pathname);
}

async function staleWhileRevalidate(event) {
  // One entry per shell path: the key is the bare pathname, so a query string never adds an entry.
  const key = new URL(event.request.url).pathname;
  const cache = await caches.open(CacheName);
  const cached = await cache.match(key);
  const network = fetch(event.request).then(async (response) => {
    if (response.ok) {
      await cache.put(key, response.clone());
    }
    return response;
  });
  if (cached) {
    event.waitUntil(network.catch(() => undefined));
    return cached;
  }
  return network;
}

self.addEventListener("fetch", (event) => {
  if (!isShellRequest(event.request)) {
    return;
  }
  event.respondWith(staleWhileRevalidate(event));
});
