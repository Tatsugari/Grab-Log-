const CACHE = 'grablog-v5';
const ASSETS = ['/css/app.css?v=5', '/js/notifications.js', '/js/kiosk.js', '/logo.png'];

self.addEventListener('install', (e) => {
  e.waitUntil(
    caches.open(CACHE).then((c) => c.addAll(ASSETS).catch(() => {})).then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))
    ).then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (e) => {
  const url = new URL(e.request.url);
  if (e.request.method !== 'GET') return;

  // Always network-first for pages, API, CSS, JS
  if (
    url.pathname.startsWith('/api/') ||
    url.pathname === '/' ||
    url.pathname.startsWith('/Inventory') ||
    url.pathname.startsWith('/Index') ||
    url.pathname.startsWith('/Registry') ||
    url.pathname.startsWith('/Logs') ||
    url.pathname.startsWith('/Alerts') ||
    url.pathname.startsWith('/Operators') ||
    url.pathname.startsWith('/Notifications') ||
    url.pathname.endsWith('.css') ||
    url.pathname.endsWith('.js') ||
    url.search.includes('v=')
  ) {
    e.respondWith(
      fetch(e.request)
        .then((res) => {
          if (url.pathname.endsWith('.css') || url.pathname.endsWith('.js')) {
            const copy = res.clone();
            caches.open(CACHE).then((c) => c.put(e.request, copy)).catch(() => {});
          }
          return res;
        })
        .catch(() => caches.match(e.request))
    );
    return;
  }

  e.respondWith(
    fetch(e.request)
      .then((res) => {
        const copy = res.clone();
        caches.open(CACHE).then((c) => c.put(e.request, copy)).catch(() => {});
        return res;
      })
      .catch(() => caches.match(e.request))
  );
});
