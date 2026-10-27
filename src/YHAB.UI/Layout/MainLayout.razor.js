export function initializeNavigation(blazor) {
    const navigationKey = 'yhab.navigation.collapsed';
    const progressDelay = 250;
    let navigationSequence = 0;
    let progressTimer = 0;
    let progressInterval = 0;
    let progressFinish = 0;
    let progressSafety = 0;
    const progressElement = () => document.getElementById('navigation-progress');
    const mainElement = () => document.getElementById('main-content');
    const resetProgress = () => {
        clearTimeout(progressTimer);
        clearInterval(progressInterval);
        clearTimeout(progressFinish);
        clearTimeout(progressSafety);
        const progress = progressElement();
        progress?.removeAttribute('data-active');
        progress?.style.setProperty('--navigation-progress', '0');
        mainElement()?.removeAttribute('data-navigating');
    };
    const beginNavigation = () => {
        const sequence = ++navigationSequence;
        clearTimeout(progressTimer);
        clearInterval(progressInterval);
        clearTimeout(progressFinish);
        clearTimeout(progressSafety);
        // A navigation that finishes quickly must not flash an indicator.
        progressTimer = setTimeout(() => {
            if (sequence !== navigationSequence) return;
            const progress = progressElement();
            if (!progress) return;
            progress.setAttribute('data-active', 'true');
            let value = 0.08;
            progress.style.setProperty('--navigation-progress', String(value));
            mainElement()?.setAttribute('data-navigating', 'true');
            progressInterval = setInterval(() => {
                value += (0.9 - value) * 0.14;
                progress.style.setProperty('--navigation-progress', value.toFixed(3));
            }, 180);
        }, progressDelay);
        // A request aborted by the browser never reports completion, so release
        // the old page instead of leaving it dimmed behind a stuck indicator.
        progressSafety = setTimeout(resetProgress, 8000);
    };
    const endNavigation = () => {
        navigationSequence++;
        clearTimeout(progressTimer);
        clearInterval(progressInterval);
        clearTimeout(progressSafety);
        const progress = progressElement();
        if (!progress?.hasAttribute('data-active')) {
            resetProgress();
            return;
        }
        progress.style.setProperty('--navigation-progress', '1');
        progressFinish = setTimeout(resetProgress, 220);
    };
    blazor.addEventListener('enhancednavigationstart', beginNavigation);
    blazor.addEventListener('enhancednavigationend', endNavigation);

    let navigationPreference = null;
    try {
        navigationPreference = localStorage.getItem(navigationKey);
    } catch {
        // Navigation still works when the browser disallows local storage.
    }
    const updateNavigation = () => {
        const navigationCollapsed = navigationPreference === 'true' ||
            (navigationPreference === null && /^\/plans\/[0-9a-f-]{36}(\/|$)/i.test(location.pathname));
        const navigation = document.getElementById('desktop-navigation');
        if (navigation) navigation.dataset.collapsed = String(navigationCollapsed);
        for (const toggle of document.querySelectorAll('[data-navigation-toggle]')) {
            toggle.setAttribute('aria-expanded', String(!navigationCollapsed));
            toggle.querySelector('[data-navigation-label]').textContent = navigationCollapsed ? 'Show menu' : 'Hide menu';
        }
    };
    document.addEventListener('click', event => {
        if (!event.target.closest('[data-navigation-toggle]')) return;
        navigationPreference = document.getElementById('desktop-navigation')?.dataset.collapsed === 'true' ? 'false' : 'true';
        updateNavigation();
        try {
            localStorage.setItem(navigationKey, navigationPreference);
        } catch {
            // Keep the current page usable without a persisted preference.
        }
    });
    const applyDocumentTheme = () => {
        // The root element paints scrollbars and native controls, so it must
        // agree with the resolved body theme, not only the body itself.
        const mode = blazor.theme?.getThemeSettings()?.mode ?? 'system';
        const dark = mode === 'dark' || (mode === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
        document.documentElement.dataset.theme = dark ? 'dark' : 'light';
    };
    const updateThemeSelector = () => {
        for (const select of document.querySelectorAll('[data-theme-select]')) {
            select.value = blazor.theme?.getThemeSettings()?.mode ?? 'system';
        }
    };
    document.addEventListener('change', (event) => {
        if (event.target.matches('[data-theme-select]')) {
            blazor.theme.setThemeMode(event.target.value);
            applyDocumentTheme();
            updateThemeSelector();
        }
    });
    matchMedia('(prefers-color-scheme: dark)').addEventListener('change', applyDocumentTheme);
    blazor.addEventListener('enhancedload', () => {
        // Enhanced navigation restores the server's body attributes. Reapply
        // Fluent's resolved theme so native controls and dark-mode CSS agree.
        blazor.theme?.setThemeMode(blazor.theme.getThemeSettings()?.mode ?? 'system');
        applyDocumentTheme();
        updateThemeSelector();
        updateNavigation();
    });
    applyDocumentTheme();
    updateThemeSelector();
    updateNavigation();
    // Static SSR doesn't execute FluentNavItem's interactive drawer-close handler.
    // Close the web component before Blazor patches the next page into the document.
    blazor.addEventListener('enhancednavigationstart', () => {
        for (const drawer of document.querySelectorAll('.app-layout fluent-drawer[hamburger]')) {
            if (typeof drawer.hide === 'function') {
                drawer.hide();
            }
        }
    });
}
