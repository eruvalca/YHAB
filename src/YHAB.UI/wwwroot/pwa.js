export function initializePwa(blazor) {
    if (!('serviceWorker' in navigator) || !window.isSecureContext) return;

    let registration;
    let installPrompt;
    let updateAvailable = false;
    let dismissed = false;
    let reloadRequested = false;
    let installMessage = '';
    let hadController = Boolean(navigator.serviceWorker.controller);
    const standalone = matchMedia('(display-mode: standalone)');
    const render = () => {
        const update = document.getElementById('pwa-update');
        if (update) update.hidden = !updateAvailable || dismissed;
        const help = document.querySelector('[data-pwa-install-help]');
        if (help) help.hidden = standalone.matches || navigator.standalone === true;
        const action = document.querySelector('[data-pwa-install-action]');
        if (action) action.hidden = !installPrompt;
        const message = document.querySelector('[data-pwa-install-message]');
        if (message) message.textContent = installMessage;
    };
    const offerUpdate = () => {
        updateAvailable = true;
        dismissed = false;
        render();
    };

    window.addEventListener('beforeinstallprompt', event => {
        event.preventDefault();
        installPrompt = event;
        render();
    });
    window.addEventListener('appinstalled', () => {
        installPrompt = undefined;
        installMessage = 'YHAB is installed.';
        render();
    });
    standalone.addEventListener('change', render);
    blazor.addEventListener('enhancedload', render);
    document.addEventListener('click', async event => {
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest('[data-pwa-install]') && installPrompt) {
            const prompt = installPrompt;
            installPrompt = undefined;
            render();
            try {
                await prompt.prompt();
                await prompt.userChoice;
            } catch {
                installMessage = 'Use your browser’s menu to install YHAB.';
            }
            render();
        }
        if (target?.closest('[data-pwa-later]')) {
            dismissed = true;
            render();
        }
        if (target?.closest('[data-pwa-reload]') && !reloadRequested) {
            if (!window.confirm('Reload YHAB now? Finish any save and copy any unsaved entries first. This page will reload.')) return;
            reloadRequested = true;
            if (registration?.waiting) {
                registration.waiting.postMessage({ type: 'ACTIVATE_UPDATE' });
            } else {
                window.location.reload();
            }
        }
    });
    navigator.serviceWorker.addEventListener('controllerchange', () => {
        // Only the tab whose user requested the update reloads. Other tabs may
        // contain unsaved forms or an uncertain write and must stay intact.
        if (reloadRequested) window.location.reload();
        else if (hadController) offerUpdate();
        hadController = true;
    });

    // The URL stays stable across deployments. Revalidate the generated worker
    // (including its content-hash version) rather than reuse HTTP cache.
    navigator.serviceWorker.register(new URL('service-worker.js', document.baseURI), { updateViaCache: 'none' })
        .then(worker => {
            registration = worker;
            if (worker.waiting && navigator.serviceWorker.controller) offerUpdate();
            const notifyWhenInstalled = event => {
                if (event.currentTarget.state === 'installed' && navigator.serviceWorker.controller) offerUpdate();
            };
            const observeInstalling = () => worker.installing?.addEventListener('statechange', notifyWhenInstalled);
            worker.addEventListener('updatefound', observeInstalling);
            // A navigation check or another tab may have started installation
            // before register() resolved and before this updatefound listener.
            observeInstalling();
        })
        .catch(() => {
            // Browsing remains usable if storage is disabled or registration fails.
        });
    let lastCheck = Date.now();
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState !== 'visible' || Date.now() - lastCheck < 60_000) return;
        lastCheck = Date.now();
        registration?.update().catch(() => { /* Recheck on a later visit when connected. */ });
    });
    render();
}
