/* Only public fallback assets are stored. Application pages and data stay online. */
// The build prepends self.yhabVersion from application content hashes.

const cachePrefix = 'yhab-offline-';
const cacheName = cachePrefix + self.yhabVersion;
const offlineUrl = new URL('offline.html', self.registration.scope).href;
const publicAssets = ['offline.html', 'pwa/offline.css', 'icons/icon-192.png']
    .map(path => new URL(path, self.registration.scope).href);

self.addEventListener('install', event => {
    // Do not skip waiting: the user chooses when to activate an update.
    event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(
        publicAssets.map(url => new Request(url, { cache: 'reload', credentials: 'omit' })))));
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        for (const key of await caches.keys()) {
            if (key.startsWith(cachePrefix) && key !== cacheName) await caches.delete(key);
        }
        await self.clients.claim();
    })());
});

self.addEventListener('message', event => {
    if (event.data?.type === 'ACTIVATE_UPDATE') event.waitUntil(self.skipWaiting());
});

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.method !== 'GET' || new URL(request.url).origin !== self.location.origin) return;
    if (publicAssets.includes(request.url)) {
        event.respondWith(caches.open(cacheName).then(async cache => (await cache.match(request)) || fetch(request)));
        return;
    }
    const isPage = request.mode === 'navigate' || request.headers.get('accept')?.includes('text/html; blazor-enhanced-nav=on');
    if (!isPage) return;

    event.respondWith((async () => {
        try {
            // Never store HTML, redirects, cookies, account forms, API data, or writes.
            return await fetch(request);
        } catch (error) {
            if (request.signal.aborted) throw error;
            const offline = await (await caches.open(cacheName)).match(offlineUrl);
            if (!offline) throw error;
            // Blazor also displays this error document for failed enhanced GETs.
            // A 503 cannot be mistaken for a successful application operation.
            return new Response(await offline.blob(), {
                status: 503,
                headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }
            });
        }
    })());
});
